using Kimi.AppKit.Web.Authentication;
using Kimi.AppKit.Web.BackgroundJobs;
using Kimi.AppKit.Web.ErrorHandling;
using Kimi.AppKit.Web.HealthChecks;
using Kimi.AppKit.Web.Localization;
using Kimi.AppKit.Web.OpenApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Kimi.AppKit.Web.Hosting;

/// <summary>
/// 标准 HTTP 管线的编排。
/// </summary>
/// <remarks>
/// 【为什么把顺序收进包】这条管线上有五处「写错就静默出 bug」的顺序约束，
/// 每一处都在真实环境里咬过人，而且**症状离根因极远**（见下面各步注释）。
/// 顺序不是消费方该拥有的决策——它没有业务含义，只有对错。
///
/// 【⚠️ 什么**没有**收进来】业务决策一律留在消费方的 Program.cs 显式书写：
/// 默认拒绝授权策略、DbContext 注册、CRUD 白名单（「本系统对外开放了哪些表」的
/// 唯一声明处）、各端点的 RequireAuthorization。
/// 那些即使值等于默认也应当写出来——**可见的冗余在这里是值得的**。
/// </remarks>
public static class KAppPipeline
{
    /// <summary>
    /// 按正确顺序装配标准管线，并映射框架自带的端点。
    /// </summary>
    /// <param name="app">应用。</param>
    /// <param name="configure">按需关闭某些部分。</param>
    /// <remarks>
    /// ⚠️ 调用它之后**只应再映射业务端点**。在它之前插入依赖端点路由的中间件
    /// 会破坏这里保证的顺序。
    /// </remarks>
    public static WebApplication MapAppKitApp(
        this WebApplication app, Action<KAppPipelineOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = new KAppPipelineOptions();
        configure?.Invoke(options);

        // ⚠️ **UseWebAssemblyDebugging 刻意不在这里。** 它来自
        //    Microsoft.AspNetCore.Components.WebAssembly.Server，把那个包拖进本包
        //    等于强加给所有消费方——包括根本没有 WASM 客户端的纯 API 服务。
        //    需要它的消费方在调用本方法**之前**自己调一行即可。
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler(options.ErrorPath, createScopeForErrors: true);
            app.UseHsts();
        }

