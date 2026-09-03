using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Kimi.AppKit.Web.Authentication;

/// <summary>
/// 鉴权装配：Cookie 始终注册；OIDC 与 JwtBearer **只在配置齐全时**注册。
/// </summary>
/// <remarks>
/// 【⚠️ 为什么 OIDC 必须条件注册，而不是「配了就用、没配就算了」】
/// <c>OpenIdConnectHandler</c> 实现 <c>IAuthenticationRequestHandler</c>，
/// 于是认证中间件对**每一个请求**都会实例化它，而实例化时
/// <c>OpenIdConnectOptions.Validate()</c> 在 <c>ClientId</c> 为空时直接抛。
/// 结果是：只要注册了 OIDC 却没给 ClientId，**全站每条路由都 500**——
/// 包括 <c>/health/live</c>，也就是编排器用来判断「容器还活着吗」的那个端点。
/// 容器因此反复重启，而日志里只有一句指不到根因的
/// "Value cannot be null. (Parameter 'ClientId')"。
///
/// 这个失败形态编译期与单测期完全看不出来（构建 0 告警、测试全绿），
/// 只有真把应用跑起来打一次请求才会暴露。
///
/// 【⚠️ 不要引入「按域名跳过证书校验」的 BackchannelHttpHandler】
/// 前身挂过一个对写死的两个内网域名无条件返回「证书有效」、对其余主机一律返回无效的
/// handler。两个方向都是错的：前者让 OIDC 后台通道（令牌端点、JWKS）可被中间人替换，
/// 从而伪造出本应用会接受的令牌；后者让任何合法证书的 IdP 都连不上。
/// **默认的证书校验就是对的，不要覆盖它。**
/// </remarks>
public static class KAuthenticationSetup
{
    /// <summary>装配认证。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置，读 <see cref="KOidcOptions.SectionName"/> 节。</param>
    /// <param name="configure">在配置绑定之后进一步调整。</param>
    public static IServiceCollection AddAppKitAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<KOidcOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new KOidcOptions();
        configuration.GetSection(KOidcOptions.SectionName).Bind(options);
        configure?.Invoke(options);

        services.Configure<KOidcOptions>(o =>
        {
            configuration.GetSection(KOidcOptions.SectionName).Bind(o);
            configure?.Invoke(o);
        });

        var builder = services.AddAuthentication(schemes =>
        {
            // ⚠️ 默认【认证】方案必须是 Cookie：ROPC 登录与 OIDC 回调都把身份写进 Cookie。
            //    OIDC 是 remote handler，对普通 GET 的 AuthenticateAsync 返回 NoResult；
            //    若默认认证=OIDC，服务端预渲染读不到 Cookie → User 匿名 → 又被重定向去登录，
            //    表现是「已经登录了却一直被弹回登录页」。
            schemes.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;

            // ⚠️ 未配置 OIDC 时挑战方案退回 Cookie。挑战一个没注册的方案会抛
            //    InvalidOperationException，症状同样是全站 500。
            schemes.DefaultChallengeScheme = options.IsConfigured
                ? KAuthenticationSchemes.Oidc
                : CookieAuthenticationDefaults.AuthenticationScheme;

            schemes.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        });

        if (options.IsConfigured)
        {
            builder.AddJwtBearer(o => ConfigureJwtBearer(o, options));
            builder.AddOpenIdConnect(KAuthenticationSchemes.Oidc, o => ConfigureOidc(o, options));
        }

