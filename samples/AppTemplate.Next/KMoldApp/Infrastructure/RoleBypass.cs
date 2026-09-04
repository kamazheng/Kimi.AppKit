using KMoldApp.Shared.Constants;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace KMoldApp.Infrastructure;

/// <summary>
/// 开发期权限绕过的配置。
/// </summary>
/// <remarks>
/// 【它做什么】启用后，给每个已登录用户注入**全部**角色（含 <c>Root</c>），
/// 免去本地开发时先在 IdP 上配角色的麻烦。
///
/// 【⚠️ 与前身实现的三处差异，每一处都是安全或可测性问题】
///
/// 1. **默认关闭，必须显式配置开启。** 前身默认值是「非生产环境一律开启」——
///    后果不只是安全，更是**可测性**：在 Staging 上验证「普通用户看不到管理菜单」
///    时所有人都是 Root，测试通过了，上生产才发现权限压根没配对。
///
/// 2. **不依赖环境变量兜底。** 前身靠 <c>env.IsProduction()</c> 关闭绕过，
///    而 <c>ASPNETCORE_ENVIRONMENT</c> 在容器里忘了设就默认成 Development——
///    等于后门大开。现在是「显式开启 + 生产环境硬拒绝」双条件。
///
/// 3. **没有运行期 setter。** 前身是个 Singleton 且 <c>IsActive</c> 可写，
///    属于铁律 6 禁止的进程内共享可变状态：多副本部署下
///    「同一个操作在不同副本上结果不同」。现在只在启动时从配置读一次。
/// </remarks>
public sealed class RoleBypassOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Auth:RoleBypass";

    /// <summary>
    /// 是否启用。⚠️ 默认 <c>false</c>——**不要改这个默认值**，
    /// 要用就在 <c>appsettings.Development.json</c> 里显式打开。
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// 绕过启用时，给已认证用户补齐全部角色 claim。
/// </summary>
/// <remarks>
/// 【管线位置】认证 → **本组件** → 授权策略 / <c>AuthorizeView</c>
///
/// ⚠️ WASM 客户端**不经过本组件**（不同进程）。要让两端行为一致，
/// 客户端需自行查询绕过状态并注入同一套角色——两处共用
/// <see cref="AppRoles.InjectMissing"/>，避免各写一份而漂移
/// （那会造成「前端看得见的菜单后端却 403」这种最难查的不一致）。
/// </remarks>
public sealed class RoleBypassClaimsTransformation(
    RoleBypassGate gate) : IClaimsTransformation
{
    /// <inheritdoc />
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (!gate.IsActive || principal.Identity?.IsAuthenticated != true)
            return Task.FromResult(principal);

        AppRoles.InjectMissing((ClaimsIdentity)principal.Identity, "role");
        return Task.FromResult(principal);
    }
}

/// <summary>
/// 绕过是否生效的最终判定。**不可变**，启动时定死。
/// </summary>
/// <param name="enabled">配置里是否显式开启。</param>
/// <param name="isProduction">当前是否生产环境。</param>
public sealed class RoleBypassGate(bool enabled, bool isProduction)
{
    /// <summary>
    /// 是否生效。⚠️ 生产环境**恒为 false**，配置开了也不生效。
    /// </summary>
    public bool IsActive { get; } = enabled && !isProduction;
}