        // ⚠️ **只对非 API 路径启用 SPA 状态码页。**
        //    它把 404/403 这类响应「重新执行」成错误页，对浏览器导航是对的，
        //    但对 API 是灾难：调用方拿到一大坨 text/html，response.json() 当场抛
        //    SyntaxError，而错误信息完全指不到「这其实是个 404」。
        //    ⚠️ 位置也重要：必须在这里而不是更靠后——状态码页要包住后续整条管线
        //    才能捕获到它们产生的状态码。
        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments(options.ApiPathPrefix),
            branch => branch.UseStatusCodePagesWithReExecute(
                options.NotFoundPath, createScopeForStatusCodePages: true));

        app.UseHttpsRedirection();

        // ⚠️ **必须显式调用，且必须排在状态码页之后。** 不写这一行时框架会把路由中间件
        //    自动插到**管线最前面**，于是状态码页重新执行请求时**不再经过路由匹配**，
        //    端点为 null，落进「默认拒绝」的兜底策略——未登录用户因此被 302 到 IdP。
        //    ⚠️ 症状离根因极远：浏览器请求一个已失效的 /_framework/*.pdb
        //    （发布后浏览器还缓存着旧 index.html 就会发生），本该拿到 404 自愈，
        //    实际收到跨域重定向 → 控制台只报 CORS 被拦 → mono 加载失败 →
        //    WASM 起不来 → 页面底部弹出 Blazor 那条黄色「未处理错误」条。
        //    整条链上没有任何一处提到「授权」或「中间件顺序」。
        app.UseRouting();

        // ⚠️ 必须排在静态文件之前。官方约束是「在任何可能读取请求 culture 的中间件之前」，
        //    并点名 UseStaticFiles 作为例子。放错是**静默**的——管线照常工作。
        if (options.RequestLocalization)
        {
            app.UseAppKitRequestLocalization(options.DefaultCulture, options.SupportedCultures);
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        // ⚠️ 必须 AllowAnonymous。MapStaticAssets 是**端点路由**，会被默认拒绝策略管到；
        //    而它取代的 UseStaticFiles 是**中间件**，压根不走端点授权——
        //    这个差异是从 UseStaticFiles 迁到 MapStaticAssets 时最容易漏的一步。
        //    漏掉的表现极具迷惑性：页面照常返回 200（预渲染的 HTML 出得来），
        //    但每个 css/js 请求都被 302 到登录页并收到一份 HTML，
        //    浏览器把 HTML 当 JS 解析，控制台只有一句 "Unexpected token '<'"。
        app.MapStaticAssets().AllowAnonymous();

        if (options.HealthChecks) app.MapAppHealthChecks();
        if (options.OpenApi) app.MapAppKitOpenApi(options.OpsPolicy);
        if (options.HangfireDashboard && options.OpsPolicy is { Length: > 0 } opsPolicy)
        {
            app.MapAppKitHangfireDashboard(opsPolicy);
        }

        if (options.AuthenticationEndpoints) app.MapAppKitAuthenticationEndpoints(options);

        // ⚠️ 未命中的 API 必须自己接住并回 problem+json。不接的话它没有端点，
        //    落进默认拒绝策略，未登录调用方收到的是 **302 + 一页登录 HTML**，
        //    response.json() 当场抛 SyntaxError，错误信息完全指不到「这个地址不存在」。
        //    ⚠️ AllowAnonymous 是刻意的：地址存不存在不是秘密，
        //    而把「不存在」伪装成「要登录」只会让调用方查错查到别处去。
        if (options.ApiNotFoundFallback)
        {
            app.MapFallback($"{options.ApiPathPrefix}/{{**path}}", (HttpContext http) =>
                    TypedResults.Problem(
                        title: "接口不存在",
                        detail: $"没有匹配 {http.Request.Path} 的接口。",
                        statusCode: StatusCodes.Status404NotFound))
                .AllowAnonymous();
        }

        return app;
    }

    /// <summary>登录 / 登出 / 现场密码 / 扫码 四组认证端点。</summary>
    /// <remarks>
    /// ⚠️ 全部做成服务端 minimal API 而非 Blazor 页面：它们要在**响应**里
    /// 种 Cookie 或发挑战重定向，交互式组件只能改自己的渲染树，改不了 HTTP 响应。
    /// </remarks>
    private static void MapAppKitAuthenticationEndpoints(
        this WebApplication app, KAppPipelineOptions options)
    {
        // ⚠️ 登录入口**直接发起挑战，不渲染任何页面**。
        //    做成「只有一个登录按钮的中间页」等于让用户多点一次，纯属噪音。
        //    ⚠️ 未配 IdP 时落到现场密码通道，而不是给一个 503 死路：
        //    没有 IdP 的部署（纯脱域现场）本来就只有那条路可走。
        app.MapGet(options.LoginPath, (string? returnUrl, IOptions<KOidcOptions> oidc) =>
                oidc.Value.IsConfigured
                    ? Results.Challenge(
                        new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
                        [KAuthenticationSchemes.Oidc])
                    : Results.Redirect(options.PasswordLoginPath))
            .AllowAnonymous();

        // ⚠️ 未配 IdP 时返回 503 而不是发起挑战：那种情况下 DefaultChallengeScheme
        //    会退回 Cookie，而 Cookie 挑战就是重定向到 LoginPath，于是登录页把自己
        //    转回自己——浏览器上表现为 ERR_TOO_MANY_REDIRECTS，日志里什么也看不出来。
        app.MapGet("/authentication/login", (string? returnUrl, IOptions<KOidcOptions> oidc) =>
                oidc.Value.IsConfigured
                    ? Results.Challenge(
                        new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
                        [KAuthenticationSchemes.Oidc])
                    : Results.Problem(
                        "尚未配置 OpenIDConnect:Issuer / ClientId，单点登录不可用。",
                        statusCode: StatusCodes.Status503ServiceUnavailable))
            .AllowAnonymous();

        // ⚠️ 登出必须是服务端端点——会话是服务端 Cookie，WASM 端清不掉它。
        //    客户端只在自己那边改认证态的话，界面显示已登出、下一次请求却仍带着有效 Cookie。
        //    ⚠️ 配了 OIDC 时还要通知 IdP 结束会话（单点登出），否则用户点了登出、
        //    下次点登录会**无感知地自动登回来**——因为 IdP 那边的会话还在。
        app.MapGet("/authentication/logout", async (HttpContext http, IOptions<KOidcOptions> oidc) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)
                .ConfigureAwait(false);

            return oidc.Value.IsConfigured
                ? Results.SignOut(
                    new AuthenticationProperties { RedirectUri = "/" }, [KAuthenticationSchemes.Oidc])
                : Results.Redirect("/");
        }).AllowAnonymous();

        if (options.PasswordLogin) app.MapAppKitPasswordLogin();
        if (options.QrLogin) app.MapAppKitQrLogin();
        if (options.ErrorPage) app.MapAppKitErrorPage();
    }
}
