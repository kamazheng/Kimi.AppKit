using Kimi.AppKit.Core.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace Kimi.AppKit.Components.Auth;

/// <summary>
/// WASM 端认证态提供者：读取服务端持久化过来的 <see cref="KUserInfo"/>。
/// </summary>
/// <remarks>
/// 【⚠️ 身份在 WASM 生命周期内是固定的】它只在启动时读一次。
/// 用户登出或令牌失效，客户端**不会自动感知**——那些流程都要走整页导航
/// （<c>NavigationManager.NavigateTo(..., forceLoad: true)</c>），
/// 让服务端重新决定身份。这不是缺陷，是 Blazor Web App 的既定模型：
/// 认证是服务端的事，WASM 只是拿到一份快照。
///
/// 【⚠️ 不要在这里做权限判断的兜底】页面级授权由 <c>AuthorizeView</c> 与
/// <c>[Authorize]</c> 负责，且**服务端必须独立再校验一次**——客户端的角色 claim
/// 来自可被篡改的页面数据，只能用于「显示/隐藏菜单」，绝不能作为访问控制的依据。
/// </remarks>
public sealed class KPersistentAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly Task<AuthenticationState> Anonymous =
        Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

    private readonly Task<AuthenticationState> _authenticationStateTask;

    public KPersistentAuthenticationStateProvider(PersistentComponentState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _authenticationStateTask =
            state.TryTakeFromJson<KUserInfo>(nameof(KUserInfo), out var userInfo) && userInfo is not null
                ? Task.FromResult(new AuthenticationState(userInfo.ToClaimsPrincipal()))
                : Anonymous;
    }

    /// <inheritdoc />
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _authenticationStateTask;
}
