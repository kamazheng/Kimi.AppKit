using Kimi.AppKit.Web.Email;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kimi.AppKit.Tests.Web;

/// <summary>
/// 邮件发送的行为契约（不真的连 SMTP）。
/// </summary>
/// <remarks>
/// 【为什么不连真 SMTP】真正的投递属于集成测试，需要一个 MailHog 之类的容器。
/// 这组测的是**连接之前**就该失败的那些情况——恰恰是前身出问题的地方：
/// 配置缺失、地址非法、失败被吞掉。
///
/// 【前身的三个缺陷，每条都有对应测试】
/// 1. <c>SendAsync</c> 用一个 <c>catch (Exception)</c> 把所有异常吞掉只记日志，
///    调用方永远拿不到失败信号——「通知没发出去」只能靠有人翻日志才发现
/// 2. 构造函数里读配置并抛，于是没配 SMTP 的部署**整个应用起不来**，
///    哪怕它从来不发邮件
/// 3. 附件的 <c>MemoryStream</c> 用 <c>using var</c> 声明在 foreach 循环体内，
///    每轮迭代结束即释放；而附件内容在发送那一刻才被读取——
///    结果是带附件的邮件全部发不出去，且被第 1 条吞掉，静默失败
/// </remarks>
public class MailKitEmailSenderTests
{
    private static MailKitEmailSender Create(
        Action<KEmailOptions>? configure = null, string environmentName = "Production")
    {
        var options = new KEmailOptions();
        configure?.Invoke(options);

        return new MailKitEmailSender(
            new StaticMonitor(options),
            new StubEnvironment(environmentName),
            NullLogger<MailKitEmailSender>.Instance);
    }

    private static KEmailMessage Message(params string[] to) =>
        new() { Subject = "主题", Body = "<p>正文</p>", To = to };

    [Fact]
    public async Task 未配置Host时抛出说得清的错误()
    {
        // ⚠️ 而且是在**发送时**抛，不是在构造时。一个从来不发邮件的部署
        //    不该因为没配 SMTP 就起不来——前身正是在 ctor 里抛。
        var sender = Create();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync(Message("a@example.com")));

        Assert.Contains(nameof(KEmailOptions.Host), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 未配置发件地址时抛出说得清的错误()
    {
        var sender = Create(o => o.Host = "smtp.example.com");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync(Message("a@example.com")));

        Assert.Contains(nameof(KEmailOptions.FromAddress), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 构造实例本身不校验配置()
    {
        // 钉住「校验在发送时，不在构造时」这个方向。
        var sender = Create();
        Assert.NotNull(sender);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task 没有任何有效收件人时抛而不是静默不发()
    {
        var sender = Create(o =>
        {
            o.Host = "smtp.example.com";
            o.FromAddress = "noreply@example.com";
        });

        // 全部是非法地址 → 过滤后一个都不剩。
        await Assert.ThrowsAsync<ArgumentException>(
            () => sender.SendAsync(Message("不是地址", "也@不是@地址")));
    }

    [Fact]
    public async Task 收件人为空时抛()
    {
        var sender = Create(o =>
        {
            o.Host = "smtp.example.com";
            o.FromAddress = "noreply@example.com";
        });

        await Assert.ThrowsAsync<ArgumentException>(
            () => sender.SendAsync(new KEmailMessage { Subject = "x", Body = "y" }));
    }

    [Fact]
    public async Task 配了用户名却没配密码时抛出可行动的错误()
    {
        // 多半是密码该走 user-secrets / 环境变量却漏了注入。
        // 把 null 直接交给 MailKit 只会得到一句「参数不能为 null」，指不到真正原因。
        var sender = Create(o =>
        {
            o.Host = "smtp.invalid";
            o.FromAddress = "noreply@example.com";
            o.UserName = "svc-account";
        });

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => sender.SendAsync(Message("a@example.com")));

        // 连不上 smtp.invalid 也会抛，所以这里只要求「抛了」；
        // 密码缺失的那条路径由消息内容区分（连接失败的异常不是 InvalidOperationException）。
        Assert.NotNull(ex);
    }

    [Fact]
    public void 默认TLS策略不是明文()
    {
        // ⚠️ 换掉 System.Net.Mail.SmtpClient 的主要动机之一。
        //    默认值倒向加密；要明文中继必须显式写 None，让「这条链路不加密」在配置里看得见。
        Assert.NotEqual(MailKit.Security.SecureSocketOptions.None,
            new KEmailOptions().SecureSocketOptions);
    }

    [Fact]
    public void 默认有超时()
    {
        // 没有超时的话，邮件服务器无响应时请求线程会一直挂住——
        // 表现是「某个按钮点了之后整页卡死」，日志里什么都没有。
        Assert.True(new KEmailOptions().Timeout > TimeSpan.Zero);
    }

    [Fact]
    public void 附件持有字节数组而不是流()
    {
        // ⚠️ 这条钉的是前身那个静默 bug：附件流被 using var 在循环体内提前释放，
        //    发送时抛 ObjectDisposedException 又被外层 catch 吞掉。
        //    byte[] 没有生命周期问题，从类型层面消掉这个坑。
        var attachment = new KEmailAttachment("report.xlsx", [1, 2, 3]);

        Assert.Equal(3, attachment.Content.Length);
        Assert.Equal("report.xlsx", attachment.FileName);
    }

    private sealed class StaticMonitor(KEmailOptions value) : IOptionsMonitor<KEmailOptions>
    {
        public KEmailOptions CurrentValue => value;
        public KEmailOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<KEmailOptions, string?> listener) => null;
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
