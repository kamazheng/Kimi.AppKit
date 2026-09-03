namespace Kimi.AppKit.Web.Email;

/// <summary>发送邮件。</summary>
/// <remarks>
/// 【⚠️ 失败会抛，不会被吞掉】前身的 <c>SendAsync</c> 用一个 <c>catch (Exception)</c>
/// 把所有异常记进日志就算完，调用方**永远拿不到失败信号**。
/// 于是「审批通知没发出去」这类事故只能靠有人翻日志才发现，
/// 而业务流程还在按「已通知」继续往下走。
///
/// 现在的约定是：**发送失败就抛**。调用方自己决定要重试、要降级、还是要让整个操作失败。
/// 真的「发不出去也无所谓」的场景，在调用处显式 try/catch 并写清为什么可以忽略。
/// </remarks>
public interface IKEmailSender
{
    /// <summary>发送一封邮件。失败抛异常。</summary>
    /// <param name="message">邮件内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendAsync(KEmailMessage message, CancellationToken cancellationToken = default);
}
