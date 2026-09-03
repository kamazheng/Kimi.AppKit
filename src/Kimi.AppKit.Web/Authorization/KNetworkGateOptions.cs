namespace Kimi.AppKit.Web.Authorization;

/// <summary>
/// 网络准入白名单配置。
/// </summary>
/// <remarks>
/// 【用在哪】把某些高风险入口限制在特定网段内。典型是**明文 HTTP 上的密码登录**
/// （脱域现场没有可信证书，只能走 HTTP + ROPC），也适用于后台任务面板一类运维入口。
///
/// ⚠️ **IP 是网络位置，不是机器身份**。这是一道纵深防御，不能当主鉴权用。
/// 反代之后要靠 <c>UseForwardedHeaders</c> 还原真实来源 IP，
/// 且转发头的信任边界必须在那里配好——否则客户端可以自己伪造 <c>X-Forwarded-For</c>。
/// </remarks>
public sealed class KNetworkGateOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Auth:NetworkGate";

    /// <summary>
    /// 允许的网段（CIDR，如 <c>10.20.30.0/24</c>）。
    /// </summary>
    /// <remarks>
    /// ⚠️ **空清单表示全部拒绝，不是全部放行。**
    /// 前身的实现在这里未配置时直接 <c>return null</c>（放行），注释还写着「便于可行性联调」。
    /// 实际后果是：整个仓库的 appsettings 里从来没有配过这一项，
    /// 于是这道门在**所有环境**下都是全开的——一道「只有车间网段能用明文密码登录」的门，
    /// 事实上对全世界开放，而代码读起来像是有防护的。
    /// 联调需要放开时请显式写 <c>0.0.0.0/0</c>，让「这里是敞开的」在配置里看得见。
    /// </remarks>
    public IList<string> AllowedSubnets { get; } = [];

    /// <summary>是否放行本机环回地址。</summary>
    /// <remarks>
    /// ⚠️ 默认 <c>false</c>。前身无条件放行环回，那等于在一道安全门上留了个恒开的后门：
    /// 任何能在服务器本机发起请求的进程（包括被攻破的旁路容器、SSRF 打到 localhost）
    /// 都绕过了网段限制。本地开发请在 <c>appsettings.Development.json</c> 里显式打开。
    /// </remarks>
    public bool AllowLoopback { get; set; }
}
