using Kimi.AppKit.Components;
using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Web.Authentication;
using Kimi.AppKit.Web.Authorization;
using Kimi.AppKit.Web.Branding;
using Kimi.AppKit.Web.Excel;
using Kimi.AppKit.Web.Identity;
using Kimi.AppKit.Web.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Security.Claims;

namespace Kimi.AppKit.Web.Hosting;

/// <summary>
/// 标准应用的服务装配。
/// </summary>
/// <remarks>
/// 【收进来的是什么】清一色的**基建注册**——认证、网络准入、令牌服务、扫码、
/// 运行环境、企业标识、认证态传递、API 文档、Excel、开发期角色绕过。
/// 它们没有业务含义，漏一个就在启动期或运行期炸，而错误信息往往指向别处
/// （典型：漏注册 IHttpContextAccessor 时，报错指向 IKCurrentUser）。
///
/// 【⚠️ 刻意**没有**收进来的】这些必须留在消费方显式书写，因为它们是
/// 客户该知道、也可能要改的决策：
/// <list type="bullet">
/// <item>默认拒绝授权与角色策略（<c>AddDefaultDenyAuthorization</c>）</item>
/// <item>DbContext 注册与 <c>EnableSensitiveDataLogging</c> 只开发环境</item>
/// <item>CRUD 白名单 <c>AddKCrud().AddEntity&lt;T&gt;()</c>——
/// 「本系统对外开放了哪些表」的唯一声明处</item>
/// <item>健康检查、邮件、后台任务、可观测性——它们要么带泛型参数、
/// 要么要接线具体 provider，藏进包只会让配置变得更绕</item>
/// </list>
/// 把这些也收进来能再省二十来行，但代价是客户的 Program.cs 里
/// **一行都看不到自己的安全边界**——排查权限问题时得反编译。不划算。
/// </remarks>
public static class KAppServices
{
    /// <summary>装配标准应用所需的基建服务。</summary>
    /// <param name="builder">应用构建器。</param>
    /// <param name="configure">按需调整。</param>
    public static WebApplicationBuilder AddAppKitApp(
        this WebApplicationBuilder builder, Action<KAppServiceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new KAppServiceOptions();
        configure?.Invoke(options);

        var services = builder.Services;
        var configuration = builder.Configuration;

        // 认证。⚠️ 未配 OpenIDConnect:Issuer / ClientId 时会跳过 OIDC 与 JwtBearer
        //    并在控制台打印提示，应用照常启动——本地起一个空壳看界面不必先架 IdP。
        services.AddAppKitAuthentication(configuration, o => o.LoginPath = options.LoginPath);

        // 现场登录三件套：网络准入（空白名单 = 拒绝）、ROPC 令牌服务、扫码加解密。
        services.AddAppKitNetworkGate(configuration);
        services.AddAppKitOidcTokenService();
        services.AddAppKitQrLogin(configuration);

        // <KEnvChip /> 的依赖。⚠️ 漏了它那个组件渲染时抛——而它恰恰是
        //    「这不是生产环境」的可见标识，渲染不出来时配错环境变量的实例
        //    看起来和正式站一模一样。
        services.AddAppKitEnvironment();

        // 企业标识向身份服务要。⚠️ 一套部署里客户只该设一次，
        //    各服务各配一份必然出现两个名字并存。
        services.AddAppKitBranding();
        services.AddScoped<IKBrandingSource, KServerBranding>();

        // 把服务端已认证的身份送给 WASM 端（另一个进程，拿不到 HttpContext）。
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, KPersistingAuthenticationStateProvider>();

        // 审计字段的「谁干的」来自这里。
        // ⚠️ HttpContextCurrentUser 依赖 IHttpContextAccessor，**必须一并注册**——
        //    漏了它启动就失败（启动期 DI 校验抓的），而错误信息指向 IKCurrentUser
        //    而不是这一行。
        services.AddHttpContextAccessor();
        services.AddScoped<IKCurrentUser, HttpContextCurrentUser>();
        services.AddSingleton(TimeProvider.System);

        // CRUD 端点自带 Excel 导出/导入。
        // ⚠️ 漏了它会在启动期抛，且错误信息直接给出修法——这类「装配不全」
        //    就该在启动期炸掉，而不是等用户点导出时才 500。
        if (options.Excel) services.AddAppKitExcel();

        if (options.OpenApi) services.AddAppKitOpenApi(configuration);

        // 开发期权限绕过。⚠️ 默认关闭，要用必须显式开（Auth:RoleBypass:Enabled）；
        //    生产环境即使配了也不生效。前身是「非生产默认开启」，
        //    那让权限相关的 bug 在 Staging 根本测不出来。
        if (options.RoleBypass)
        {
            services.AddAppKitRoleBypass(
                configuration, builder.Environment.IsProduction(), options.InjectBypassRoles);
        }

        return builder;
    }
}

/// <summary><see cref="KAppServices.AddAppKitApp"/> 的开关。</summary>
public sealed class KAppServiceOptions
{
    /// <summary>登录入口路径。⚠️ 必须与 <c>KAppPipelineOptions.LoginPath</c> 一致。</summary>
    public string LoginPath { get; set; } = "/login";

    /// <summary>Excel 导入导出（CRUD 端点需要）。</summary>
    public bool Excel { get; set; } = true;

    /// <summary>API 文档生成。</summary>
    public bool OpenApi { get; set; } = true;

    /// <summary>开发期权限绕过。⚠️ 注册它不等于启用，启用要在配置里显式开。</summary>
    public bool RoleBypass { get; set; } = true;

    /// <summary>
    /// 绕过生效时注入哪些角色。
    /// </summary>
    /// <remarks>
    /// ⚠️ 必须由消费方提供：角色名是业务身份，包里不该替客户定义权限模型。
    /// 不设则绕过等于空操作——那也是安全的默认。
    /// </remarks>
    public Action<ClaimsIdentity>? InjectBypassRoles { get; set; }
}
