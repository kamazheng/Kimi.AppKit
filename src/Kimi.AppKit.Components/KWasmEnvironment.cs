using Kimi.AppKit.Core.Abstractions;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kimi.AppKit.Components;

/// <summary>
/// WASM 客户端的 <see cref="IKEnvironment"/> 实现，转接 <see cref="IWebAssemblyHostEnvironment"/>。
/// </summary>
/// <remarks>
/// 【为什么需要这层转接】服务端与 WASM 客户端各有一套互不兼容的宿主环境类型
/// （<c>IHostEnvironment</c> vs <c>IWebAssemblyHostEnvironment</c>）。
/// 服务端实现 <see cref="KHostEnvironment"/> 与本类同包；这里是它在客户端的对应物。
///
/// ⚠️ **两端都要注册，缺一个就 500。** <c>&lt;KEnvChip /&gt;</c> 这类组件会被
/// 服务端预渲染一次、WASM 接管后再跑一次，两次用的是**两个不同的容器**。
/// 只注册服务端那份的话，WASM 端渲染 <c>KEnvChip</c> 时找不到 <c>IKEnvironment</c>，
/// 直接抛 <c>InvalidOperationException</c>，表现是整页无法交互（渲染树崩溃）。
/// </remarks>
internal sealed class KWasmEnvironment(IWebAssemblyHostEnvironment environment) : IKEnvironment
{
    public string Name => environment.Environment;

    public bool IsProduction => string.Equals(environment.Environment, "Production", StringComparison.OrdinalIgnoreCase);
}

/// <summary>WASM 客户端运行环境的注册入口。</summary>
public static class KClientEnvironmentServiceCollectionExtensions
{
    /// <summary>
    /// 注册 WASM 客户端的 <see cref="IKEnvironment"/>。
    /// </summary>
    /// <remarks>
    /// ⚠️ 用到 <c>&lt;KEnvChip /&gt;</c> 就必须两端都调：服务端调
    /// <see cref="KEnvironmentServiceCollectionExtensions.AddAppKitEnvironment"/>，
    /// 客户端调本方法。两者故意不同名——避免同一个 <c>using</c> 范围内
    /// 因扩展方法同名产生调用歧义。
    /// </remarks>
    public static IServiceCollection AddAppKitClientEnvironment(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IKEnvironment, KWasmEnvironment>();

        return services;
    }
}
