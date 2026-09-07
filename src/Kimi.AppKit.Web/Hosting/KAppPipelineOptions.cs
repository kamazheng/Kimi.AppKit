namespace Kimi.AppKit.Web.Hosting;

/// <summary>
/// <see cref="KAppPipeline.MapAppKitApp"/> 的开关与路径。
/// </summary>
/// <remarks>
/// ⚠️ 这里只有**开关与路径**，没有安全策略。像「默认拒绝授权」「开放哪些表」
/// 这类客户该知道的决策一律留在消费方的 Program.cs 显式书写——
/// 藏进包里会让排查权限问题时得反编译。
/// </remarks>
public sealed class KAppPipelineOptions
{
    /// <summary>登录入口路径。与 Cookie handler 的 <c>LoginPath</c> 必须一致。</summary>
    public string LoginPath { get; set; } = "/login";

    /// <summary>现场密码登录页路径。未配 IdP 时登录入口会落到这里。</summary>
    public string PasswordLoginPath { get; set; } = "/password-login";

    /// <summary>未处理异常的落地页路径。</summary>
    public string ErrorPath { get; set; } = "/Error";

    /// <summary>SPA 状态码页重新执行的目标路径。</summary>
    public string NotFoundPath { get; set; } = "/not-found";

    /// <summary>
    /// API 路径前缀。
    /// </summary>
    /// <remarks>
    /// ⚠️ 改它会同时影响两处、且都是静默的：状态码页只对非该前缀启用；
    /// 未命中的该前缀请求回 problem+json 而不是登录页 HTML。
    /// </remarks>
    public string ApiPathPrefix { get; set; } = "/api";

    /// <summary>
    /// 运维页面（API 文档、后台任务面板）所需的授权策略名。
    /// </summary>
    /// <remarks>
    /// ⚠️ 必须由消费方给：「谁算运维」是业务决策。
    /// 留空时后台任务面板**不会映射**——宁可没有入口，也不要一个谁都能进的任务面板。
    /// </remarks>
    public string? OpsPolicy { get; set; }

    /// <summary>默认语言。</summary>
    public string DefaultCulture { get; set; } = "en";

    /// <summary>支持的语言。</summary>
    public string[] SupportedCultures { get; set; } = ["en", "zh-CN"];

    // ── 以下为按需关闭的开关。默认全开，关掉即整段不映射。 ──

    /// <summary>请求本地化中间件。</summary>
    public bool RequestLocalization { get; set; } = true;

    /// <summary>健康检查端点（<c>/health/live</c> 与 <c>/health/ready</c>）。</summary>
    public bool HealthChecks { get; set; } = true;

    /// <summary>API 文档（<c>/openapi/v1.json</c> 与 <c>/scalar/v1</c>）。</summary>
    public bool OpenApi { get; set; } = true;

    /// <summary>后台任务面板（<c>/hangfire</c>）。⚠️ 还需 <see cref="OpsPolicy"/> 非空。</summary>
    public bool HangfireDashboard { get; set; } = true;

    /// <summary>登录 / 登出端点。</summary>
    public bool AuthenticationEndpoints { get; set; } = true;

    /// <summary>现场密码登录（ROPC）。⚠️ 密码会经过本应用，只在脱域现场用。</summary>
    public bool PasswordLogin { get; set; } = true;

    /// <summary>扫码登录。⚠️ 卡片等价于一张写着密码的便条，靠有效期 + 网络准入约束。</summary>
    public bool QrLogin { get; set; } = true;

    /// <summary>未处理异常的落地页。</summary>
    public bool ErrorPage { get; set; } = true;

    /// <summary>未命中的 API 回 problem+json 404。</summary>
    public bool ApiNotFoundFallback { get; set; } = true;
}
