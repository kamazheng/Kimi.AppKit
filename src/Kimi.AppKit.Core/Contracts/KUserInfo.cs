using System.Security.Claims;

namespace Kimi.AppKit.Core.Contracts;

/// <summary>
/// 服务端预渲染时序列化、由 WASM 端还原的用户身份。
/// </summary>
/// <remarks>
/// 【它解决什么】Blazor Web App 的服务端已经认证过了，但 WASM 端是另一个进程，
/// 拿不到服务端的 <c>HttpContext</c>。靠 <c>PersistentComponentState</c> 把身份
/// 随首屏 HTML 一起送过去，客户端才不必再问一次「我是谁」。
///
/// 【⚠️ 刻意**不**携带 refresh token，与前身不同】
/// 前身把 <c>RefreshToken</c> 一起序列化进页面。refresh token 是**长期**凭据，
/// 一旦 XSS 拿到它就能长期冒充该用户，而 id token 过期后至少还会失效。
/// 而且它是多余的：<c>Kimi.AppKit.Web</c> 的 <c>ConfigureCookieOidcRefresh</c>
/// 已经在**服务端** Cookie 校验时自动续期，客户端根本不需要自己刷新。
///
/// 【⚠️ 角色 claim 用短名 <c>"role"</c>】服务端配了 <c>MapInboundClaims = false</c>，
/// JWT 里的角色是短名而非 WS-* 长 URI。凡从 token 重建 <see cref="ClaimsIdentity"/>
/// 都必须显式传这个值，否则**令牌里明明有角色、页面却一路 403**，且不报错。
/// </remarks>
public sealed class KUserInfo
{
    /// <summary>用户名 claim 的类型名。</summary>
    public const string NameClaimType = "name";

    /// <summary>角色 claim 的类型名。见类型注释里的 <c>MapInboundClaims</c> 说明。</summary>
    public const string RoleClaimType = "role";

    /// <summary>显示名 claim 的类型名。</summary>
    public const string DisplayNameClaimType = "displayname";

    /// <summary>登录名。</summary>
    public required string Name { get; init; }

    /// <summary>显示名。IdP 未提供时回退到 <see cref="Name"/>。</summary>
    public required string DisplayName { get; init; }

    /// <summary>身份令牌，供 WASM 端调 API 时挂 Bearer。</summary>
    public required string IdToken { get; init; }

    /// <summary>全部 claim。</summary>
    public List<ClaimInfo> Claims { get; init; } = [];

    /// <summary>
    /// 从服务端的 <see cref="ClaimsPrincipal"/> 构造。
    /// </summary>
    /// <remarks>
    /// ⚠️ **不要因为缺少某个 claim 就抛异常。** 前身对 <c>name</c> 与 <c>displayname</c>
    /// 都用「找不到就抛」，而 <c>displayname</c> 是不少 IdP 根本不发的可选 claim。
    /// 那个异常发生在 <c>PersistentComponentState</c> 的持久化回调里，
    /// 后果是**客户端认证态整个丢失、用户被弹回登录页**，而日志里只有一句
    /// 「Could not find required 'displayname' claim」——看不出它会导致登不上。
    /// </remarks>
    public static KUserInfo FromClaimsPrincipal(ClaimsPrincipal principal, string idToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var name = principal.FindFirst(NameClaimType)?.Value
                   ?? principal.Identity?.Name
                   ?? string.Empty;

        return new KUserInfo
        {
            Name = name,
            DisplayName = principal.FindFirst(DisplayNameClaimType)?.Value ?? name,
            IdToken = idToken,
            Claims = [.. principal.Claims.Select(c => new ClaimInfo { Type = c.Type, Value = c.Value })],
        };
    }

    /// <summary>还原成 <see cref="ClaimsPrincipal"/>。</summary>
    public ClaimsPrincipal ToClaimsPrincipal() =>
        new(new ClaimsIdentity(
            Claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: nameof(KUserInfo),
            nameType: NameClaimType,
            roleType: RoleClaimType));
}

/// <summary>可序列化的单条 claim。</summary>
public sealed class ClaimInfo
{
    /// <summary>类型。</summary>
    public required string Type { get; init; }

    /// <summary>值。</summary>
    public required string Value { get; init; }
}
