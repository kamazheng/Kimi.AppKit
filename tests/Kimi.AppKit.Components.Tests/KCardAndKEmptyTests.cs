using Bunit;
using Kimi.AppKit.Components.Components;
using Xunit;

namespace Kimi.AppKit.Components.Tests;

public class KCardTests : BunitContext
{
    [Fact]
    public void 标题与说明为空时不渲染对应元素()
    {
        var cut = Render<KCard>(p => p.AddChildContent("<p>内容</p>"));

        Assert.DoesNotContain("k-card__title", cut.Markup);
        Assert.DoesNotContain("k-card__lede", cut.Markup);
        Assert.Contains("内容", cut.Markup);
    }

    [Fact]
    public void 设置标题与说明后正确渲染()
    {
        var cut = Render<KCard>(p => p
            .Add(x => x.Title, "恢复码")
            .Add(x => x.Lede, "每个只能用一次。"));

        Assert.Contains("恢复码", cut.Markup);
        Assert.Contains("每个只能用一次。", cut.Markup);
    }

    [Fact]
    public void Narrow_为真时附加收窄样式类()
    {
        var cut = Render<KCard>(p => p.Add(x => x.Narrow, true));

        Assert.Contains("k-card--narrow", cut.Markup);
    }
}

public class KEmptyTests : BunitContext
{
    [Fact]
    public void 默认展示暂无数据文案()
    {
        var cut = Render<KEmpty>();

        Assert.Contains("暂无数据", cut.Markup);
    }

    [Fact]
    public void 文案可以自定义()
    {
        var cut = Render<KEmpty>(p => p.Add(x => x.Text, "没有匹配的记录"));

        Assert.Contains("没有匹配的记录", cut.Markup);
    }

    [Fact]
    public void 可以附加操作按钮之类的子内容()
    {
        var cut = Render<KEmpty>(p => p.AddChildContent("<button>新建</button>"));

        Assert.Contains("<button>新建</button>", cut.Markup);
    }
}
