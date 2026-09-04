using Kimi.AppKit.Components;
using Kimi.AppKit.Web.Localization;
using Kimi.AppKit.Web.BackgroundJobs;
using Kimi.AppKit.Web.Email;
using Kimi.AppKit.Observability;
using Hangfire.PostgreSql;
using Hangfire;
using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Data;
using Kimi.AppKit.Data.Providers;
using Kimi.AppKit.Web.Crud;
using Kimi.AppKit.Web.Excel;
using Kimi.AppKit.Web.Hosting;
using Kimi.AppKit.Web.HealthChecks;
using Kimi.AppKit.Web.Identity;
using KMoldApp.Data;
using Kimi.AppKit.Crud.Http;
using KMoldApp.Data.Entities;
using KMoldApp.Shared.Entities;
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
    .AddInteractiveWebAssemblyComponents()
    .AddInteractiveServerComponents();

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
    // 策略定义在 KMoldApp.Shared，与客户端共用同一份——两端各写一遍必然漂移。
    options.AddAppPolicies();
});

// 脱域现场的密码登录（ROPC）所需的两样。
// ⚠️ 网络准入的白名单**空 = 拒绝**（Auth:NetworkGate:AllowedSubnets）。方向是刻意的：
//    忘了配等于更严。本地调试要放行回环得显式开 AllowLoopback。
builder.Services.AddAppKitNetworkGate(builder.Configuration);
builder.Services.AddAppKitOidcTokenService();

// <KEnvChip /> 的依赖。⚠️ 漏了它那个组件渲染时抛——而它恰恰是「这不是生产环境」
// 的可见标识，渲染不出来时一个配错环境变量的实例看起来和正式站一模一样。
builder.Services.AddAppKitQrLogin(builder.Configuration);
builder.Services.AddAppKitEnvironment();

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

// 可观测性单独成包：OTel 是十个 NuGet 依赖，不该强加给只想要健康检查的消费方。
// ⚠️ 端点走 OTel 标准环境变量（OTEL_EXPORTER_OTLP_ENDPOINT），未配置则跳过注册——
//    本地开发不必先架一套采集端。
builder.AddAppKitObservability();

// 邮件走 MailKit。⚠️ 不用 System.Net.Mail.SmtpClient——微软官方明示「不应用于新开发」，
//    它的 TLS 与认证方式跟不上（Office 365 已要求 OAuth2）。
builder.Services.AddAppKitEmail(builder.Configuration);

// 后台任务。⚠️ Kimi.AppKit.Web 刻意不引用 Hangfire.PostgreSql（那会把 PG 驱动强加给
//    只用 SQL Server 的消费方），所以 PG 存储由本层经 configurePostgres 接线。
var hangfireConnection = builder.Configuration.GetConnectionString("DefaultConnection")!;
var hangfireProvider = DatabaseProviderSetup.Resolve(builder.Configuration[DatabaseProviderSetup.ConfigKey]);
builder.Services.AddAppKitHangfire(
    hangfireProvider,
    hangfireConnection,
    configurePostgres: c => c.UsePostgreSqlStorage(o => o.UseNpgsqlConnection(hangfireConnection)));
builder.Services.AddHangfireServer();

// ⚠️ 这是**开放面白名单**：没登记的实体既解析不出数据源、也映射不出端点。
//    前身把「读写任意表」压缩成一个通用端点，于是「这个系统对外开放了哪些表」
//    在代码里没有任何一处在说明，权限判断因此无处可挂。
// CRUD 端点自带 Excel 导出/导入，故需要 IExcelService。
// ⚠️ 漏了它会在启动时抛，且包的错误信息直接给出修法——这类「装配不全」
//    就该在启动期炸掉，而不是等用户点导出时才 500。
builder.Services.AddAppKitExcel();

// ⚠️ 与客户端的 AddHttpCrudDataSource<SettingDto>() **成对使用**：
//    服务端在预渲染 WASM 页面时也要实例化它的注入属性，即使 prerender:false。
//    漏了这份注册整页 500，且错误信息指向组件属性注入、不指向这里。
builder.Services.AddPrerenderCrudDataSource<SettingDto>();

