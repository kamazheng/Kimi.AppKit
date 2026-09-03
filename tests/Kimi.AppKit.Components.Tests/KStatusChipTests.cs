using Kimi.AppKit.Components.Components;
using MudBlazor;
using Xunit;

namespace Kimi.AppKit.Components.Tests;

/// <summary>
/// 语义状态芯片：颜色词汇与 KAlert 共用同一套 Severity。只测
/// <see cref="KStatusChip.ToColor"/> 这个纯函数，理由同 <see cref="KEnumChipTests"/>。
/// </summary>
public class KStatusChipTests
{
    [Theory]
    [InlineData(Severity.Success, Color.Success)]
    [InlineData(Severity.Warning, Color.Warning)]
    [InlineData(Severity.Error, Color.Error)]
    [InlineData(Severity.Info, Color.Info)]
    [InlineData(Severity.Normal, Color.Default)]
    public void Severity映射到对应的Color(Severity severity, Color expected) =>
        Assert.Equal(expected, KStatusChip.ToColor(severity));
}
