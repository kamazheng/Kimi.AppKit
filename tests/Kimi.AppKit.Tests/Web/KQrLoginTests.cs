using Kimi.AppKit.Web.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Kimi.AppKit.Tests.Web;

/// <summary>
/// 二维码登录密文的行为契约。
/// </summary>
/// <remarks>
/// 【为什么要测】密文里装的是**明文密码**，印在纸上。前身没有任何时间信息，
/// 于是一张打印出来的卡片永久有效——员工离职、密码轮换之后那张纸依然能登录。
/// 「有效期真的生效」这件事一旦失效是静默的：卡片照常能用，没人会发现它本该失效。
/// </remarks>
public class KQrLoginTests
{
    private static IDataProtectionProvider Provider() =>
        DataProtectionProvider.Create(nameof(KQrLoginTests));

    [Fact]
    public void 加密后能原样解回()
    {
        var provider = Provider();
        var payload = KQrLogin.Protect(provider, "alice", "p@ssw0rd");

        var result = KQrLogin.TryUnprotect(provider, payload);

        Assert.NotNull(result);
        Assert.Equal("alice", result.Value.Username);
        Assert.Equal("p@ssw0rd", result.Value.Password);
    }

    [Fact]
    public void 过期后解不开()
    {
        var provider = Provider();

        // 负的有效期 = 签发即过期。比在测试里等真实时间可靠，也不引入时间抽象。
        var payload = KQrLogin.Protect(provider, "alice", "p@ssw0rd", TimeSpan.FromSeconds(-1));

        Assert.Null(KQrLogin.TryUnprotect(provider, payload));
    }

    [Fact]
    public void 密文被篡改后解不开()
    {
        var provider = Provider();
        var payload = KQrLogin.Protect(provider, "alice", "p@ssw0rd");

        // 改末位。DataProtection 带完整性校验，任何改动都应导致解密失败。
        var tampered = payload[..^1] + (payload[^1] == 'A' ? 'B' : 'A');

        Assert.Null(KQrLogin.TryUnprotect(provider, tampered));
    }

    [Fact]
    public void 换一个用途串的提供者解不开()
    {
        // 用途隔离：别处签出的密文不能拿来登录。
        var payload = KQrLogin.Protect(Provider(), "alice", "p@ssw0rd");

        Assert.Null(KQrLogin.TryUnprotect(DataProtectionProvider.Create("其它应用"), payload));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("不是密文")]
    public void 非法输入返回null而不抛(string? payload) =>
        Assert.Null(KQrLogin.TryUnprotect(Provider(), payload));

    [Fact]
    public void 默认有效期是一个班次()
    {
        // 钉住这个值：调长之前先想清楚「这张纸落在别人手里能用多久」。
        Assert.Equal(TimeSpan.FromHours(8), KQrLogin.DefaultLifetime);
    }
}
