using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
/// <item>**必须有有效期**（<see cref="KQrLoginOptions.Lifetime"/>），过期的卡片作废</item>
/// <item>打印页要写明「请妥善保管、勿外传」</item>
/// <item>服务端**不持久化**密码，只在这一次请求里用完即弃</item>
/// </list>
///
/// 【⚠️ 有效期用 <see cref="ITimeLimitedDataProtector"/>，不要自己塞时间戳字段】
/// 它把到期时间**签进密文**，<c>Unprotect</c> 时自动校验并抛异常。
/// 自己加一个 <c>exp</c> 字段的做法更脆：忘了校验不会有任何报错，
/// 表现就是有效期形同虚设，而代码里明明写着过期时间。
/// </remarks>
public sealed class KQrLogin(
    IDataProtectionProvider provider,
    IOptionsMonitor<KQrLoginOptions> options,
    ILogger<KQrLogin> logger)
{
    /// <summary>DataProtection 的用途串。与其它 protector 隔离，密文不能跨用途解开。</summary>
    private const string Purpose = "Kimi.AppKit.Web.QrLogin";

    private ITimeLimitedDataProtector Protector =>
        provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    /// <summary>把账号密码加密成可放进二维码的单行密文（base64url，扫码枪可直接键入）。</summary>
    /// <param name="username">账号。</param>
    /// <param name="password">密码。</param>
    /// <param name="lifetime">
    /// 有效期。不传则用 <see cref="KQrLoginOptions.Lifetime"/>——
    /// 这个参数只为「某一次签发要更短」留口子，**不要**用它绕过配置去写死一个更长的值。
    /// </param>
    public string Protect(string username, string password, TimeSpan? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var payload = JsonSerializer.Serialize(new QrCredential(username, password));
        return Protector.Protect(payload, lifetime ?? options.CurrentValue.Lifetime);
    }

    /// <summary>
    /// 解密二维码密文。密文非法、被篡改或**已过期**时返回 <c>null</c>，只记日志不抛。
    /// </summary>
    /// <remarks>
    /// ⚠️ 失败一律返回 <c>null</c>、给调用方同一个结果，不要把「过期」和「篡改」区分着告诉用户——
    /// 那等于给攻击者一个预言机。日志里可以留细节，响应里不行。
    /// </remarks>
    public (string Username, string Password)? TryUnprotect(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try
        {
            var json = Protector.Unprotect(payload.Trim());
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
            logger.LogWarning(ex, "二维码登录密文解密失败（已过期、被篡改或密钥环已轮换）。");
            return null;
        }
    }

    /// <summary>密文的明文结构。字段名压到一个字母是为了缩小二维码尺寸。</summary>
    private sealed record QrCredential(string u, string p);
}
