namespace Kimi.AppKit.Web.Authentication;

/// <summary>OIDC 接入配置。</summary>
/// <remarks>
/// 【⚠️ 为什么要有这个类，而不是继续读 <c>IConfiguration["OpenIDConnect:Issuer"]</c>】
/// 前身在六处直接索引配置并加 <c>!</c> 抑制可空告警。后果有二：
/// <list type="number">
/// <item>拼错一个键名不会有任何编译错误，运行时得到 <c>null</c>，
///       再被 <c>!</c> 一路带下去，最终在某个 <c>FormUrlEncodedContent</c> 里
///       抛「值不能为 null」——那条堆栈指不到「配置键写错了」</item>
/// <item>「这个功能到底要配哪几项」这件事，代码里没有任何一处在集中说明</item>
/// </list>
/// </remarks>
public sealed class KOidcOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "OpenIDConnect";

    /// <summary>IdP 的 issuer / authority。</summary>
    public string? Issuer { get; set; }

    /// <summary>客户端 ID。同时用作 JWT 校验的 audience。</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// 客户端密钥。
    /// </summary>
    /// <remarks>
    /// ⚠️ **不要写进 appsettings.json**（它进版本控制）。
    /// 用 user-secrets 或环境变量 <c>OpenIDConnect__ClientSecret</c>。
    /// </remarks>
    public string? ClientSecret { get; set; }

    /// <summary>令牌端点。用于 ROPC 密码登录与刷新令牌。</summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>请求的 scope。</summary>
    /// <remarks>
    /// ⚠️ ROPC 下多数 IdP 需要 <c>offline_access</c> 才会返回 refresh_token，
    /// 否则 id_token 一过期用户就掉登录，而现场看到的只是「莫名其妙又要重新登」。
    /// </remarks>
    public IList<string> Scopes { get; } = ["openid", "profile", "email", "roles"];

    /// <summary>ROPC 密码登录额外追加的 scope。</summary>
    public IList<string> PasswordGrantScopes { get; } = ["offline_access"];

    /// <summary>匿名访问受保护资源时跳转的登录页。</summary>
    /// <remarks>
    /// ⚠️ 必须指向一个**真实存在**的路由。Cookie handler 的默认值是
    /// <c>/Account/Login</c>——那个路由在多数应用里并不存在，于是匿名请求被重定向过去，
    /// 该请求自身又被默认拒绝策略拦下再重定向，形成死循环。
    /// </remarks>
    public string LoginPath { get; set; } = "/login";

    /// <summary>拒绝访问时跳转的路径。默认与 <see cref="LoginPath"/> 相同。</summary>
    public string? AccessDeniedPath { get; set; }

    /// <summary>登出后 IdP 回跳的路径。</summary>
    public string SignOutCallbackPath { get; set; } = "/signout-callback";

    /// <summary>
    /// 强制把 OIDC 回调地址拼成 https。
    /// </summary>
    /// <remarks>
    /// ⚠️ 默认开启。IdP 侧登记的回调通常只允许 https；反向代理终止 TLS 后
    /// 后端跑的是 http，按 <c>Request.Scheme</c> 拼会得到 http 回调而被 IdP 拒绝，
    /// 症状是「登录跳过去就报 redirect_uri 不匹配」。
    /// </remarks>
    public bool ForceHttpsRedirectUri { get; set; } = true;

    /// <summary>SignalR 集线器的路径前缀。这些路径的令牌走查询串。</summary>
    /// <remarks>
    /// WebSocket 握手带不了 <c>Authorization</c> 头，令牌只能放查询串。
    /// ⚠️ 因此这类 URL 一定要脱敏后再进日志与遥测。
    /// </remarks>
    public string HubPathPrefix { get; set; } = "/hubs";

    /// <summary>OIDC 的最小配置是否齐全。</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(ClientId);
}

/// <summary>包内使用的认证方案名。</summary>
/// <remarks>
/// 【为什么集中定义】前身把 OIDC scheme 名分别写在两个文件里各一份局部常量。
/// 改一处忘了另一处，注册的方案名与 <c>SignOutAsync</c> 引用的方案名就对不上——
/// 症状是**登出静默失败**：Cookie 清了、OIDC 侧会话没清，
/// 用户点登出再点登录会被上游直接静默登回来，看起来像「退出按钮失灵」。
/// </remarks>
public static class KAuthenticationSchemes
{
    /// <summary>OIDC 挑战/登出方案名。</summary>
    public const string Oidc = "AppKitOpenId";

    /// <summary>ROPC 密码登录签发身份的 <c>AuthenticationType</c>。会出现在审计里。</summary>
    public const string PasswordLogin = "AppKitPassword";
}
