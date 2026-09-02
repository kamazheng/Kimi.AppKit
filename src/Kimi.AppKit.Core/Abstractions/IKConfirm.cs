namespace Kimi.AppKit.Core.Abstractions;

/// <summary>
/// 危险操作的二次确认。
/// </summary>
/// <remarks>
/// 【为什么收口】前身模板里手写了 65 处 <c>ShowMessageBox</c>，而它依赖的组件库其实
/// **已经提供了一个 <c>DeleteConfirmation</c> 组件、全库 0 使用**——
/// 「包里有但没人知道」正是本框架要解决的问题之一。
/// </remarks>
public interface IKConfirm
{
    /// <summary>
    /// 危险操作确认。返回 true 表示用户确认执行。
    /// </summary>
    /// <remarks>
    /// ⚠️ 确认按钮的文案要写清楚**会发生什么**（「删除这 3 个工单」），
    /// 不要只写「确定」——用户在连点时根本不会读标题。
    /// </remarks>
    Task<bool> DangerAsync(string title, string message, string confirmText = "删除");

    /// <summary>
    /// 需要填写原因的确认。返回填写的原因；用户取消时返回 null。
    /// 用于不可逆且需要审计追责的操作（作废、强制关闭、越权放行）。
    /// </summary>
    Task<string?> WithReasonAsync(string title, string message, string reasonLabel = "原因");
}
