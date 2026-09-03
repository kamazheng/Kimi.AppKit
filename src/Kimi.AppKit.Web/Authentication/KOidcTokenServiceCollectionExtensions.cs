using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kimi.AppKit.Web.Authentication;

/// <summary>令牌服务的注册入口。</summary>
public static class KOidcTokenServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <see cref="IKOidcTokenService"/> 与它的具名 <c>HttpClient</c>。
    /// </summary>
    /// <remarks>
    /// ⚠️ 依赖 <see cref="KOidcOptions"/>，须先调 <c>AddAppKitAuthentication</c>。
    /// 缺配置**不会**阻止启动——校验发生在真正换取令牌那一刻，
    /// 一个只走 OIDC 授权码流程、从不用 ROPC 的部署不该因为没配令牌端点就起不来。
    /// </remarks>
    public static IServiceCollection AddAppKitOidcTokenService(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(KOidcTokenService.HttpClientName);
        services.TryAddScoped<IKOidcTokenService, KOidcTokenService>();

        return services;
    }
}
