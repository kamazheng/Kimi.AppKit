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

    /// <summary>
    /// 本应用要调用的下游服务标识（RFC 8707 <c>resource</c> 参数）。
    /// </summary>
    /// <remarks>
    /// 【作用】声明「我这张令牌是要拿去调谁的」，IdP 据此写入 access token 的 <c>aud</c>。
    ///   两者都没有时（既不配本项，IdP 也没做 scope→resource 映射）令牌无 <c>aud</c>，
    ///   下游只能关掉受众校验，服务间就此失去边界。
    ///
    /// 【为什么用它而不是申请业务 scope】scope 路线要 IdP 预先登记「谁能调谁」，
    ///   配置量是 O(服务数 × 调用方数)，且加 scope 必须重启 IdP。
    ///   本路线下 IdP 零配置，授权决策由资源服务自己做（各自的调用方白名单）。
    ///
    /// ⚠️ **必须是绝对 URI**：RFC 8707 强制，IdP 会拒绝非 URI 值（ID2030）。
    /// ⚠️ **必须与目标服务配置的 audience 逐字一致，含尾斜杠。**
    ///   <c>https://api.example</c> 与 <c>https://api.example/</c> 是两个不同的值，
    ///   不一致的表现是下游 401 且错误只说 audience 不匹配——指不到少了个斜杠。
    ///   建议统一带尾斜杠。
    /// </remarks>
    public IList<string> Resources { get; } = [];

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

    /// <summary>
    /// 是否要求 IdP 的元数据端点（discovery / JWKS）走 HTTPS。
    /// </summary>
    /// <remarks>
    /// ⚠️ **默认 <c>true</c>，生产环境绝不要关。** 元数据里含签名公钥集，
    /// 明文传输意味着中间人可以替换公钥，进而伪造出本应用会接受的令牌——
    /// 那是认证根基被击穿，不是「传输不加密」这种程度的问题。
    ///
    /// 关它的唯一合理场景是**本地联调**：本机起的 IdP 通常不配证书，
    /// 此时 OIDC handler 会抛
    /// <c>The MetadataAddress or Authority must use HTTPS unless disabled for development</c>，
    /// 整个登录入口 500。
    ///
    /// ⚠️ 与 <c>JwtBearerSetup.ConfigureAppKitJwtBearer</c> 的同名参数保持一致：
    /// 带安全默认值的可选项，而不是硬编码——「不同部署确实需要不同值」的才该开放为配置。
    /// </remarks>
    public bool RequireHttpsMetadata { get; set; } = true;

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
