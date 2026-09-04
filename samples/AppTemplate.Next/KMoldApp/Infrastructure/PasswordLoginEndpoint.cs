using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Web.Authentication;
using Kimi.AppKit.Web.Authorization;
using KMoldApp.Components.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace KMoldApp.Infrastructure;

/// <summary>
/// 脱域现场的密码登录端点。
/// </summary>
/// <remarks>
/// 【为什么需要它】现场机器可能不在域内、拿不到可信证书，走不了 OIDC 授权码流程。
/// 这条路径用 OAuth ROPC（<c>grant_type=password</c>）直接换令牌。
///
/// ⚠️ **ROPC 会把用户名和明文密码发到 IdP**，因此：
/// 必须过网络准入（只允许现场网段）、必须走 HTTPS 或受控内网、
/// 且**绝不能**在这条链路上关闭证书校验。
///
/// ⚠️ **端点做成服务端 minimal API 而不是 Blazor 组件**：登录要在**响应**里种 Cookie，
/// 交互式组件只能改自己的渲染树，改不了 HTTP 响应头。
/// </remarks>
public static class PasswordLoginEndpoint
{
    private static readonly string[] BlockedReturnPrefixes = ["/login", "/password-login", "/authentication"];

    /// <summary>映射密码登录端点。</summary>
    public static IEndpointRouteBuilder MapPasswordLogin(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // ⚠️ 登录页由端点渲染，**不是**路由表里的 Blazor 页面。
        //    服务端页面配上 @page 也能 SSR 出来，但 WASM 接管后会被清空（实测，零报错）。
        //    完整理由见 Components/Account/AuthLayout.razor 顶部。
        endpoints.MapGet("/password-login", RenderAsync).AllowAnonymous();

        endpoints.MapPost("/authentication/password-login", HandleAsync)
            .AllowAnonymous();

        return endpoints;
    }

    private static IResult RenderAsync(
        HttpContext http, KNetworkGate networkGate, string? returnUrl, string? error) =>
        new RazorComponentResult<PasswordLoginPage>(new
        {
            Gate = networkGate.Evaluate(http),
            ReturnUrl = returnUrl,
            Error = error,
            Host = http.Request.Host.Value,
        });

    /// <remarks>
    /// ⚠️ 三个字段用 <c>[FromForm]</c> **显式绑定**，不要图省事去 <c>ReadFormAsync</c>
    /// 手取。带表单绑定的 minimal API 端点才会被打上防伪元数据，
    /// <c>UseAntiforgery</c> 中间件据此校验令牌；手取表单的端点**没有那份元数据，
    /// 中间件会直接放行**——防伪形同虚设，而页面上那个 &lt;AntiforgeryToken /&gt;
    /// 还在，看起来一切正常。
    /// </remarks>
    private static async Task<IResult> HandleAsync(
        HttpContext http,
        [FromForm] string? username,
        [FromForm] string? password,
        [FromForm] string? returnUrl,
        KNetworkGate networkGate,
        IKOidcTokenService tokenService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // ⚠️ 网络准入在这里**再判一次**，不能只靠页面渲染时那次——
        //    页面上的判断只是避免让人白填，端点才是真正的防线。
        //    直接 POST 这个地址是绕不过去的。
        if (networkGate.Evaluate(http) != KNetworkGateResult.Allowed)
            return Redirect("/password-login", null, "当前网络不允许使用密码登录。");

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Redirect("/password-login", returnUrl, "请填写工号与密码。");

        KTokenResponse? token;
        try
        {
            token = await tokenService
                .PasswordLoginAsync(username, password, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // ⚠️ 令牌服务在 IdP 返回非 2xx 时**抛异常**，不是返回 null。
            //    凭据错误走的正是这条路——不捕获的话每次输错密码都是一个 500。
            // ⚠️ 异常消息里带着 IdP 的 error_description（可能区分「用户不存在」
            //    与「密码错误」），**只能进日志，绝不能回显**。
            loggerFactory.CreateLogger(typeof(PasswordLoginEndpoint))
                .LogWarning(ex, "密码登录失败，IdP 拒绝了令牌请求。");
            token = null;
        }

        // ⚠️ 只回一条**分类**消息，不区分「用户不存在」与「密码错误」——
        //    区分了就等于给攻击者一个账号存在性预言机。
        if (token?.IdToken is null)
            return Redirect("/password-login", returnUrl, "工号或密码不正确。");

        // 签发会话与扫码登录共用一份（见 CookieSignIn）——两条通道的 claim 口径必须一致。
        await CookieSignIn.SignInAsync(http, token).ConfigureAwait(false);

        // ⚠️ 回跳地址必须过 KReturnUrl.Sanitize，否则是开放重定向漏洞。
        //    ⚠️ 同时把登录相关路径列进 blockedPrefixes：跳回登录页会让用户看到
        //    「登录成功了却还在登录页」，与登录失败无法区分。
        return Results.Redirect(KReturnUrl.Sanitize(returnUrl, BlockedReturnPrefixes));
    }

    private static IResult Redirect(string path, string? returnUrl, string error)
    {
        var query = $"?error={Uri.EscapeDataString(error)}";
        if (!string.IsNullOrWhiteSpace(returnUrl))
            query += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";

        return Results.Redirect(path + query);
    }
}
