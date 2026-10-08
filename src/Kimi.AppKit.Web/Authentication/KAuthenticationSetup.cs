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

        // ⚠️ **API 路径不重定向到登录页，直接给状态码。**
        //    Cookie handler 出厂行为是 302 到 LoginPath，那对浏览器导航是对的，
        //    对 API 调用方却是灾难：拿到的是一页登录 HTML，`response.json()` 当场抛
        //    SyntaxError，错误信息完全指不到「其实是没登录」。
        //    ⚠️ 判据用 Accept 头而不只是路径前缀：路径前缀由各应用自己定，
        //    而「我要的是 JSON」是调用方自己声明的，跨应用都成立。
        cookie.Events.OnRedirectToLogin = context =>
            RespondWithStatusCodeForApi(context, StatusCodes.Status401Unauthorized);

        cookie.Events.OnRedirectToAccessDenied = context =>
            RespondWithStatusCodeForApi(context, StatusCodes.Status403Forbidden);
    }

    /// <summary>API 请求给状态码，其余照常重定向。</summary>
    private static Task RespondWithStatusCodeForApi(
        RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (WantsJson(context.Request))
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    private static bool WantsJson(HttpRequest request) =>
        request.Path.StartsWithSegments("/api")
        || request.Headers.Accept.Any(v => v?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        || string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

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

        // ⚠️ 默认 true。关掉它意味着元数据（含签名公钥集）走明文，
        //    中间人可替换公钥并伪造出本应用会接受的令牌——认证根基被击穿。
        //    仅本地联调（IdP 没配证书）时才该关。
        oidc.RequireHttpsMetadata = options.RequireHttpsMetadata;
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

        // ⚠️ 启动时就拒绝多个 resource，不要留到登录时才出问题。
        //    RFC 8707 的多值语义是**重复参数**（resource=A&resource=B），而
        //    OpenIdConnectMessage.Parameters 是 IDictionary<string, string>，
        //    结构上就传不了同名重复参数；退而求其次用空格拼接更糟——
        //    OpenIddict 的 GetResources() 读 request.Resources 数组、**不按空格切**
        //    （对比 GetScopes() 才是 GetValues(Scope, Separators.Space)），
        //    于是整串会被当成**一个** aud 值，下游永远匹配不上，且完全静默。
        //    要调多个下游，走「refresh_token 现换窄 aud 令牌」——那本来就是更安全的做法，
        //    一张对所有下游通用的令牌会让下游 A 能转手冒充用户去调下游 B。
        if (options.Resources.Count > 1)
        {
            throw new InvalidOperationException(
                "OpenIDConnect:Resources 目前只支持一个值（RFC 8707 多值需重复参数，" +
                "OpenIdConnectMessage 的参数字典不支持）。要调用多个下游服务，" +
                "请在调用前用 refresh_token 换取 aud 指向目标服务的 access token。");
        }

        // ⚠️ 这三件事必须挂在**同一个** OnRedirectToIdentityProvider 上。
        //    事件是赋值不是叠加，分开写第二个会把第一个整个顶掉，且没有任何编译或运行时信号。
        oidc.Events.OnRedirectToIdentityProvider = context =>
        {
            var request = context.HttpContext.Request;

            // ⚠️ **API 请求不发起 OIDC 挑战，直接回 401。**
            //    默认挑战方案配了 OIDC 之后，未认证的 API 调用拿到的是
            //    302 + 一整页 IdP 登录 HTML，`response.json()` 当场抛 SyntaxError；
            //    浏览器里更糟——跨域重定向被报成 CORS 错误，
            //    整条信息链没有一处提到「没登录」。
            //    ⚠️ Cookie handler 上那份同样的短路**不够**：配了 OIDC 时挑战根本不走 Cookie。
            if (WantsJson(request))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.HandleResponse();
                return Task.CompletedTask;
            }

            if (options.ForceHttpsRedirectUri)
            {
                context.ProtocolMessage.RedirectUri = new UriBuilder
                {
                    Scheme = "https",
                    Host = request.Host.Host,
                    Port = request.Host.Port ?? -1,
                    Path = request.PathBase + oidc.CallbackPath,
                }.ToString();
            }

            // RFC 8707 资源指示器：声明本次令牌要拿去调哪个下游服务，IdP 据此写入 aud。
            // 不配则令牌无 aud，下游只能关掉受众校验——服务间失去边界。
            // （多值已在上面启动时拒绝，此处必然是 0 或 1 个。）
            if (options.Resources.Count > 0)
            {
                context.ProtocolMessage.SetParameter("resource", options.Resources[0]);
            }

            return Task.CompletedTask;
        };

        // ⚠️ **必须与下面拼进 post_logout_redirect_uri 的路径是同一个值。**
        //    handler 默认监听 /signout-callback-oidc；只设下面那处等于告诉 IdP
        //    「登出后回到 /signout-callback」，而**本方根本没有人监听那个地址**。
        //    后果是「点了登出回不到应用」：IdP 确实登出了、也确实跳回来了，
        //    但这个路径既没有端点、也没被 handler 接住，于是落进默认拒绝策略被再次
        //    302 去登录——用户看到的是**点登出反而弹出登录页**。
        //    ⚠️ 整条链上没有任何错误日志，两处路径同源是唯一的防线。
        oidc.SignedOutCallbackPath = options.SignOutCallbackPath;

        // 登出回调处理完之后回到哪。⚠️ 不设的话 handler 停在回调路径上返回一页空白，
        //    看起来像「登出卡死」。也不能指望从 state 里恢复跳转目标——
        //    规范不强制上游回传 state，实测确有 IdP 不回传。
        oidc.SignedOutRedirectUri = "/";

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
