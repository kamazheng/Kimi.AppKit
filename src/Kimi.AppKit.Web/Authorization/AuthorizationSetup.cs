using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;

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
    /// </remarks>
    public static IServiceCollection AddDefaultDenyAuthorization(
        this IServiceCollection services, Action<AuthorizationOptions>? configure = null)
    {
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            configure?.Invoke(options);
        });

        services.AddSingleton<IAuthorizationHandler, AppRoleAuthorizationHandler>();

        return services;
    }

    /// <summary>
    /// 构造一个「持有其中任意角色即可」的策略，供 <c>[Authorize(Policy = ...)]</c> 使用。
    /// </summary>
    public static AuthorizationPolicy RequireAnyRole(params string[] roles) =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new AppRoleRequirement(roles))
            .Build();
}
