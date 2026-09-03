using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kimi.AppKit.Web.Authorization;

/// <summary>网络准入的注册入口。</summary>
public static class KNetworkGateServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <see cref="KNetworkGate"/>，从 <see cref="KNetworkGateOptions.SectionName"/> 读配置。
    /// </summary>
    /// <remarks>
    /// ⚠️ 用 <c>IOptionsMonitor</c> 而不是 <c>IOptions</c>：网段是运维会在运行期调的配置，
    /// 改一次就要重启整个服务不合理。<see cref="KNetworkGate"/> 的 CIDR 解析缓存
    /// 按配置实例做身份比较，配置换了会自动重新解析。
    /// </remarks>
    public static IServiceCollection AddAppKitNetworkGate(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<KNetworkGateOptions>(configuration.GetSection(KNetworkGateOptions.SectionName));
        services.TryAddSingleton<KNetworkGate>();

        return services;
    }
}