builder.Services.AddKCrud<KMoldDbContext>()
    .AddEntity<Setting>()
    .AddEntity<EmailTemplate>();

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
// ⚠️ **只对非 API 路径启用 SPA 状态码页。**
//    它会把 404/403 这类响应「重新执行」成 /not-found 页面，对浏览器导航是对的，
//    但对 API 是灾难：调用方拿到一大坨 text/html，response.json() 当场抛
//    SyntaxError，而错误信息完全指不到「这其实是个 404」。
//    ⚠️ 顺序也重要：UseWhen 必须在这里而不是更靠后——状态码页要包住后续整条管线
//    才能捕获到它们产生的状态码。
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();

// ⚠️ **必须显式调用，且必须排在状态码页之后。** 不写这一行时框架会把路由中间件
//    自动插到**管线最前面**，于是 UseStatusCodePagesWithReExecute 重新执行请求时
//    **不再经过路由匹配**，端点为 null，落进「默认拒绝」的兜底策略——
//    未登录用户因此被 302 到 IdP。
//    ⚠️ 症状离根因极远：浏览器请求一个已失效的 /_framework/*.pdb（发布后缓存了旧
//    index.html 就会发生），本该拿到 404 自愈，实际收到跨域重定向，
//    控制台只报 CORS 被拦，接着 mono 加载失败、WASM 起不来，
//    最后表现为页面底部弹出 Blazor 那条黄色「未处理错误」条。
//    整条链上没有任何一处提到「授权」或「中间件顺序」。
app.UseRouting();

// ⚠️ 必须排在静态文件中间件**之前**。官方约束是「在任何可能读取请求 culture 的
//    中间件之前」，并点名 UseStaticFiles 作为例子。放错是静默的——管线照常工作。
app.UseAppKitRequestLocalization(defaultCulture: "en", supportedCultures: ["en", "zh-CN"]);

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
    .AddInteractiveServerRenderMode()
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

// 脱域现场的密码登录（ROPC）。页面在 Components/Account/PasswordLoginPage.razor。
app.MapPasswordLogin();

// 未处理异常的落地页，配合上面的 UseExceptionHandler("/Error")。
app.MapErrorPage();

// 扫码登录：域内打印加密卡片，现场用扫码枪扫入登录。
// ⚠️ 卡片等价于一张写着密码的便条，缓解全靠有效期 + 网络准入 + 卡面提示三条一起。
app.MapQrLogin();

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

// 每实体一组 CRUD 端点，读写各自挂授权。
// ⚠️ 出厂即 RequireAuthorization()；这里再显式收紧到管理员——设置与邮件模板
//    属于系统配置，普通登录用户不该能读写。
app.MapCrudEndpoints<Setting>().RequireAuthorization(AppPolicies.AdminOnly);
app.MapCrudEndpoints<EmailTemplate>().RequireAuthorization(AppPolicies.AdminOnly);

app.MapControllers();

// ⚠️ 未命中的 /api/* 必须自己接住并回 problem+json。不接的话它没有端点，
//    落进「默认拒绝」的兜底策略，未登录调用方收到的是 **302 + 一页登录 HTML**，
//    `response.json()` 当场抛 SyntaxError，错误信息完全指不到「这个地址不存在」。
//    ⚠️ 挂 AllowAnonymous 是刻意的：地址存不存在不是秘密，
//    而把「不存在」伪装成「要登录」只会让调用方查错查到别处去。
app.MapFallback("/api/{**path}", (HttpContext http) =>
        TypedResults.Problem(
            title: "接口不存在",
            detail: $"没有匹配 {http.Request.Path} 的接口。",
            statusCode: StatusCodes.Status404NotFound))
    .AllowAnonymous();

// 健康检查：/health/live（进程存活）与 /health/ready（含数据库连通性）。
// ⚠️ 两个端点在包里已带 AllowAnonymous——探针不可能先登录。
app.MapAppHealthChecks();

app.Run();
