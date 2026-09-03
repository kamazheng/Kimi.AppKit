using System.Text.Json;
using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Core.Http;
using Microsoft.Extensions.Options;

namespace Kimi.AppKit.Web.Authentication;

/// <summary>直接与 IdP 令牌端点交互：ROPC 密码登录、刷新令牌。</summary>
public interface IKOidcTokenService
{
    /// <summary>用账号密码换取令牌（OAuth ROPC，<c>grant_type=password</c>）。</summary>
    Task<KTokenResponse?> PasswordLoginAsync(
        string username, string password, CancellationToken cancellationToken = default);

    /// <summary>用刷新令牌换取新令牌。</summary>
    Task<KTokenResponse?> RefreshTokenAsync(
        string refreshToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// 基于 <see cref="IHttpClientFactory"/> 的令牌服务。
/// </summary>
/// <remarks>
/// 【⚠️ ROPC 是降级方案，不是首选】密码直接经过本应用，绕过 IdP 的 MFA 与条件访问。
/// 它存在只是因为脱域现场没有可信证书、走不了浏览器重定向的授权码流程。
/// 有条件走授权码流程时不要用它。
///
/// 【⚠️ 走 <see cref="IHttpClientFactory"/>，不要 <c>new HttpClient()</c>】
/// 登录与刷新在换班高峰会突发大量调用，每次新建会让套接字堆积在 TIME_WAIT 直到端口耗尽；
/// 工厂复用连接池，且会定期轮换 handler，不像静态 <c>HttpClient</c> 那样
/// 长期持有过期的 DNS 解析结果。
///
/// 【⚠️ 证书照常校验】前身这里挂过一个接受任意服务端证书的 handler，
/// 那让中间人可以冒充 IdP 的令牌端点。默认校验就是对的，不要覆盖。
/// </remarks>
public sealed class KOidcTokenService(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<KOidcOptions> options) : IKOidcTokenService
{
    /// <summary>本服务使用的具名 <c>HttpClient</c>。</summary>
    public const string HttpClientName = "AppKitIdpToken";

    /// <inheritdoc />
    public Task<KTokenResponse?> PasswordLoginAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var config = options.CurrentValue;

        // ROPC 下多数 IdP 需要 offline_access 才返回 refresh_token，
        // 否则 id_token 一过期用户就掉登录，现场看到的只是「莫名其妙又要重新登」。
        var scopes = config.Scopes.Concat(config.PasswordGrantScopes).Distinct();

        return RequestAsync(config, new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password,
            ["scope"] = string.Join(' ', scopes),
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<KTokenResponse?> RefreshTokenAsync(
        string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        return RequestAsync(options.CurrentValue, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, cancellationToken);
    }

    private async Task<KTokenResponse?> RequestAsync(
        KOidcOptions config, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        // ⚠️ 缺配置在这里就要说清楚。前身用 `configuration["OpenIDConnect:Token"]!` 直取，
        //    键名写错时得到 null，被 `!` 一路带下去，最终在 HttpRequestMessage 里
        //    抛「值不能为 null」——那条堆栈指不到「配置键写错了」。
        if (string.IsNullOrWhiteSpace(config.TokenEndpoint))
        {
            throw new InvalidOperationException(
                $"未配置 {KOidcOptions.SectionName}:{nameof(KOidcOptions.TokenEndpoint)}，无法换取令牌。");
        }

        if (string.IsNullOrWhiteSpace(config.ClientId))
        {
            throw new InvalidOperationException(
                $"未配置 {KOidcOptions.SectionName}:{nameof(KOidcOptions.ClientId)}，无法换取令牌。");
        }

        form["client_id"] = config.ClientId;
        if (!string.IsNullOrEmpty(config.ClientSecret)) form["client_secret"] = config.ClientSecret;

        using var request = new HttpRequestMessage(HttpMethod.Post, config.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);

        // 失败时把 IdP 的响应体解析进异常（error / error_description），调用方才能记录原因。
        // ⚠️ 异常里只有**响应**，绝不包含上面那个含密码的请求体。
        await response.EnsureSuccessOrThrowAsync();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<KTokenResponse>(content);
    }
}
