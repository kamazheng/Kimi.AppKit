using Microsoft.Extensions.DependencyInjection;
using Bunit;
using Kimi.AppKit.Components.Components;
using Kimi.AppKit.Core.Abstractions;
using Xunit;

namespace Kimi.AppKit.Components.Tests;

/// <summary>
/// 非生产环境警示条。这不是装饰——生产配置守卫与异常页脱敏都按环境分支，
/// 哪个环境在跑必须一眼可见，否则漏设环境变量的实例会被当成正式站交付。
/// </summary>
public class KEnvChipTests : BunitContext
{
    [Fact]
    public void 生产环境不渲染任何内容()
    {
        Services.AddSingleton<IKEnvironment>(new FakeEnvironment("Production", true));

        var cut = Render<KEnvChip>();

        Assert.Empty(cut.Markup.Trim());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("UAT")]
    public void 非生产环境显示环境名(string envName)
    {
        Services.AddSingleton<IKEnvironment>(new FakeEnvironment(envName, false));

        var cut = Render<KEnvChip>();

        Assert.Contains(envName, cut.Markup);
        Assert.Contains("k-envchip", cut.Markup);
    }

    private sealed class FakeEnvironment(string name, bool isProduction) : IKEnvironment
    {
        public string Name => name;
        public bool IsProduction => isProduction;
    }
}
