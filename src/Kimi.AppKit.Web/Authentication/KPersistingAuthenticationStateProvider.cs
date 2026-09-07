using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Kimi.AppKit.Core.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;

namespace Kimi.AppKit.Web.Authentication;

/// <summary>
/// 服务端认证态提供者：把已认证身份经 <see cref="PersistentComponentState"/> 送给 WASM 端。
/// </summary>
/// <remarks>
/// 【为什么需要它】WASM 是另一个进程，拿不到服务端的 <c>HttpContext</c>。
/// 没有它的话客户端首屏永远是匿名，要么闪一下登录态、要么直接被弹回登录页。
/// </remarks>
public sealed class KPersistingAuthenticationStateProvider
    : AuthenticationStateProvider, IHostEnvironmentAuthenticationStateProvider, IDisposable
{
    private readonly PersistentComponentState _state;
    private readonly PersistingComponentStateSubscription _subscription;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<KPersistingAuthenticationStateProvider> _logger;
    private Task<AuthenticationState>? _authenticationStateTask;

    public KPersistingAuthenticationStateProvider(
        PersistentComponentState state,
        IHttpContextAccessor httpContextAccessor,
        ILogger<KPersistingAuthenticationStateProvider> logger)
    {
        _state = state;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _subscription = state.RegisterOnPersisting(OnPersistingAsync, RenderMode.InteractiveWebAssembly);
    }

    /// <inheritdoc />
    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        _authenticationStateTask
        ?? throw new InvalidOperationException(
            $"不要在 Razor 组件的 DI 作用域之外调用 {nameof(GetAuthenticationStateAsync)}。");

    /// <inheritdoc />
    public void SetAuthenticationState(Task<AuthenticationState> authenticationStateTask) =>
        _authenticationStateTask = authenticationStateTask;

    /// <remarks>
    /// ⚠️ **整个方法必须吞掉异常。** 这是 <see cref="PersistentComponentState"/> 的持久化
    /// 回调，抛出去的后果是客户端认证态整个丢失——用户明明登录成功却被弹回登录页，
    /// 且界面上没有任何错误提示。用 <c>LogError</c> 而非 <c>LogWarning</c>：
    /// 这是「登录后进不去」的真故障，必须可诊断，不能当噪音。
    /// </remarks>
    private async Task OnPersistingAsync()
    {
        try
        {
            var authenticationState = await GetAuthenticationStateAsync().ConfigureAwait(false);
            var principal = authenticationState.User;
            var httpContext = _httpContextAccessor.HttpContext;

            // ⚠️ 默认认证方案是 OIDC，而 OIDC 是 remote handler——普通 GET 请求
            //    不读 Cookie，HttpContext.User 是空的。显式认证一次 Cookie 方案兜底，
            //    才能拿到密码登录/OIDC 回调写进去的身份与令牌。
            AuthenticationProperties? cookieProperties = null;
            if (httpContext is not null)
            {
                var cookie = await httpContext
                    .AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)
                    .ConfigureAwait(false);

                if (cookie.Succeeded && cookie.Principal is not null)
                {
                    cookieProperties = cookie.Properties;
                    if (principal.Identity?.IsAuthenticated != true)
                        principal = cookie.Principal;
                }
            }

            if (principal.Identity?.IsAuthenticated != true) return;

            // ⚠️ 只送 id token，**不送 refresh token**——后者是长期凭据，
            //    进了浏览器就等于把「长期冒充该用户」的能力交给任何一个 XSS。
            //    令牌续期由服务端的 ConfigureCookieOidcRefresh 在 Cookie 校验时完成。
            var idToken = cookieProperties?.GetTokenValue("id_token")
                          ?? (httpContext is null
                              ? null
                              : await httpContext.GetTokenAsync("id_token").ConfigureAwait(false))
                          ?? string.Empty;

            _state.PersistAsJson(nameof(KUserInfo), KUserInfo.FromClaimsPrincipal(principal, idToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "持久化用户认证态失败，客户端将退化为匿名并被弹回登录页。");
        }
    }

    /// <inheritdoc />
    public void Dispose() => _subscription.Dispose();
}
