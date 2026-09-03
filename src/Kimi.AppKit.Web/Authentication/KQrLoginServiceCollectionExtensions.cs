using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kimi.AppKit.Web.Authentication;

/// <summary>二维码登录的注册入口。</summary>
public static class KQrLoginServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <see cref="KQrLogin"/>，从 <see cref="KQrLoginOptions.SectionName"/> 读配置。
    /// </summary>
    /// <remarks>
    /// ⚠️ 依赖 <c>IDataProtectionProvider</c>。宿主必须先调 <c>AddDataProtection()</c>
    /// 并**持久化密钥环**——用默认的临时密钥环时，容器一重启密钥就换了，
    /// 之前打印出去的所有二维码卡片同时失效，而现场只会看到「扫了没反应」。
    /// </remarks>
    public static IServiceCollection AddAppKitQrLogin(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<KQrLoginOptions>(configuration.GetSection(KQrLoginOptions.SectionName));
        services.TryAddSingleton<KQrLogin>();

        return services;
    }
}
