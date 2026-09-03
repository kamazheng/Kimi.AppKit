using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Contracts;
using MudBlazor;

namespace Kimi.AppKit.Components.Dialogs;

/// <summary>
/// <see cref="IKNotify"/> 的 MudBlazor 实现，经 <see cref="ISnackbar"/> 弹吐司。
/// </summary>
/// <remarks>
/// ⚠️ 消费它的 Provider 必须是交互式的，见 <c>KMudProviders</c> 的说明——
/// 静态 SSR 布局里注入这个类不会报错，调用 <see cref="Ok"/> 等方法也不会报错，
/// 只是吐司永远不出现。
/// </remarks>
public sealed class MudNotify(ISnackbar snackbar) : IKNotify
{
    /// <inheritdoc />
    public void Ok(string message) => snackbar.Add(message, Severity.Success);

    /// <inheritdoc />
    public void Warn(string message) => snackbar.Add(message, Severity.Warning);

    /// <inheritdoc />
    public void Fail(string message) => snackbar.Add(message, Severity.Error);

    /// <inheritdoc />
    public void Result(KResult result, string successText)
    {
        if (result.Succeeded)
        {
            Ok(successText);
            return;
        }

        // 多条错误逐条展示，而不是拼成一行——长句子在吐司里会被截断，
        // 用户根本读不完就自动消失了。
        foreach (var error in result.Errors) Fail(error);
    }
}
