namespace Kimi.AppKit.Components.Buttons;

/// <summary>按钮的处理中状态。独立成类是为了能在多个按钮变体之间复用同一份状态机逻辑。</summary>
public sealed class ProcessingState
{
    /// <summary>是否正在处理点击触发的异步操作。</summary>
    public bool IsProcessing { get; set; }
}
