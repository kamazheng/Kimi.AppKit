using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Kimi.AppKit.Web.Authentication;

/// <summary>
/// 二维码登录凭据的加解密。
/// </summary>
/// <remarks>
/// 【用在哪】脱域现场：域内电脑上生成一张加密的登录二维码卡片并打印，
/// 现场电脑用扫码枪扫入，服务端解密后走密码登录。
///
/// 【⚠️ 这张卡片就是密码本身】密文里装着账号和明文密码。
/// 印出来的纸片等价于一张写着密码的便条——这是 ROPC + 二维码这个方案的固有性质，
/// 不是可以在这一层修掉的缺陷。因此：
/// <list type="bullet">
/// <item>**必须有有效期**（见下），过期的卡片作废</item>
/// <item>打印页要写明「请妥善保管、勿外传」</item>
/// <item>服务端**不持久化**密码，只在这一次请求里用完即弃</item>
/// </list>
///
/// 【⚠️ 有效期用 <see cref="ITimeLimitedDataProtector"/>，不要自己塞时间戳】
/// 前身的密文明文结构是 <c>{u, p, v}</c>——账号、密码、版本号，**没有任何时间信息**。
/// DataProtection 的密文本身不带过期，于是一张打印出来的卡片**永久有效**：
/// 员工离职、密码轮换之后，那张纸依然能登录。
/// 正解是框架自带的时限保护器，它把到期时间**签进密文**，
/// <c>Unprotect</c> 时自动校验并抛 <c>CryptographicException</c>——
/// 比自己加一个 <c>exp</c> 字段可靠：手写字段容易忘了校验，而忘了校验不会有任何报错。
/// </remarks>
public static class KQrLogin
{
    /// <summary>DataProtection 的用途串。与其它 protector 隔离，密文不能跨用途解开。</summary>
    private const string Purpose = "Kimi.AppKit.Web.QrLogin";

    /// <summary>默认有效期。</summary>
    /// <remarks>
    /// 8 小时 ≈ 一个班次：卡片当班有效，下班作废，重新打印的成本只是点一次按钮。
    /// 调长之前先想清楚「这张纸落在别人手里能用多久」。
    /// </remarks>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);

    /// <summary>把账号密码加密成可放进二维码的单行密文（base64url，扫码枪可直接键入）。</summary>
    /// <param name="provider">数据保护提供者。</param>
    /// <param name="username">账号。</param>
    /// <param name="password">密码。</param>
    /// <param name="lifetime">有效期，默认 <see cref="DefaultLifetime"/>。</param>
    public static string Protect(
        IDataProtectionProvider provider, string username, string password, TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var payload = JsonSerializer.Serialize(new QrCredential(username, password));
        return provider.CreateProtector(Purpose)
            .ToTimeLimitedDataProtector()
            .Protect(payload, lifetime ?? DefaultLifetime);
    }

    /// <summary>
    /// 解密二维码密文。密文非法、被篡改或**已过期**时返回 <c>null</c>，只记日志不抛。
    /// </summary>
    /// <remarks>
    /// ⚠️ 失败一律返回 <c>null</c>、给调用方同一句话，不要把「过期」和「篡改」区分着告诉用户——
    /// 那等于给攻击者一个预言机。日志里可以留细节，响应里不行。
    /// </remarks>
    public static (string Username, string Password)? TryUnprotect(
        IDataProtectionProvider provider, string? payload, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try
        {
            var json = provider.CreateProtector(Purpose)
                .ToTimeLimitedDataProtector()
                .Unprotect(payload.Trim());

            var credential = JsonSerializer.Deserialize<QrCredential>(json);
            if (credential is null
                || string.IsNullOrEmpty(credential.u)
                || string.IsNullOrEmpty(credential.p))
            {
                return null;
            }

            return (credential.u, credential.p);
        }
        catch (Exception ex)
        {
            // ⚠️ 不要把 payload 记进日志——那是凭据本身。
            logger?.LogWarning(ex, "二维码登录密文解密失败（已过期、被篡改或密钥环已轮换）。");
            return null;
        }
    }

    /// <summary>密文的明文结构。字段名压到一个字母是为了缩小二维码尺寸。</summary>
    private sealed record QrCredential(string u, string p);
}
