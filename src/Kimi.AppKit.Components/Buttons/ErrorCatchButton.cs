using Kimi.AppKit.Core.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Kimi.AppKit.Components.Buttons;

/// <summary>
/// 统一"点击 → 请求 → loading → 错误处理"的按钮。取代各页各写一遍的
/// <c>try/await/catch</c> 样板——MES 里同类模式已有 373 处遵守，证明这个抽象值得抽包。
/// </summary>
/// <remarks>
/// 【⚠️ 信号量必须是实例级，禁止改 static】
/// 如果把 <see cref="_semaphore"/> 声明成 static，会全局共享成一把锁：
/// 打开对话框的那个按钮在 <c>await</c> 对话框结果期间持有锁，
/// 此时对话框**内部**的另一个 <see cref="ErrorCatchButton"/>（比如"确认"按钮）
/// 调用 <c>WaitAsync(0)</c> 会立刻失败并静默退出——点击毫无反应，且不报任何错。
/// 这个坑只有在"按钮打开的对话框里还有另一个同类按钮"这种组合下才会触发，
/// 孤立测试单个按钮完全测不出来。
///
/// 【⚠️ catch 块必须先复位 IsProcessing 再展示错误】
/// 如果保持 <c>IsProcessing = true</c> 就去弹错误对话框，按钮自身的 loading 遮罩
/// 会持续显示、盖在错误对话框上方，用户会觉得"点了没反应，页面卡住了"，
/// 而错误对话框其实已经弹出来了，只是被遮罩挡住看不见。
/// </remarks>
public class ErrorCatchButton : MudButton
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ProcessingState _processingState = new();

    [Inject] private IKNotify Notify { get; set; } = null!;
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private NavigationManager? Navigation { get; set; }

    /// <summary>是否渲染这个按钮。<c>false</c> 时整个按钮连 DOM 都不生成——
    /// 用于"这个操作对当前用户/状态不适用"的场景，比隐藏更彻底地不占位。</summary>
    [Parameter] public bool Display { get; set; } = true;

    /// <inheritdoc />
    protected override async Task OnClickHandler(MouseEventArgs e)
    {
        if (!await _semaphore.WaitAsync(0)) return;

        try
        {
            if (_processingState.IsProcessing) return;
            _processingState.IsProcessing = true;
            await InvokeAsync(StateHasChanged);

            await OnClick.InvokeAsync(e);
        }
        catch (Exception ex)
        {
            // ⚠️ 顺序不能换：先复位再展示，见类注释。
            _processingState.IsProcessing = false;
            await InvokeAsync(StateHasChanged);
            await ApiErrorPresenter.PresentAsync(ex, Notify, DialogService, Navigation);
        }
        finally
        {
            _processingState.IsProcessing = false; // 幂等：catch 已经复位过，这里是保底
            await InvokeAsync(StateHasChanged);
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Display) base.BuildRenderTree(builder);
    }
}
