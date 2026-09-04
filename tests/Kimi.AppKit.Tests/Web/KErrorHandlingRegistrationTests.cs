using Kimi.AppKit.Web.ErrorHandling;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kimi.AppKit.Tests.Web;

/// <summary>
/// <c>AddAppKitErrorHandling()</c> 的装配行为。
/// </summary>
public class KErrorHandlingRegistrationTests
{
    [Fact]
    public void 未注册策略时兜底为不暴露详情()
    {
        // 在此之前 FixedErrorDetailPolicy.Disabled 的文档自称是「兜底」，
        // 但没有任何一处把它注册成默认值——漏注册策略是**启动期** DI 解析失败。
        var provider = BuildProvider(services => services.AddAppKitErrorHandling());

        var policy = provider.GetRequiredService<IErrorDetailPolicy>();

        Assert.Same(FixedErrorDetailPolicy.Disabled, policy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 消费方自己的策略不被兜底覆盖(bool registerPolicyFirst)
    {
        // 两个方向都要成立，文档才敢承诺「顺序无关」：
        // 先注册靠 TryAdd 不覆盖，后注册靠「同一服务多次 Add 时最后一个胜出」。
        var mine = new FixedErrorDetailPolicy(true);
        var provider = BuildProvider(services =>
        {
            if (registerPolicyFirst) services.AddSingleton<IErrorDetailPolicy>(mine);
            services.AddAppKitErrorHandling();
            if (!registerPolicyFirst) services.AddSingleton<IErrorDetailPolicy>(mine);
        });

        Assert.Same(mine, provider.GetRequiredService<IErrorDetailPolicy>());
    }

    [Fact]
    public void 异常处理器可被解析()
    {
        // 这条覆盖的是「装配不全 → 启动才炸」那类失败：编译通过、单测全绿，
        // 直到真的 Build() 一次容器才暴露。
        var provider = BuildProvider(services => services.AddAppKitErrorHandling());

        var handler = Assert.Single(provider.GetServices<IExceptionHandler>());

        Assert.IsType<GlobalExceptionHandler>(handler);
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);

        // ValidateOnBuild 让缺失依赖在这里就炸，而不是等到第一次解析。
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
