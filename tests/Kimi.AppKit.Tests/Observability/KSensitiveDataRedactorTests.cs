using Kimi.AppKit.Observability;
using Xunit;

namespace Kimi.AppKit.Tests.Observability;

/// <summary>
/// 脱敏器的行为契约。
/// </summary>
/// <remarks>
/// 【为什么值得测】脱敏失效是**静默**的：遥测照常上报，只是里面多了明文凭据。
/// 没有任何报错、任何监控会提示你，通常要到有人翻日志时才发现。
/// </remarks>
public class KSensitiveDataRedactorTests
{
    [Theory]
    [InlineData("""{"password":"p@ssw0rd"}""")]
    [InlineData("""{"Password":"p@ssw0rd"}""")]
    [InlineData("""{"access_token":"p@ssw0rd"}""")]
    [InlineData("""{"clientSecret":"p@ssw0rd"}""")]
    [InlineData("""{"connectionString":"p@ssw0rd"}""")]
    public void JSON里的敏感字段被抹掉(string json)
    {
        var result = KSensitiveDataRedactor.Redact(json);

        Assert.DoesNotContain("p@ssw0rd", result);
        Assert.Contains(KSensitiveDataRedactor.Mask, result);
    }

    [Fact]
    public void 非敏感字段原样保留()
    {
        var result = KSensitiveDataRedactor.Redact("""{"userName":"alice","password":"x"}""");

        Assert.Contains("alice", result);
        Assert.DoesNotContain("\"x\"", result);
    }

    [Fact]
    public void Bearer令牌被抹掉()
    {
        var result = KSensitiveDataRedactor.Redact("Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.abc");

        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", result);
    }

    [Fact]
    public void 查询串里的凭据被抹掉()
    {
        // ⚠️ 前身漏了这一类。URL 几乎必然被记录（span 名、url.full 属性、反代日志），
        //    而 SignalR 的 WebSocket 握手就是把令牌放在查询串里传的。
        var result = KSensitiveDataRedactor.Redact("https://api.example.com/hubs/x?access_token=SECRETVALUE&id=7");

        Assert.DoesNotContain("SECRETVALUE", result);
        Assert.Contains("id=7", result);
    }

    [Fact]
    public void 业务自定义字段可追加()
    {
        var result = KSensitiveDataRedactor.Redact("""{"userPin":"123456"}""", ["userPin"]);

        Assert.DoesNotContain("123456", result);
    }

    [Fact]
    public void 未登记的自定义字段会漏掉这是已知边界()
    {
        // 这条测试是**故意断言缺陷存在**的：脱敏靠字段名匹配，没登记就漏，且不报错。
        // 写成测试是为了让「请求体默认不进遥测」这个决定有据可依——
        // 哪天有人想打开 CaptureRequestBody，会先看到这条。
        var result = KSensitiveDataRedactor.Redact("""{"userPin":"123456"}""");

        Assert.Contains("123456", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空输入原样返回(string? input) =>
        Assert.Equal(input, KSensitiveDataRedactor.Redact(input));

    [Fact]
    public void Authorization头只保留方案名()
    {
        Assert.Equal($"Bearer {KSensitiveDataRedactor.Mask}",
            KSensitiveDataRedactor.RedactAuthorizationHeader("Bearer abc.def.ghi"));

        // 没有空格分隔时整体抹掉，不能把裸令牌当"方案名"留下。
        Assert.Equal(KSensitiveDataRedactor.Mask,
            KSensitiveDataRedactor.RedactAuthorizationHeader("abcdefghi"));
    }
}
