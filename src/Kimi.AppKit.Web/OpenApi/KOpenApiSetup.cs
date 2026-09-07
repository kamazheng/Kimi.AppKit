using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Web.Authentication;
using Microsoft.Extensions.Options;

// ⚠️ OpenAPI.NET v2 把这些类型从 Microsoft.OpenApi.Models 提到了 Microsoft.OpenApi。
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Kimi.AppKit.Web.OpenApi;

/// <summary>
/// 生成 OpenAPI 文档并挂交互式 API 参考页。
/// </summary>
/// <remarks>
/// 【路径】文档 <c>/openapi/v1.json</c>，UI <c>/scalar/v1</c>。
///
/// 【为什么不是 Swashbuckle】.NET 9 起官方模板已移除它，改用内置的
/// <c>Microsoft.AspNetCore.OpenApi</c>。内置包只产出文档、不含 UI，
/// UI 由 Scalar 提供（.NET 10 官方教程用的就是它）。不要再把 Swashbuckle 引回来。
///
/// 【安全】文档会**完整暴露 API 形状**。默认只在开发环境开放；
/// 生产要开必须显式置 <see cref="KOpenApiOptions.Enabled"/>，且此时强制要求登录——
/// 给接入方看可以，给公网匿名看不行。
/// </remarks>
public static class KOpenApiSetup
{
    private const string DocumentName = "v1";
    private const string BearerScheme = "Bearer";
    private const string OAuth2Scheme = "OAuth2";

    /// <summary>注册 OpenAPI 文档生成。</summary>
    public static IServiceCollection AddAppKitOpenApi(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KOpenApiOptions>(configuration.GetSection(KOpenApiOptions.SectionName));

        services.AddOpenApi(DocumentName, openApi =>
            openApi.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "KMoldApp API",
                    Version = "v1",
                    Description = "本应用对外开放的接口。所有 /api 接口都要求 Bearer 令牌。",
                };

                AddBearerScheme(document);
                return Task.CompletedTask;
            }));

        return services;
    }

    /// <summary>映射文档与 UI。</summary>
    /// <param name="app">应用。</param>
    /// <param name="productionPolicy">
    /// 生产环境开放文档时所需的授权策略名。为 null 则只要求已登录。
    /// ⚠️ 文档页的可见性 ≠ 接口的权限：这里管的只是「谁能看到这一页」，
    /// 每个接口自己的权限由它自己挂。
    /// </param>
    public static WebApplication MapAppKitOpenApi(this WebApplication app, string? productionPolicy = null)
    {
        var options = app.Services.GetRequiredService<IOptions<KOpenApiOptions>>().Value;

        // 未显式配置时按环境决定：开发开放、生产关闭。
        if (!(options.Enabled ?? app.Environment.IsDevelopment())) return app;

        var oidc = app.Services.GetRequiredService<IOptions<KOidcOptions>>().Value;

        var document = app.MapOpenApi();
        var reference = app.MapScalarApiReference(scalar =>
        {
            scalar.WithTitle("KMoldApp API").WithTheme(ScalarTheme.BluePlanet);

            // ⚠️ **不配这段就没法在页面上测接口**：所有 /api 都要 Bearer 令牌，
            //    而文档页自己拿不到——只能让人手工去别处复制一个 access token 贴进来。
            //    配上之后 Scalar 上会出现「Authorize」按钮，走标准授权码 + PKCE 取令牌。
            // ⚠️ 回调地址必须**已登记在 IdP 的该客户端下**，否则点了授权只会撞
            //    invalid_redirect_uri。地址是 {站点}/scalar/v1（Scalar 的默认回调就是它自己）。
            if (oidc.IsConfigured)
            {
                ConfigureOidcFlow(scalar, oidc);
            }

            // ⚠️ **气隙部署的关键三项。** Scalar 默认会往公网发请求：
            //    字体走 CDN、AI 助手与遥测打 api.scalar.com。
            //    内网客户那里这些请求要等到**超时**才失败，页面首屏因此长时间卡住——
            //    表现是「文档页打不开」，而实际上文档本身早就返回了。
            scalar.DisableDefaultFonts();
            scalar.DisableAgent();
            scalar.DisableTelemetry();
        });

        // ⚠️ **文档页的可见性 ≠ 接口的权限，两者必须分开想。**
        //    这里管的只是「谁能看到这一页」：API 形状属于内部信息，生产环境要求登录。
        //    而**每个接口自己的权限由它自己挂**（见各 MapCrudEndpoints 的
        //    RequireAuthorization）——文档页不该、也无法替它们决定。
        //    早先这里挂的是 AdminOnly，那等于「只有管理员能通过文档页调接口」，
        //    可一个普通用户本来就有权调的只读接口，凭什么从这一页调就不行？
        //    页面归页面、接口归接口，混在一起会让人误以为接口权限比实际更严。
        if (!app.Environment.IsDevelopment())
        {
            if (productionPolicy is { Length: > 0 })
            {
                document.RequireAuthorization(productionPolicy);
                reference.RequireAuthorization(productionPolicy);
            }
            else
            {
                document.RequireAuthorization();
                reference.RequireAuthorization();
            }
        }
        else
        {
            document.AllowAnonymous();
            reference.AllowAnonymous();
        }

        return app;
    }

    /// <summary>
    /// 让文档页自己能走 OIDC 取令牌，从而在页面上直接测接口。
    /// </summary>
    /// <remarks>
    /// ⚠️ **必须用授权码 + PKCE，不要用隐式流。** 隐式流把令牌直接甩在 URL 片段里，
    /// 会进浏览器历史与 Referer；OAuth 2.1 已经把它废弃。
    /// ⚠️ **不要在这里配 ClientSecret。** 这是跑在浏览器里的公共客户端，
    /// 任何写进去的密钥对访问者都是明文可见的——那等于把机密客户端凭据公开。
    /// PKCE 就是为替代 secret 而生的。
    /// ⚠️ IdP 侧必须把 <c>{站点}/scalar/v1</c> 登记进该客户端的 RedirectUris，
    /// 否则点授权只会撞 <c>invalid_redirect_uri</c>，而 Scalar 的提示很含糊。
    /// </remarks>
    private static void ConfigureOidcFlow(ScalarOptions scalar, KOidcOptions oidc)
    {
        var issuer = oidc.Issuer!.TrimEnd('/');

        scalar
            .AddPreferredSecuritySchemes(OAuth2Scheme)
            .AddOAuth2Authentication(OAuth2Scheme, scheme =>
                // ⚠️ ClientId 在**流**上而不是在方案上（OAuthFlow 基类），
                //    写到 scheme 上编译不过——同一个 IdP 客户端在不同流里可以不同。
                scheme.Flows = new ScalarFlows
                {
                    AuthorizationCode = new AuthorizationCodeFlow
                    {
                        ClientId = oidc.ClientId,
                        AuthorizationUrl = $"{issuer}/connect/authorize",
                        TokenUrl = oidc.TokenEndpoint ?? $"{issuer}/connect/token",
                        Pkce = Pkce.Sha256,
                        SelectedScopes = [.. oidc.Scopes],
                    },
                });
    }

    /// <summary>把 Bearer 方案写进文档，让 Scalar 的「Authorize」按钮能用。</summary>
    private static void AddBearerScheme(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "由身份服务签发的 access token。",
        };
    }
}

/// <summary>API 文档页的开关。</summary>
public sealed class KOpenApiOptions
{
    public const string SectionName = "OpenApi";

    /// <summary>留空 = 按环境决定（开发开、生产关）。生产显式置 true 时会要求管理员登录。</summary>
    public bool? Enabled { get; set; }
}
