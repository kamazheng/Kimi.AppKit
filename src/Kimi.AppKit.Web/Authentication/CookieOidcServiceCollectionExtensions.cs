using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Kimi.AppKit.Web.Authentication;

/// <summary>
/// Provides extension methods for configuring OpenID Connect cookie refresh functionality.
/// </summary>
internal static partial class CookieOidcServiceCollectionExtensions
{
    /// <summary>
    /// Configures cookie authentication options to support OpenID Connect token refresh.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="cookieScheme">The cookie authentication scheme name.</param>
    /// <param name="oidcScheme">The OpenID Connect scheme name.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection ConfigureCookieOidcRefresh(this IServiceCollection services, string cookieScheme, string oidcScheme)
    {
        services.AddSingleton<CookieOidcRefresher>();
        services.AddOptions<CookieAuthenticationOptions>(cookieScheme).Configure<CookieOidcRefresher>((cookieOptions, refresher) =>
        {
            cookieOptions.Events.OnValidatePrincipal = context => refresher.ValidateOrRefreshCookieAsync(context, oidcScheme);
        });
        services.AddOptions<OpenIdConnectOptions>(oidcScheme).Configure(oidcOptions =>
        {
            // Request a refresh_token.
            oidcOptions.Scope.Add(OpenIdConnectScope.OfflineAccess);
            // Store the refresh_token.
            oidcOptions.SaveTokens = true;
        });
        return services;
    }
}



