using Kimi.AppKit.Components;
using Kimi.AppKit.Web.Authentication;
using Kimi.AppKit.Web.Authorization;
using KMoldApp.Client.Pages;
using KMoldApp.Components;
using KMoldApp.Shared.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// ⚠️ MudBlazor 与对话框服务**两端都要注册**：WASM 端给真正跑在浏览器里的组件用；
//    服务端在**预渲染**含这些组件的页面时也要能解析它们。
//    少了服务端这份，首页直接 500：
//    "Cannot provide a value for property 'PopoverService' on type 'MudBlazor.MudPopoverProvider'"。
//    ⚠️ 这不是「客户端注册就够了」——预渲染跑在服务端进程里，用的是服务端的容器。
builder.Services.AddMudServices();
builder.Services.AddAppKitDialogs();

// 认证装配来自 Kimi.AppKit.Web。登录页路径是本应用的策略，因此从这里传。
// ⚠️ 未配 OpenIDConnect:Issuer / ClientId 时会跳过 OIDC 与 JwtBearer 注册并在控制台
//    打印提示，应用照常启动——本地起一个空壳来看界面不需要先架一台 IdP。
builder.Services.AddAppKitAuthentication(builder.Configuration, o =>
{
    o.LoginPath = "/login";
});

// ⚠️ 默认拒绝：没显式标注的端点一律要求登录。安全默认值应当是「忘了标注就拦下」，
//    而不是裸 AddAuthorization() 那样「忘了标注就裸奔」。
//    必须匿名的端点（健康检查、首屏引导）显式调 .AllowAnonymous()。
builder.Services.AddDefaultDenyAuthorization(options =>
{
    // ⚠️ 角色判断走**策略**，不要用自定义授权特性。策略名与角色名都来自 KMoldApp.Shared
    //    的常量——两端共用同一份定义，手写字面量写错不会有编译错误，只在有人真的
    //    访问那个端点时抛 "The AuthorizationPolicy named ... was not found"。
    // ⚠️ 只 AddPolicy，**不要在这里重设 FallbackPolicy**：包里的那份带着
    //    /_framework 放行（见 AuthorizationSetup.BlazorFrameworkPath），覆盖掉它
    //    会让 WASM 起不动，且症状指不到授权。
    options.AddPolicy(AppPolicies.AdminOnly,
        AuthorizationSetup.RequireAnyRole(AppRoles.Root, AppRoles.Admin));
});

builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// ⚠️ 必须 AllowAnonymous。MapStaticAssets 是**端点路由**，会被上面的 FallbackPolicy
//    （默认拒绝）管到；而它取代的 UseStaticFiles 是**中间件**，压根不走端点授权——
//    这个差异是从 UseStaticFiles 迁到 MapStaticAssets 时最容易漏的一步。
//    漏掉的表现极具迷惑性：页面照常返回 200（服务端预渲染的 HTML 出得来），
//    但每一个 css/js 请求都被 302 到登录页并收到一份 HTML，
//    浏览器把 HTML 当 JS 解析，控制台只有一句 "Unexpected token '<'"，
//    完全指不到「静态资源被授权拦了」这个真正原因。
app.MapStaticAssets().AllowAnonymous();

// ⚠️ Blazor 端点必须显式 AllowAnonymous，否则会被上面的 FallbackPolicy（默认拒绝）拦住。
//    Blazor 的鉴权层是组件级的 AuthorizeRouteView 与页面上的 [Authorize]，不是端点级策略；
//    端点级一刀切会把**登录页自己**也要求登录，症状是登录后又被弹回登录页的重定向死循环。
//    这里放行的只是「能不能到达 Blazor 管线」，受保护页面照旧由各自的 [Authorize] 把关。
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(KMoldApp.Client._Imports).Assembly)
    .AllowAnonymous();

// OIDC 登录入口。做成服务端端点而非 Blazor 页面，因为 Challenge 要在**响应**里
// 发重定向与相关性 cookie，交互式组件做不到这件事。
// ⚠️ 未配 IdP 时返回 503 而不是发起挑战：那种情况下 DefaultChallengeScheme 会退回
//    Cookie，而 Cookie 挑战就是重定向到 LoginPath，于是登录页把自己转回自己——
//    浏览器上表现为 ERR_TOO_MANY_REDIRECTS，日志里什么也看不出来。
app.MapGet("/authentication/login", (string? returnUrl, IOptions<KOidcOptions> oidc) =>
        oidc.Value.IsConfigured
            ? Results.Challenge(
                new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
                [KAuthenticationSchemes.Oidc])
            : Results.Problem(
                "尚未配置 OpenIDConnect:Issuer / ClientId，单点登录不可用。",
                statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

app.Run();
