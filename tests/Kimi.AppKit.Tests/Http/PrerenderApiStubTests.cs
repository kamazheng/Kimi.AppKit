using Kimi.AppKit.Http;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using Xunit;

namespace Kimi.AppKit.Tests.Http;

/// <summary><see cref="PrerenderApiStub.AddPrerenderRefitClient{T}"/> 的行为契约。</summary>
public class PrerenderApiStubTests
{
    [Fact]
    public void 可以解析出接口实例()
    {
        var services = new ServiceCollection();
        services.AddPrerenderRefitClient<ITestApi>();

        var api = services.BuildServiceProvider().GetRequiredService<ITestApi>();

        Assert.NotNull(api);
    }

    [Fact]
    public async Task 真的调用方法时抛异常而不是返回假数据()
    {
        var services = new ServiceCollection();
        services.AddPrerenderRefitClient<ITestApi>();
        var api = services.BuildServiceProvider().GetRequiredService<ITestApi>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetThingAsync(1));
    }
}
