using MailKit.Security;

namespace Kimi.AppKit.Web.Email;

/// <summary>邮件发送配置。</summary>
public sealed class KEmailOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Email";

    /// <summary>SMTP 主机名。**必填**。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP 端口。587（STARTTLS）是当下的常规选择，25 基本只在内网中继上还能用。</summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// 默认发件地址。**必填**。
    /// </summary>
    /// <remarks>
    /// ⚠️ 前身把它拼成 <c>$"{公司前缀}-{appShortName}@{公司域名}"</c>——只有中间一段可配，
    /// 前缀与域名写死。换个客户，这个地址在对方邮件网关上是无效域名，
    /// 按 SPF/DKIM 校验会**直接拒收**，而且拒收信回到那个不存在的地址、没人看得到。
    /// 现在整个地址从配置读取，不拼接任何固定片段。
    /// </remarks>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>发件显示名。留空则只显示地址。</summary>
    public string? FromDisplayName { get; set; }

    /// <summary>SMTP 用户名。留空表示匿名中继（只有内网中继才允许）。</summary>
    public string? UserName { get; set; }

    /// <summary>
    /// SMTP 密码。
    /// </summary>
    /// <remarks>
    /// ⚠️ **不要写进 appsettings.json**（它进版本控制）。用 user-secrets 或环境变量：
    /// <c>Email__Password</c>。这个值绝不会出现在日志与审计详情里。
    /// </remarks>
    public string? Password { get; set; }

    /// <summary>
    /// TLS 策略。默认 <see cref="SecureSocketOptions.StartTlsWhenAvailable"/>。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这一项是换掉 <c>System.Net.Mail.SmtpClient</c> 的主要动机之一：
    /// 那个类对现代 TLS 与 OAuth2 的支持不足，而 Office 365 等服务已经要求它们。
    /// 内网明文中继请显式设 <see cref="SecureSocketOptions.None"/>，
    /// 让「这条链路不加密」在配置里看得见。
    /// </remarks>
    public SecureSocketOptions SecureSocketOptions { get; set; } = SecureSocketOptions.StartTlsWhenAvailable;

    /// <summary>连接与发送的超时。</summary>
    /// <remarks>
    /// ⚠️ 必须有。邮件服务器无响应时，没有超时的发送会把请求线程一直挂住——
    /// 表现是「某个按钮点了之后整页卡死」，而日志里什么都没有。
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 非生产环境下给主题加的前缀，并在正文顶部插一条醒目提示。留空则不改。
    /// </summary>
    /// <remarks>
    /// 防的是「测试环境把真邮件发给了真客户」。
    /// </remarks>
    public string? NonProductionSubjectPrefix { get; set; } = "[测试环境] ";
}
