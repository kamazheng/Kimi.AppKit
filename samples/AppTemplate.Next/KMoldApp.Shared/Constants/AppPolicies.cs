namespace KMoldApp.Shared.Constants;

/// <summary>
/// 授权策略名。
/// </summary>
/// <remarks>
/// ⚠️ 策略名是字符串，写错**不会有编译错误**——<c>[Authorize(Policy = "Adminonly")]</c>
/// 会在运行期抛 <c>InvalidOperationException: The AuthorizationPolicy named ... was not found</c>，
/// 而那只在有人真的访问那个端点时才发生。所以一律用这里的常量，不要手写字面量。
/// </remarks>
public static class AppPolicies
{
    /// <summary>需要 Root 或 Admin 角色。</summary>
    public const string AdminOnly = nameof(AdminOnly);
}