        builder.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme,
            o => ConfigureCookie(o, options));

        if (options.IsConfigured)
        {
            services.ConfigureCookieOidcRefresh(
                CookieAuthenticationDefaults.AuthenticationScheme, KAuthenticationSchemes.Oidc);
        }
        else
        {
            // 装配阶段还没有 ILogger 可用，只能走 Console。这条提示很重要：
            // 没有它，「为什么点登录没反应」要查很久。
            Console.WriteLine(
                $"[启动提示] 未配置 {KOidcOptions.SectionName}:Issuer / ClientId，" +
                "已跳过 OIDC 与 JwtBearer 注册。应用可正常启动、健康检查与文档页可访问，" +
                "但单点登录与 API 令牌校验不可用。生产部署必须配齐这两项。");
        }

        return services;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions cookie, KOidcOptions options)
    {
        cookie.LoginPath = options.LoginPath;
        cookie.AccessDeniedPath = options.AccessDeniedPath ?? options.LoginPath;

        // 未注册 JwtBearer 时**不能**转发——转发到不存在的方案会抛。
        if (!options.IsConfigured) return;

        // ⚠️ 判据是「请求带没带 Bearer 令牌」，**不是**端点上有没有 [ApiController]。
        //    前身用的是后者，而那个特性只存在于 MVC 控制器上；
        //    minimal API 端点（本包的 MapCrudEndpoints 就是）压根没有这个元数据，
        //    于是带 Bearer 令牌调它们不会走 JWT 校验，转而按 Cookie 认证 → 一路 401。
        //    这个坑随着端点从控制器迁到 minimal API 才出现，且**没有任何编译或测试信号**。
        cookie.ForwardDefaultSelector = context =>
            HasBearerToken(context) || IsHubPath(context, options)
                ? JwtBearerDefaults.AuthenticationScheme
                : null;
    }

    private static bool HasBearerToken(HttpContext context) =>
        context.Request.Headers.Authorization.Count > 0
        && context.Request.Headers.Authorization[0]?.StartsWith(
            "Bearer ", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsHubPath(HttpContext context, KOidcOptions options) =>
        context.Request.Path.StartsWithSegments(options.HubPathPrefix);

    private static void ConfigureJwtBearer(JwtBearerOptions jwt, KOidcOptions options)
    {
        // ⚠️ 走 ConfigureAppKitJwtBearer：它用 ConfigurationManager 做**带缓存**的 OIDC 发现、
        //    照常校验证书、并强制 ValidateAudience。
        //    前身在 IssuerSigningKeyResolver 里每次校验令牌都同步阻塞拉一次 JWKS，
        //    还关掉了证书校验（中间人可冒充 IdP 返回自己的公钥，伪造出本应用会接受的令牌），
        //    并关掉了 ValidateAudience（同一 IdP 上任意其它客户端的令牌都会被接受）。
        jwt.ConfigureAppKitJwtBearer(
            authority: options.Issuer!,
            audience: options.ClientId!,
            configureValidation: v =>
            {
                // 对齐 OIDC handler 的 name 口径：JWT 默认 NameClaimType 是长 URI，
                // 取不到令牌里的 "name" → Identity.Name 为空 → 界面上显示 "Unknown"。
                v.NameClaimType = JwtRegisteredClaimNames.Name;
            });

        jwt.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // SignalR 的 WebSocket 握手带不了 Authorization 头，令牌只能走查询串。
                // ⚠️ 因此这类 URL 必须脱敏后再进日志与遥测（见 KSensitiveDataRedactor）。
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments(options.HubPathPrefix))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    }

    private static void ConfigureOidc(OpenIdConnectOptions oidc, KOidcOptions options)
    {
        oidc.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        oidc.Authority = options.Issuer;
        oidc.ClientId = options.ClientId;
        oidc.ClientSecret = options.ClientSecret;
        oidc.ResponseType = OpenIdConnectResponseType.Code;
        oidc.SaveTokens = true;

        foreach (var scope in options.Scopes) oidc.Scope.Add(scope);

        // ⚠️ MapInboundClaims 必须为 false（铁律 7）。默认会把 role/sub/email 改写成
        //    长长的 WS-* URI，于是 RoleClaimType = "role" 与直接读 azp/sub 的代码全部失配。
        //    失败形态是**静默的**：令牌里明明有角色，接口却一路 403。
        oidc.MapInboundClaims = false;
        oidc.TokenValidationParameters.NameClaimType = JwtRegisteredClaimNames.Name;
        oidc.TokenValidationParameters.RoleClaimType = "role";

        oidc.ForwardDefaultSelector = context =>
            HasBearerToken(context) || IsHubPath(context, options)
                ? JwtBearerDefaults.AuthenticationScheme
                : KAuthenticationSchemes.Oidc;

        if (options.ForceHttpsRedirectUri)
        {
            oidc.Events.OnRedirectToIdentityProvider = context =>
            {
                var request = context.HttpContext.Request;
                context.ProtocolMessage.RedirectUri = new UriBuilder
                {
                    Scheme = "https",
                    Host = request.Host.Host,
                    Port = request.Host.Port ?? -1,
                    Path = request.PathBase + oidc.CallbackPath,
                }.ToString();

                return Task.CompletedTask;
            };
        }

        oidc.Events.OnRedirectToIdentityProviderForSignOut = context =>
        {
            var request = context.Request;
            context.ProtocolMessage.SetParameter("id_token_hint", context.ProtocolMessage.IdTokenHint);
            context.ProtocolMessage.SetParameter(
                "post_logout_redirect_uri",
                $"{request.Scheme}://{request.Host}{request.PathBase}{options.SignOutCallbackPath}");
            context.ProtocolMessage.RequestType = OpenIdConnectRequestType.Logout;

            return Task.CompletedTask;
        };
    }
}
