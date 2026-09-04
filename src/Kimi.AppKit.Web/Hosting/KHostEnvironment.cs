using Kimi.AppKit.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Kimi.AppKit.Web.Hosting;

/// <summary>
/// 服务端的 <see cref="IKEnvironment"/> 实现，转接 <see cref="IHostEnvironment"/>。
/// </summary>
/// <remarks>
/// 【为什么要这层转接】WASM 客户端拿不到 <see cref="IHostEnvironment"/>
/// （它那边是 <c>IWebAssemblyHostEnvironment</c>，两者没有共同基类），
/// 而 <c>KEnvChip</c> 这类组件两端都要渲染。抽象在 Core，两端各给一个实现。
/// </remarks>
internal sealed class KHostEnvironment : IKEnvironment
{
    private readonly IHostEnvironment _environment;

    public KHostEnvironment(IHostEnvironment environment) => _environment = environment;

    public string Name => _environment.EnvironmentName;

    public bool IsProduction => _environment.IsProduction();
}

/// <summary>运行环境的注册入口。</summary>
public static class KEnvironmentServiceCollectionExtensions
{
    /// <summary>
    /// 注册服务端的 <see cref="IKEnvironment"/>。
    /// </summary>
    /// <remarks>
    /// ⚠️ 用到 <c>&lt;KEnvChip /&gt;</c>（非生产环境警示条）就必须调它，
    /// 否则那个组件在渲染时抛「无法解析 IKEnvironment」。
    /// 而它恰恰是**告诉你这不是生产环境**的那个标识——渲染不出来时，
    /// 一个配错环境变量的实例看起来就和正式站一模一样。
    /// </remarks>
    public static IServiceCollection AddAppKitEnvironment(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IKEnvironment, KHostEnvironment>();

        return services;
    }
}
