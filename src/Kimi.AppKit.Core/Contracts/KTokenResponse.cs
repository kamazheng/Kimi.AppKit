using System.Text.Json.Serialization;

namespace Kimi.AppKit.Core.Contracts;

/// <summary>
/// IdP 令牌端点返回的令牌。
/// </summary>
/// <remarks>
/// 【为什么在 Core 而不是 Web】客户端（WASM）在刷新令牌时也要用这个形状，
/// 而 <c>Kimi.AppKit.Web</c> 是服务端专用包、浏览器侧引不了。
/// 放在零依赖的 Core 里，两端共用同一份定义——这正是「共享契约」该待的地方。
///
/// 【⚠️ 字段名必须带 <see cref="JsonPropertyNameAttribute"/>】
/// OAuth 的响应用的是 <c>snake_case</c>（<c>id_token</c> 而非 <c>idToken</c>）。
/// 漏掉特性不会报错，只会**全部反序列化成 null**——
/// 表现是「登录成功但拿不到令牌」，而 HTTP 状态码是 200。
/// </remarks>
public sealed class KTokenResponse
{
    /// <summary>身份令牌。</summary>
    [JsonPropertyName("id_token")]
    public string? IdToken { get; set; }

    /// <summary>访问令牌。</summary>
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    /// <summary>刷新令牌。</summary>
    /// <remarks>
    /// ⚠️ ROPC 下多数 IdP 需要 <c>offline_access</c> scope 才会返回它。
    /// 拿不到刷新令牌时，id_token 一过期用户就掉登录，
    /// 现场看到的只是「莫名其妙又要重新登一次」。
    /// </remarks>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>令牌类型，通常是 <c>Bearer</c>。</summary>
    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    /// <summary>访问令牌的有效秒数。</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}
