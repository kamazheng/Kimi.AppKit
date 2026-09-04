using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Kimi.AppKit.Web.Authorization;

/// <summary>
/// 授权装配：默认拒绝，角色策略按需登记。
/// </summary>
public static class AuthorizationSetup
{
    /// <summary>
    /// 注册授权服务并设置默认拒绝策略。
    /// </summary>
    /// <remarks>
    /// 【⚠️ 这是整个授权体系里最关键的一行】
    /// <c>FallbackPolicy</c> 应用于**没有任何显式授权特性**的端点。
    /// 默认（不设置）是 <c>null</c>，等价于允许匿名——这正是前身通用查询端点
    /// 「读接口忘了加 <c>[Authorize]</c>」之所以能造成匿名可读全库的根本原因：
    /// 抽象把授权判断从「框架默认拒绝、显式放行」变成了「框架默认放行、显式拒绝」，
    /// 而"显式"这件事本质上依赖人不会忘。
    ///
    /// 设了这一行之后，新增一个 Controller/Action 若没有显式 <c>[AllowAnonymous]</c>，
    /// 默认结果是「要求登录」而不是「裸奔」——健康检查这类**必须**匿名的端点，
    /// 需要显式调用 <c>.AllowAnonymous()</c>。
    ///
    /// 【⚠️ <c>/_framework</c> 无条件放行，见 <see cref="BlazorFrameworkPath"/>】
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">
    /// 进一步配置。⚠️ 在这里覆盖 <c>FallbackPolicy</c> 会**连带丢掉 <c>/_framework</c> 放行**，
    /// 那会让 Blazor WebAssembly 应用起不来（症状见 <see cref="BlazorFrameworkPath"/>）。
    /// 要加策略请用 <c>options.AddPolicy(...)</c>，不要重设 FallbackPolicy。
    /// </param>
    public static IServiceCollection AddDefaultDenyAuthorization(
        this IServiceCollection services, Action<AuthorizationOptions>? configure = null)
    {
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAssertion(static ctx =>
                    IsBlazorFrameworkAsset(ctx) || ctx.User.Identity?.IsAuthenticated == true)
                .Build();

            configure?.Invoke(options);
        });

        services.AddSingleton<IAuthorizationHandler, AppRoleAuthorizationHandler>();

        return services;
    }

    /// <summary>
    /// Blazor 框架资源的路径前缀。该前缀下的请求**不受默认拒绝策略约束**。
    /// </summary>
    /// <remarks>
    /// 【为什么必须无条件放行】这个前缀下是 WASM 程序集、<c>blazor.web.js</c> 与资源清单，
    /// 浏览器**在任何人登录之前**就要下载它们——登录页自己就是一个 WASM 页面。
    /// 拦住它们等于让应用永远走不到登录那一步，不存在「先登录再下载」的可能。
    ///
    /// 【⚠️ 光给 <c>MapStaticAssets()</c> 加 <c>.AllowAnonymous()</c> 不够】
    /// Blazor 会为 WASM 资源清单**单独创建**一组
    /// <c>_framework/resource-collection*.js</c> 端点，它们既不归 <c>MapStaticAssets</c> 管，
    /// 也不在 <c>MapRazorComponents</c> 的约定范围内。实测枚举 <c>EndpointDataSource</c>：
    /// 1318 个端点里**恰好只有那 6 个**（指纹化/非指纹化 × 普通/gz）没有匿名标记。
    ///
    /// 【⚠️ 被拦下的症状完全指不到授权】浏览器拿到 302 后的登录页 HTML，
    /// 用它算 SHA-256，与 import map 里声明的 integrity 对不上，于是只报
    /// <c>Failed to find a valid digest in the 'integrity' attribute ... The resource has been blocked</c>。
    /// 而如果连 <c>MapStaticAssets</c> 也没放行，症状是每个 css/js 都收到一份 HTML，
    /// 控制台只有一句 <c>Unexpected token '&lt;'</c>。两种都查不到根因。
    ///
    /// 【安全性】这里没有放松任何东西：该前缀下只有编译产出的框架资源与客户端程序集，
    /// 它们本来就必须匿名可下载。业务数据不经此路径。
    /// </remarks>
    public const string BlazorFrameworkPath = "/_framework";

    private static bool IsBlazorFrameworkAsset(AuthorizationHandlerContext context) =>
        context.Resource is HttpContext http
        && http.Request.Path.StartsWithSegments(BlazorFrameworkPath);

    /// <summary>
    /// 构造一个「持有其中任意角色即可」的策略，供 <c>[Authorize(Policy = ...)]</c> 使用。
    /// </summary>
    public static AuthorizationPolicy RequireAnyRole(params string[] roles) =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new AppRoleRequirement(roles))
            .Build();
}
