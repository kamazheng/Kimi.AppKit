using Kimi.AppKit.Core.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Kimi.AppKit.Components.Buttons;

/// <summary>
/// <see cref="ErrorCatchButton"/> 的图标按钮变体：表格行内的操作按钮（编辑/删除/下钻）
/// 这类场景没有文字标签，只有图标，处理中时把图标替换成转圈。
/// </summary>
public class ErrorCatchIconButton : MudIconButton
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ProcessingState _processingState = new();

    [Inject] private IKNotify Notify { get; set; } = null!;
    [Inject] private IDialogService? DialogService { get; set; }
    [Inject] private NavigationManager? Navigation { get; set; }

    /// <inheritdoc cref="ErrorCatchButton.Display" />
    [Parameter] public bool Display { get; set; } = true;

    /// <inheritdoc />
    protected override async Task OnClickHandler(MouseEventArgs e) =>
        await ErrorCatchDispatcher.HandleClickAsync(
            _semaphore, _processingState, args => OnClick.InvokeAsync(args), e,
            () => InvokeAsync(StateHasChanged), Notify, DialogService, Navigation);

    /// <inheritdoc />
    /// <remarks>
    /// 临时改写继承来的 <see cref="MudIconButton.Icon"/> 参数并在同一次调用内恢复——
    /// 安全的前提是 <c>BuildRenderTree</c> 全程同步执行，恢复发生在返回给调用方之前，
    /// 不会有其它代码在"改写"和"恢复"之间读到中间状态。
    /// </remarks>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!Display) return;

        var originalIcon = Icon;
        if (_processingState.IsProcessing) Icon = Icons.Material.Filled.HourglassTop;

        base.BuildRenderTree(builder);

        Icon = originalIcon;
    }
}
