using Kimi.AppKit.Web.Authentication;
using Xunit;

namespace Kimi.AppKit.Tests.Web;

/// <summary>
/// 回跳地址收敛的行为契约。
/// </summary>
/// <remarks>
/// 【为什么这组测试必须存在】开放重定向的失败是**看不出来的**：
/// 用户登录成功、被送到攻击者的站点，而地址栏上一跳还是你的域名。
/// 这段逻辑原先是登录端点里的 <c>private static</c> 方法，
/// 写得不错但没有任何测试，且以那个可见性根本写不了测试——
/// 一段安全关键、边界条件多、又没法测的代码，每次改动都是在赌。
/// </remarks>
public class KReturnUrlTests
{
    [Theory]
    [InlineData("/orders")]
    [InlineData("/orders/42")]
    [InlineData("/orders?status=open")]
    [InlineData("orders")]                       // 不带前导斜杠也应补上
    public void 站内相对路径原样放行(string input)
    {
        var result = KReturnUrl.Sanitize(input);

        Assert.StartsWith("/", result, StringComparison.Ordinal);
        Assert.Contains("orders", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://evil.example/path")]
    [InlineData("ftp://evil.example")]
    public void 绝对URL一律回落根路径(string input) =>
        Assert.Equal(KReturnUrl.Fallback, KReturnUrl.Sanitize(input));

    [Theory]
    [InlineData("//evil.example")]
    [InlineData("//evil.example/path")]
    public void 协议相对URL被挡住(string input)
    {
        // ⚠️ 这是最容易漏的一条：`//host` 是**合法的相对 URI**，首字符也是 '/'，
        //    只按「以斜杠开头就是站内」判断会直接放过它，
        //    而浏览器会把它当绝对地址跳到 evil.example。
        Assert.Equal(KReturnUrl.Fallback, KReturnUrl.Sanitize(input));
    }

    [Theory]
    [InlineData(@"/\evil.example")]
    [InlineData(@"\\evil.example")]
    [InlineData(@"/path\..\..\evil")]
    public void 反斜杠被挡住(string input)
    {
        // 浏览器会把反斜杠归一成斜杠，于是 "/\evil.example" 等价于 "//evil.example"。
        Assert.Equal(KReturnUrl.Fallback, KReturnUrl.Sanitize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空输入回落根路径(string? input) =>
        Assert.Equal(KReturnUrl.Fallback, KReturnUrl.Sanitize(input));

    [Theory]
    [InlineData("/http-login")]
    [InlineData("/HTTP-LOGIN?error=1")]          // 大小写不敏感
    [InlineData("/authentication/login")]
    public void 禁止回跳登录端点(string input)
    {
        // 跳回登录页会让用户看到「登录成功了却又回到登录页」，像是登录失败。
        var blocked = new[] { "/http-login", "/authentication", "/qr-print" };

        Assert.Equal(KReturnUrl.Fallback, KReturnUrl.Sanitize(input, blocked));
    }

    [Fact]
    public void 不传禁止前缀时登录端点也能回跳()
    {
        // 禁止清单是调用方的策略，不是这个函数自己的判断——不同应用的登录路径不一样。
        Assert.Equal("/http-login", KReturnUrl.Sanitize("/http-login"));
    }

    [Theory]
    [InlineData("/orders\n/evil")]
    [InlineData("/orders\r\nLocation: https://evil.example")]
    [InlineData("/orders\tx")]
    public void 含控制字符的输入被挡住(string input)
    {
        // 换行进 Location 头就是响应拆分。IsWellFormedUriString 会拒掉这些。
        Assert.Equal(KReturnUrl.Fallback, KReturnUrl.Sanitize(input));
    }
}
