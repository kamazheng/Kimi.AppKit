using Kimi.AppKit.Components.Components;
using MudBlazor;
using Xunit;

namespace Kimi.AppKit.Components.Tests;

/// <summary>
/// 枚举 → 颜色/图标映射芯片。取代 MES 里 47 处枚举 → 颜色手写 switch。
/// 只测 <see cref="KEnumChip{TEnum}.Resolve"/> 这个纯函数，不渲染 MudChip——
/// 渲染 MudChip 需要拉起 MudBlazor 的 IKeyInterceptorService/JS 互操作，
/// 而本测试关心的风险点（轮转确定性、特性覆盖）与渲染管线无关。
/// </summary>
public class KEnumChipTests
{
    private enum SampleStatus
    {
        Draft,
        [EnumChipColor(Color.Success)] Approved,
        Rejected,
    }

    [Fact]
    public void 未贴特性时使用枚举成员名作为标签()
    {
        var (label, _) = KEnumChip<SampleStatus>.Resolve(SampleStatus.Draft);

        Assert.Equal("Draft", label);
    }

    [Fact]
    public void 贴了特性的枚举值使用特性指定的颜色()
    {
        var (_, color) = KEnumChip<SampleStatus>.Resolve(SampleStatus.Approved);

        Assert.Equal(Color.Success, color);
    }

    [Fact]
    public void 未贴特性的不同枚举值轮转出不同颜色()
    {
        var (_, draftColor) = KEnumChip<SampleStatus>.Resolve(SampleStatus.Draft);
        var (_, rejectedColor) = KEnumChip<SampleStatus>.Resolve(SampleStatus.Rejected);

        Assert.NotEqual(draftColor, rejectedColor);
    }
}
