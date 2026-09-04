using Kimi.AppKit.Components;
using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Data;
using Kimi.AppKit.Data.Providers;
using Kimi.AppKit.Web.HealthChecks;
using Kimi.AppKit.Web.Identity;
using KMoldApp.Data;
using KMoldApp.Infrastructure;
using Kimi.AppKit.Web.Authentication;
using Kimi.AppKit.Web.Authorization;
using KMoldApp.Client.Pages;
using KMoldApp.Components;
using KMoldApp.Shared.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
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

builder.Services.AddControllers();
builder.Services.AddCascadingAuthenticationState();

// 把服务端已认证的身份送给 WASM 端（另一个进程，拿不到 HttpContext）。
builder.Services.AddScoped<AuthenticationStateProvider, PersistingAuthenticationStateProvider>();

// 开发期权限绕过。⚠️ 默认关闭，要用必须在 appsettings 里显式开
// （Auth:RoleBypass:Enabled）；生产环境即使配了也不生效。
// 前身是「非生产环境默认开启」，那让权限相关的 bug 在 Staging 根本测不出来。
var roleBypass = new RoleBypassOptions();
builder.Configuration.GetSection(RoleBypassOptions.SectionName).Bind(roleBypass);
builder.Services.AddSingleton(new RoleBypassGate(roleBypass.Enabled, builder.Environment.IsProduction()));
builder.Services.AddTransient<IClaimsTransformation, RoleBypassClaimsTransformation>();

// 审计字段的「谁干的」来自这里。
// ⚠️ HttpContextCurrentUser 依赖 IHttpContextAccessor，**必须一并注册**——
//    漏了它启动就失败（启动期 DI 校验抓的），而错误信息指向 IKCurrentUser 不是这一行。
// ⚠️ 后台任务（Hangfire/托管服务）里没有 HttpContext，拿到的永远是哨兵值；
//    那类场景应另注册一个固定系统身份的实现，别让审计表把任务写的记录记成匿名。
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IKCurrentUser, HttpContextCurrentUser>();
builder.Services.AddSingleton(TimeProvider.System);

// ⚠️ 用 AddDbContextFactory 而不是 AddDbContext。自 EF Core 5 起（dotnet/efcore#25164）
//    它**同时**把 DbContext 注册成 scoped，构造注入一处都不用改；额外得到的
//    IDbContextFactory 是 Kimi.AppKit.Data 里 CRUD 数据源的必需依赖——它们刻意不接受
//    scoped 上下文，因为 Blazor 会并发渲染同一棵树里的组件，抢同一个 DbContext 会抛
//    "A second operation was started on this context instance"。
// ⚠️ lifetime 显式给 Scoped：默认是 Singleton，那会让 scoped 的拦截器被单例长期持有
//    （captive dependency）。
builder.Services.AddDbContextFactory<KMoldDbContext>((sp, options) =>
{
    var provider = DatabaseProviderSetup.Resolve(builder.Configuration[DatabaseProviderSetup.ConfigKey]);
    options.Apply(provider, builder.Configuration.GetConnectionString("DefaultConnection"));

    // ⚠️ 只在开发环境开。EnableSensitiveDataLogging 会把**参数值原文**写进日志——
    //    登录、改密、导入这类请求的明文凭据与个人信息会直接落到日志文件里，
    //    而日志的访问控制通常远松于数据库。
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
}, ServiceLifetime.Scoped);

// 事务的唯一入口。⚠️ 不要在业务代码里裸调 BeginTransactionAsync：
// 一旦为生产可靠性开启 EnableRetryOnFailure，裸事务会抛
// "The configured execution strategy does not support user-initiated transactions"，
// 而重试通常只在生产开，开发和测试环境从不触发。
builder.Services.AddScoped(sp => new UnitOfWork(sp.GetRequiredService<KMoldDbContext>()));

builder.Services.AddAppHealthChecks<KMoldDbContext>();

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

// 登出：清 Cookie 会话。
// ⚠️ 必须是服务端端点——会话是服务端 Cookie，WASM 端清不掉它。
//    客户端只在自己那边改认证态的话，界面显示已登出、下一次请求却仍带着有效 Cookie。
// ⚠️ 配了 OIDC 时还要通知 IdP 结束会话（单点登出），否则用户点了登出、
//    下次点登录会**无感知地自动登回来**——因为 IdP 那边的会话还在。
app.MapGet("/authentication/logout", async (HttpContext http, IOptions<KOidcOptions> oidc) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    if (oidc.Value.IsConfigured)
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [KAuthenticationSchemes.Oidc]);

    return Results.Redirect("/");
}).AllowAnonymous();

app.MapControllers();

// 健康检查：/health/live（进程存活）与 /health/ready（含数据库连通性）。
// ⚠️ 两个端点在包里已带 AllowAnonymous——探针不可能先登录。
app.MapAppHealthChecks();

app.Run();
