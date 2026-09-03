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
/// 处理中/错误分流逻辑见 <see cref="ErrorCatchDispatcher"/>，本类与
/// <see cref="ErrorCatchIconButton"/>/<see cref="ErrorCatchFab"/> 共用同一份。
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

    /// <summary>
    /// 按钮文案。设置后按钮在处理中会自动切换成"转圈 + 文案…"，取代手写的
    /// <c>ChildContent</c>——不设置时按钮行为与普通 <see cref="MudButton"/> 一致，
    /// 由调用方通过 <c>ChildContent</c> 完全自定义外观。
    /// </summary>
    [Parameter] public string? Label { get; set; }

    /// <inheritdoc />
    protected override async Task OnClickHandler(MouseEventArgs e) =>
        await ErrorCatchDispatcher.HandleClickAsync(
            _semaphore, _processingState, args => OnClick.InvokeAsync(args), e,
            () => InvokeAsync(StateHasChanged), Notify, DialogService, Navigation);

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!Display) return;

        if (Label is not null)
        {
            ChildContent = _processingState.IsProcessing
                ? LoadingContent(Label)
                : PlainLabelContent(Label);
        }

        base.BuildRenderTree(builder);
    }

    private static RenderFragment LoadingContent(string label) => builder =>
    {
        builder.OpenComponent<MudProgressCircular>(0);
        builder.AddAttribute(1, nameof(MudProgressCircular.Size), Size.Small);
        builder.AddAttribute(2, nameof(MudProgressCircular.Indeterminate), true);
        builder.AddAttribute(3, nameof(MudProgressCircular.Class), "ms-n1 me-2");
        builder.CloseComponent();
        builder.AddContent(4, $"{label}…");
    };

    private static RenderFragment PlainLabelContent(string label) => builder =>
        builder.AddContent(0, label);
}
