using MudBlazor;

namespace Kimi.AppKit.Components.Components;

/// <summary>
/// 声明式指定某个枚举值在 <see cref="KEnumChip{TEnum}"/> 里的颜色。
/// 比每个页面各写一份 switch 更不容易漏枚举值——新增枚举成员时，不贴这个特性就落到默认轮转色，
/// 至少不会是"忘了处理"而报错或显示空白。
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class EnumChipColorAttribute(Color color) : Attribute
{
    public Color Color { get; } = color;
}
