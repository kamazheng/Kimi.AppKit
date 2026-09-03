using Kimi.AppKit.Core.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Kimi.AppKit.Components.Buttons;

/// <summary>
/// <see cref="ErrorCatchButton"/> 的悬浮操作按钮（FAB）变体——移动端/紧凑布局下的主操作入口。
/// </summary>
public class ErrorCatchFab : MudFab
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
    /// <remarks>同 <see cref="ErrorCatchIconButton"/>：临时改写继承来的
    /// <see cref="MudFab.StartIcon"/> 参数并在同一次同步调用内恢复。</remarks>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!Display) return;

        var originalIcon = StartIcon;
        if (_processingState.IsProcessing) StartIcon = Icons.Material.Filled.HourglassTop;

        base.BuildRenderTree(builder);

        StartIcon = originalIcon;
    }
}
