using Microsoft.AspNetCore.Authorization;

namespace KMoldApp.Shared.Constants;

/// <summary>
/// 授权策略名与策略定义。
/// </summary>
/// <remarks>
/// ⚠️ 策略名是字符串，写错**不会有编译错误**——运行期才抛
/// <c>The AuthorizationPolicy named ... was not found</c>，而且只在有人真的
/// 访问那个端点/页面时才发生。一律用这里的常量，不要手写字面量。
///
/// 【⚠️ 为什么策略**定义**也放在这里，而不是各端各写一份】
/// 服务端与 WASM 端都要注册同名策略：服务端管端点，客户端管
/// <c>&lt;AuthorizeView&gt;</c> 与页面上的 <c>[Authorize(Policy=...)]</c>。
/// 两处各写一遍的话，任何一次改动漏掉一边就会出现最难查的那类不一致——
/// **前端看得见的菜单，后端却 403**；或者反过来，客户端页面直接抛
/// 「策略未找到」而整页白屏（本模板在 X2-9 真踩过这个）。
/// </remarks>
public static class AppPolicies
{
    /// <summary>需要 Root 或 Admin 角色。</summary>
    public const string AdminOnly = nameof(AdminOnly);

    /// <summary>
    /// 注册全部应用策略。**服务端与客户端都要调**。
    /// </summary>
    /// <remarks>
    /// ⚠️ 用 <c>RequireRole</c>（OR 语义：持有任一即可），与服务端包里
    /// <c>AuthorizationSetup.RequireAnyRole</c> 的语义保持一致。
    /// </remarks>
    public static void AddAppPolicies(this AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddPolicy(AdminOnly, policy =>
            policy.RequireRole(AppRoles.Root, AppRoles.Admin));
    }
}
