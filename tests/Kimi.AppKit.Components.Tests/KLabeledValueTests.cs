using Bunit;
using Kimi.AppKit.Components.Components;
using Xunit;

namespace Kimi.AppKit.Components.Tests;

/// <summary>详情页"字段名: 字段值"只读展示。</summary>
public class KLabeledValueTests : BunitContext
{
    [Fact]
    public void 显示标签与值()
    {
        var cut = Render<KLabeledValue>(p => p
            .Add(x => x.Label, "客户端 ID")
            .Add(x => x.Value, "kmold-mes"));

        Assert.Contains("客户端 ID", cut.Markup);
        Assert.Contains("kmold-mes", cut.Markup);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 值为空时显示占位符而不是空白(string? value)
    {
        var cut = Render<KLabeledValue>(p => p
            .Add(x => x.Label, "备注")
            .Add(x => x.Value, value));

        Assert.Contains("—", cut.Markup);
    }

    [Fact]
    public void 占位符可以自定义()
    {
        var cut = Render<KLabeledValue>(p => p
            .Add(x => x.Label, "备注")
            .Add(x => x.Value, (string?)null)
            .Add(x => x.EmptyText, "未填写"));

        Assert.Contains("未填写", cut.Markup);
    }

    [Fact]
    public void 自定义内容优先于纯文本值()
    {
        var cut = Render<KLabeledValue>(p => p
            .Add(x => x.Label, "状态")
            .Add(x => x.Value, "这个值应该被忽略")
            .AddChildContent("<span class=\"custom\">自定义内容</span>"));

        Assert.Contains("自定义内容", cut.Markup);
        Assert.DoesNotContain("这个值应该被忽略", cut.Markup);
    }
}
