using Microsoft.AspNetCore.Authorization;

namespace Kimi.AppKit.Web.Authorization;

/// <summary>
/// 「必须持有列表中至少一个角色」的授权要求。
/// </summary>
/// <remarks>
/// 【与前身实现的核心差异】
/// 前身有三套并行的自定义授权特性（角色 / 权限 / claim），各自实现
/// <c>IAuthorizationFilter</c>、不继承 <c>AuthorizeAttribute</c>，各自复制一份权限绕过开关的判断。
/// 三套并行本身不是最大的问题——**真正的问题是它们完全绕开了 ASP.NET Core 的原生授权管线**，
/// 于是"整个应用默认放行、只有显式加了特性的端点才检查"，
/// 而"忘记加特性"这件事在通用查询端点上就真的发生了：
/// <c>GeneralDbController</c> 的读端点没有任何授权标注，任何人可匿名读任意表。
///
/// 原生 policy-based authorization 把这个默认值反过来：
/// <see cref="AuthorizationSetup.AddDefaultDenyAuthorization"/> 设置
/// <c>FallbackPolicy = RequireAuthenticatedUser()</c>，
/// 于是"忘记标注"的默认后果从"裸奔"变成"要求登录"——即便还是漏配了具体角色要求，
/// 至少不会匿名可读。
/// </remarks>
public sealed class AppRoleRequirement(params string[] roles) : IAuthorizationRequirement
{
    /// <summary>满足其中任意一个即通过。</summary>
    public IReadOnlyList<string> Roles { get; } = roles;
}

/// <summary>
/// <see cref="AppRoleRequirement"/> 的判定逻辑。
/// </summary>
public sealed class AppRoleAuthorizationHandler : AuthorizationHandler<AppRoleRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AppRoleRequirement requirement)
    {
        if (requirement.Roles.Any(context.User.IsInRole))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
