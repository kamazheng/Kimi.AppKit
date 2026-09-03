namespace Kimi.AppKit.Web.Email;

/// <summary>一封待发送的邮件。</summary>
public sealed class KEmailMessage
{
    /// <summary>主题。</summary>
    public required string Subject { get; init; }

    /// <summary>正文（HTML）。</summary>
    public required string Body { get; init; }

    /// <summary>收件人。至少一个。</summary>
    public IReadOnlyList<string> To { get; init; } = [];

    /// <summary>抄送。</summary>
    public IReadOnlyList<string> Cc { get; init; } = [];

    /// <summary>密送。</summary>
    public IReadOnlyList<string> Bcc { get; init; } = [];

    /// <summary>发件地址。留空用 <see cref="KEmailOptions.FromAddress"/>。</summary>
    public string? From { get; init; }

    /// <summary>附件。</summary>
    public IReadOnlyList<KEmailAttachment> Attachments { get; init; } = [];
}

/// <summary>一个邮件附件。</summary>
/// <param name="FileName">文件名（含扩展名）。</param>
/// <param name="Content">文件内容。</param>
/// <param name="ContentType">MIME 类型。留空则按扩展名推断，推断不出用 <c>application/octet-stream</c>。</param>
/// <remarks>
/// 【⚠️ 为什么是 <c>byte[]</c> 而不是 <c>Stream</c>】
/// 前身用的是 base64 字符串，解出来包成 <c>MemoryStream</c>，
/// 而那句 <c>using var ms = new MemoryStream(data);</c> 写在 foreach **循环体内**——
/// 每轮迭代结束流就被释放，等到真正发送时早已失效。
/// 附件内容在发送那一刻才被读取，于是抛 <c>ObjectDisposedException</c>；
/// 而外层又有一个吞掉一切的 <c>catch</c>，结果是**带附件的邮件全部静默发不出去**，
/// 只在日志里留一行看不出所以然的 Error。
///
/// 改成 <c>byte[]</c> 就没有生命周期问题了：附件通常是报表、单据这个量级，
/// 全部读进内存是可接受的；真要发几百 MB 的东西，那本来就该给个下载链接而不是塞进邮件。
/// </remarks>
public sealed record KEmailAttachment(string FileName, byte[] Content, string? ContentType = null);
