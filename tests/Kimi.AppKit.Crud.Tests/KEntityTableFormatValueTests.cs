using System.Reflection;
using Kimi.AppKit.Crud;
using Xunit;

namespace Kimi.AppKit.Crud.Tests;

/// <summary>
/// <see cref="KEntityTable{T}.FormatValue"/>——表格单元格的值格式化规则。
/// 只测这个纯函数，不渲染 MudTable：渲染需要拉起 IScrollManager 等一整套 MudBlazor 服务，
/// 与这里关心的格式化规则（布尔中文化、枚举取展示名、null 不崩）无关。
/// </summary>
public class KEntityTableFormatValueTests
{
    private enum Status
    {
        Draft,
        [System.ComponentModel.DataAnnotations.Display(Name = "已批准")] Approved,
    }

    private sealed class Row
    {
        public string? Name { get; set; }
        public bool Active { get; set; }
        public Status Status { get; set; }
        public int? OptionalCount { get; set; }
    }

    private static PropertyInfo Prop(string name) => typeof(Row).GetProperty(name)!;

    [Fact]
    public void null值格式化为空字符串而不是抛异常()
    {
        var text = KEntityTable<Row>.FormatValue(Prop(nameof(Row.OptionalCount)), new Row());

        Assert.Equal(string.Empty, text);
    }

    [Theory]
    [InlineData(true, "是")]
    [InlineData(false, "否")]
    public void 布尔值格式化为中文(bool value, string expected)
    {
        var text = KEntityTable<Row>.FormatValue(Prop(nameof(Row.Active)), new Row { Active = value });

        Assert.Equal(expected, text);
    }

    [Fact]
    public void 枚举值优先使用Display特性指定的展示名()
    {
        var text = KEntityTable<Row>.FormatValue(Prop(nameof(Row.Status)), new Row { Status = Status.Approved });

        Assert.Equal("已批准", text);
    }

    [Fact]
    public void 未贴特性的枚举值退化为成员名()
    {
        var text = KEntityTable<Row>.FormatValue(Prop(nameof(Row.Status)), new Row { Status = Status.Draft });

        Assert.Equal("Draft", text);
    }

    [Fact]
    public void 普通字符串原样返回()
    {
        var text = KEntityTable<Row>.FormatValue(Prop(nameof(Row.Name)), new Row { Name = "螺栓" });

        Assert.Equal("螺栓", text);
    }
}
