using Kimi.AppKit.Core.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Kimi.AppKit.Components.Buttons;

/// <summary>
/// <see cref="ErrorCatchButton"/>/<see cref="ErrorCatchIconButton"/>/<see cref="ErrorCatchFab"/>
/// 共用的"点击 → 请求 → loading → 错误处理"分发逻辑，避免三个按钮变体各抄一份同样容易出错的样板。
/// </summary>
public static class ErrorCatchDispatcher
{
    /// <summary>
    /// 执行一次带处理态与错误分流的点击。
    /// </summary>
    /// <remarks>
    /// 【⚠️ 信号量必须是调用方持有的实例级对象，禁止改 static】
    /// 全局共享一把锁会导致"打开对话框的按钮持有锁期间，对话框内的另一个同类按钮点击静默无反应"。
    ///
    /// 【⚠️ catch 块必须先复位 <see cref="ProcessingState.IsProcessing"/> 再展示错误】
    /// 否则按钮自身的处理中遮罩会盖住随后弹出的错误对话框，用户看起来像"点了没反应"。
    /// </remarks>
    public static async Task HandleClickAsync(
        SemaphoreSlim semaphore,
        ProcessingState state,
        Func<MouseEventArgs, Task> onClick,
        MouseEventArgs eventArgs,
        Func<Task> requestRender,
        IKNotify notify,
        IDialogService? dialogService,
        NavigationManager? navigation)
    {
        if (!await semaphore.WaitAsync(0).ConfigureAwait(false)) return;

        try
        {
            if (state.IsProcessing) return;
            state.IsProcessing = true;
            await requestRender().ConfigureAwait(false);

            await onClick(eventArgs).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // ⚠️ 顺序不能换：先复位再展示，见方法注释。
            state.IsProcessing = false;
            await requestRender().ConfigureAwait(false);
            await ApiErrorPresenter.PresentAsync(ex, notify, dialogService, navigation).ConfigureAwait(false);
        }
        finally
        {
            state.IsProcessing = false; // 幂等：catch 已经复位过，这里是保底
            await requestRender().ConfigureAwait(false);
            semaphore.Release();
        }
    }
}
