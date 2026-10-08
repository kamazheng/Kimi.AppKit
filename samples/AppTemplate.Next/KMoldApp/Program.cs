using Kimi.AppKit.Core.Contracts;
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
using Kimi.AppKit.Components.Auth;
using Kimi.AppKit.Web.Authentication;
using Kimi.AppKit.Web.Branding;
using Kimi.AppKit.Web.ErrorHandling;
using Kimi.AppKit.Web.OpenApi;
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

// 整套基建：认证、网络准入、令牌服务、扫码、运行环境、企业标识、
// 认证态两端传递、API 文档、Excel、开发期角色绕过。
// ⚠️ 收进包的都是「漏一个就炸、且错误信息指向别处」的纯装配；
//    安全边界（默认拒绝、开放哪些表）仍显式写在下面——见 KAppServices 的注释。
builder.AddAppKitApp(o =>
{
    o.LoginPath = "/login";

    // ⚠️ 注入哪些角色由本应用决定——角色名是业务身份，包里不该替我们定义权限模型。
    o.InjectBypassRoles = identity => AppRoles.InjectMissing(identity, "role");
});

// ⚠️ **默认拒绝**：没显式标注的端点一律要求登录。安全默认值应当是「忘了标注就拦下」，
//    而不是裸 AddAuthorization() 那样「忘了标注就裸奔」。
// ⚠️ 只 AddPolicy，**不要在这里重设 FallbackPolicy**：包里的那份带着
//    /_framework 放行，覆盖掉它会让 WASM 起不动，且症状指不到授权。
// 策略定义在 KMoldApp.Shared，与客户端共用同一份——两端各写一遍必然漂移。
builder.Services.AddDefaultDenyAuthorization(options => options.AddAppPolicies());

// 认证页（登录 / 二维码打印 / 错误页）的宿主参数。页面本身在 Kimi.AppKit.Components 里。
// ⚠️ AppStylesheet 必须配：组件 scoped 样式包（{程序集名}.styles.css）的名字随程序集走，
//    包里猜不出来。漏配时页面照常渲染，只是所有 *.razor.css 的样式全部丢失且不报错。
builder.Services.Configure<KAuthPageOptions>(o =>
{
    o.AppStylesheet = "KMoldApp.styles.css";
    o.ProductName = AppBrand.ProductName;
});

builder.Services.AddControllers();
builder.Services.AddCascadingAuthenticationState();

// 把服务端已认证的身份送给 WASM 端（另一个进程，拿不到 HttpContext）。
builder.Services.AddScoped<AuthenticationStateProvider, KPersistingAuthenticationStateProvider>();

// 开发期权限绕过。⚠️ 默认关闭，要用必须在 appsettings 里显式开
// （Auth:RoleBypass:Enabled）；生产环境即使配了也不生效。
// 前身是「非生产环境默认开启」，那让权限相关的 bug 在 Staging 根本测不出来。
builder.Services.AddAppKitRoleBypass(
    builder.Configuration,
    builder.Environment.IsProduction(),
    // ⚠️ 注入哪些角色由本应用决定——角色名是业务身份，包里不该替我们定义权限模型。
    identity => AppRoles.InjectMissing(identity, "role"));

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
// ⚠️ 不要再补一句 AddHangfireServer()：AddAppKitHangfire 内部已经注册了服务器，
//    再调一次是两个 BackgroundJobServer 抢同一个队列（不报错，并发度悄悄翻倍）。
//    TemplateHangfireTests 守着这一条。

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

// ⚠️ 需要 WASM 调试时必须在 MapAppKitApp **之前**调它。
//    包里刻意不含这一行——它来自 WebAssembly.Server 包，
//    强加给所有消费方等于让纯 API 服务也背上 WASM 依赖。
if (app.Environment.IsDevelopment()) app.UseWebAssemblyDebugging();

// 整条标准管线（中间件顺序、静态资产授权、登录/登出/现场密码/扫码端点、
// 健康检查、API 文档、任务面板、API 未命中兜底）一次装好。
// ⚠️ 顺序不是本应用该拥有的决策——它没有业务含义，只有对错，
//    而这条管线上有五处「写错就静默出 bug」的约束。详见 KAppPipeline 的注释。
app.MapAppKitApp(o =>
{
    o.LoginPath = "/login";

    // ⚠️ 「谁算运维」是业务决策，包里不该替我们定。
    //    留空的话后台任务面板**不会映射**——宁可没有入口，也不要一个谁都能进的面板。
    o.OpsPolicy = AppPolicies.AdminOnly;
});

// ⚠️ Blazor 端点必须显式 AllowAnonymous，否则会被默认拒绝策略拦住。
//    Blazor 的鉴权层是组件级的 AuthorizeRouteView 与页面上的 [Authorize]，不是端点级策略；
//    端点级一刀切会把**登录页自己**也要求登录，症状是登录后又被弹回登录页的重定向死循环。
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(KMoldApp.Client._Imports).Assembly)
    .AllowAnonymous();

app.MapControllers();

// 每实体一组 CRUD 端点，各自挂授权。
// ⚠️ 出厂即 RequireAuthorization()；这里再显式收紧到管理员——设置与邮件模板
//    属于系统配置，普通登录用户不该能读写。
app.MapCrudEndpoints<Setting>().RequireAuthorization(AppPolicies.AdminOnly);
app.MapCrudEndpoints<EmailTemplate>().RequireAuthorization(AppPolicies.AdminOnly);

app.Run();
