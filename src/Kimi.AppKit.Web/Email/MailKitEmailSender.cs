using MailKit.Net.Smtp;
// ⚠️ System.Net.Mail 只为借用 MailAddress.TryCreate 做地址校验；
//    SmtpClient 两个命名空间都有，必须显式别名，否则撞名。
using MailAddress = System.Net.Mail.MailAddress;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Kimi.AppKit.Web.Email;

/// <summary>
/// 基于 MailKit 的邮件发送。
/// </summary>
/// <remarks>
/// 【为什么不是 <c>System.Net.Mail.SmtpClient</c>】微软自己的文档对那个类的原话是
/// **不应当用于新开发**，并直接点名 MailKit 作为替代。
/// 这不是风格问题：<c>SmtpClient</c> 对现代 TLS 与 OAuth2 的支持不足，
/// 而 Office 365 这类服务已经要求它们——换句话说，用它连不上客户实际在用的邮件服务。
///
/// 【⚠️ 每次发送新建连接】没有做连接复用。邮件是低频操作，
/// 连接池带来的复杂度（连接超时、服务端主动断开、跨请求共享可变状态）不值得。
/// 真要发几万封的场景，应该走队列 + 批量投递，那是另一个设计。
/// </remarks>
public sealed class MailKitEmailSender(
    IOptionsMonitor<KEmailOptions> options,
    IHostEnvironment environment,
    ILogger<MailKitEmailSender> logger) : IKEmailSender
{
    /// <inheritdoc />
    public async Task SendAsync(KEmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var config = options.CurrentValue;
        Validate(config);

        var mime = BuildMimeMessage(message, config);

        using var client = new SmtpClient { Timeout = (int)config.Timeout.TotalMilliseconds };

        // ⚠️ 不吞异常。失败必须让调用方知道——见 IKEmailSender 的说明。
        await client.ConnectAsync(config.Host, config.Port, config.SecureSocketOptions, cancellationToken);

        if (!string.IsNullOrEmpty(config.UserName))
        {
            // ⚠️ 配了用户名却没配密码，多半是密码该走 user-secrets / 环境变量却漏了注入。
            //    直接把 null 传给 MailKit 会变成一句「参数不能为 null」，指不到真正的原因。
            if (string.IsNullOrEmpty(config.Password))
            {
                throw new InvalidOperationException(
                    $"配置了 {KEmailOptions.SectionName}:{nameof(KEmailOptions.UserName)} 却没有密码。" +
                    $"密码不要写进 appsettings，用 user-secrets 或环境变量 " +
                    $"{KEmailOptions.SectionName}__{nameof(KEmailOptions.Password)}。");
            }

            await client.AuthenticateAsync(config.UserName, config.Password, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        // ⚠️ 只记主题与收件人数量，不记正文、不记收件地址明细——
        //    正文里可能有业务敏感内容，地址是个人信息。
        logger.LogInformation("邮件已发送。主题={Subject} 收件人数={Count}",
            message.Subject, mime.To.Count);
    }

    private static void Validate(KEmailOptions config)
    {
        // ⚠️ 在**发送时**校验，不在构造函数里。前身在 ctor 里读配置并抛，
        //    于是「没配 SMTP」会让整个 DI 容器构建失败、应用起不来——
        //    一个从来不发邮件的部署也被拖死。
        if (string.IsNullOrWhiteSpace(config.Host))
        {
            throw new InvalidOperationException(
                $"未配置 {KEmailOptions.SectionName}:{nameof(KEmailOptions.Host)}，无法发送邮件。");
        }

        if (string.IsNullOrWhiteSpace(config.FromAddress))
        {
            throw new InvalidOperationException(
                $"未配置 {KEmailOptions.SectionName}:{nameof(KEmailOptions.FromAddress)}，无法发送邮件。");
        }
    }

    private MimeMessage BuildMimeMessage(KEmailMessage message, KEmailOptions config)
    {
        var mime = new MimeMessage();

        mime.From.Add(string.IsNullOrWhiteSpace(config.FromDisplayName)
            ? MailboxAddress.Parse(message.From ?? config.FromAddress)
            : new MailboxAddress(config.FromDisplayName, message.From ?? config.FromAddress));

        AddAddresses(mime.To, message.To);
        AddAddresses(mime.Cc, message.Cc);
        AddAddresses(mime.Bcc, message.Bcc);

        if (mime.To.Count == 0 && mime.Cc.Count == 0 && mime.Bcc.Count == 0)
        {
            throw new ArgumentException("邮件没有任何有效收件人。", nameof(message));
        }

        var (subject, body) = ApplyEnvironmentMarker(message.Subject, message.Body, config);
        mime.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = body };

        foreach (var attachment in message.Attachments)
        {
            builder.Attachments.Add(
                attachment.FileName,
                attachment.Content,
                ContentType.Parse(attachment.ContentType ?? "application/octet-stream"));
        }

        mime.Body = builder.ToMessageBody();
        return mime;
    }

    /// <summary>非生产环境给主题加前缀、正文加提示，防止测试邮件被当成真通知。</summary>
    private (string Subject, string Body) ApplyEnvironmentMarker(
        string subject, string body, KEmailOptions config)
    {
        // ⚠️ 用 IHostEnvironment 判断，不直接读 ASPNETCORE_ENVIRONMENT 环境变量。
        //    前身用的是后者且写成 static，结果这段逻辑在测试里完全无法求值——
        //    「测试环境的邮件有没有加标记」这件事从来没被验证过。
        if (environment.IsProduction() || string.IsNullOrEmpty(config.NonProductionSubjectPrefix))
        {
            return (subject, body);
        }

        var marker = $"""
            <div style="padding:8px;margin-bottom:12px;border:2px solid #d32f2f;color:#d32f2f;font-weight:bold;">
                本邮件来自 {environment.EnvironmentName} 环境，不是正式通知。
            </div>
            """;

        return (config.NonProductionSubjectPrefix + subject, marker + body);
    }

    private void AddAddresses(InternetAddressList target, IReadOnlyList<string> addresses)
    {
        foreach (var address in addresses.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // ⚠️ 用 MailAddress.TryCreate 做校验，不要 try { new MailAddress(x) } catch。
            //    前身正是后者，且写在 LINQ 谓词里——一批无效地址就是一批异常，
            //    在发通知这种可能一次几百个收件人的路径上，那个开销不是可以忽略的。
            if (MailAddress.TryCreate(address, out _))
            {
                target.Add(MailboxAddress.Parse(address));
                continue;
            }

            // 单个地址不合法不该让整封信发不出去，但必须留痕。
            logger.LogWarning("收件地址 {Address} 不是合法的邮件地址，已跳过。", address);
        }
    }
}
