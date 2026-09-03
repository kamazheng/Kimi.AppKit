using Kimi.AppKit.Core.Abstractions;
using MudBlazor;

namespace Kimi.AppKit.Components.Dialogs;

/// <summary>
/// <see cref="IKConfirm"/> 的 MudBlazor 实现，经 <see cref="IDialogService"/> 弹对话框。
/// </summary>
public sealed class MudConfirm(IDialogService dialogService) : IKConfirm
{
    /// <inheritdoc />
    public async Task<bool> DangerAsync(string title, string message, string confirmText = "删除")
    {
        var result = await dialogService.ShowMessageBoxAsync(
            title, message, yesText: confirmText, cancelText: "取消");
        return result == true;
    }

    /// <inheritdoc />
    public async Task<string?> WithReasonAsync(string title, string message, string reasonLabel = "原因")
    {
        var parameters = new DialogParameters<KReasonConfirmDialog>
        {
            { x => x.Title, title },
            { x => x.Message, message },
            { x => x.ReasonLabel, reasonLabel },
        };

        var dialog = await dialogService.ShowAsync<KReasonConfirmDialog>(title, parameters);
        var result = await dialog.Result;

        return result is { Canceled: false, Data: string reason } ? reason : null;
    }
}
