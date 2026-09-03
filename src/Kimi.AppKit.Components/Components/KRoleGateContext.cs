using System.Security.Claims;

namespace Kimi.AppKit.Components.Components;

/// <summary>
/// <see cref="KRoleGate"/> 的 <c>NotAuthorized</c> 插槽拿到的上下文。
/// </summary>
/// <param name="User">当前用户（可能是匿名的 <see cref="ClaimsPrincipal"/>）。</param>
/// <param name="MissingRolesHint">"需要 XX 角色"这类现成的提示文案。</param>
public sealed record KRoleGateContext(ClaimsPrincipal User, string MissingRolesHint);
