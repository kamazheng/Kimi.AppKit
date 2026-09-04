using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Web.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace KMoldApp.Infrastructure;

/// <summary>把 IdP 换来的令牌落成本站 Cookie 会话。</summary>
/// <remarks>
/// 【为什么单独一份】密码登录与扫码登录拿到令牌之后做的事完全一样。
/// 各写一遍的话，哪天改了 claim 口径或令牌存放方式却只改一处，
/// 结果是**其中一条登录通道的用户悄悄失去角色**——两条通道都能登进来，
/// 只是从其中一条进来的人到处 403。
/// </remarks>
internal static class CookieSignIn
{
    /// <summary>按令牌签发 Cookie 会话。</summary>
    public static Task SignInAsync(HttpContext http, KTokenResponse token)
    {
        var properties = new AuthenticationProperties { IsPersistent = true };

        // 令牌存进认证票据，供后续调下游 API 时取用。
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "id_token", Value = token.IdToken! },
            new AuthenticationToken { Name = "access_token", Value = token.AccessToken ?? string.Empty },
        ]);

        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, BuildPrincipal(token.IdToken!), properties);
    }

    /// <remarks>
    /// ⚠️ 角色 claim 用短名 <c>"role"</c>。IdP 侧配了 <c>MapInboundClaims = false</c>，
    /// 令牌里就是短名；这里若按 <see cref="ClaimTypes.Role"/> 建身份，
    /// 授权策略一个角色都匹配不到——**令牌里明明有角色，页面却一路 403**。
    /// ⚠️ AuthenticationType 用包里的常量而不是 Cookie 方案名：这条身份是从哪条通道来的
    /// 会一路带进审计，事后追查靠它。
    /// </remarks>
    private static ClaimsPrincipal BuildPrincipal(string idToken)
    {
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(idToken);
        var claims = jwt.Claims.Select(c => new Claim(c.Type, c.Value)).ToList();

        var identity = new ClaimsIdentity(
            claims,
            authenticationType: KAuthenticationSchemes.PasswordLogin,
            nameType: JwtRegisteredClaimNames.Name,
            roleType: "role");

        return new ClaimsPrincipal(identity);
    }
}
