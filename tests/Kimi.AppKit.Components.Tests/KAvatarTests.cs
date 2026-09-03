using Bunit;
using Kimi.AppKit.Components.Components;
using Xunit;

namespace Kimi.AppKit.Components.Tests;

/// <summary>
/// 首字母头像。取代前身在 4 个不同文件里各写一份的 Initial() 私有方法。
/// </summary>
public class KAvatarTests : BunitContext
{
    [Theory]
    [InlineData("Kimi", "K")]
    [InlineData("kimi", "K")]      // 统一转大写
    [InlineData("  Kimi", "K")]    // trim 后再取首字母
    public void 取名字首字母并转大写(string name, string expected)
    {
        var cut = Render<KAvatar>(p => p.Add(x => x.Name, name));

        Assert.Contains($">{expected}<", cut.Markup);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 名字为空时使用默认兜底字符(string? name)
    {
        var cut = Render<KAvatar>(p => p.Add(x => x.Name, name));

        Assert.Contains(">K<", cut.Markup);
    }

    [Fact]
    public void 可以自定义兜底字符()
    {
        var cut = Render<KAvatar>(p => p
            .Add(x => x.Name, (string?)null)
            .Add(x => x.Fallback, "?"));

        Assert.Contains(">?<", cut.Markup);
    }

    [Fact]
    public void 可以自定义_CSS_类名以复用不同样式()
    {
        var cut = Render<KAvatar>(p => p
            .Add(x => x.Name, "Kimi")
            .Add(x => x.CssClass, "k-avatar"));

        Assert.Contains("class=\"k-avatar\"", cut.Markup);
    }
}
