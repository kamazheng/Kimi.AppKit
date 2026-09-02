using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Kimi.AppKit.Web.Authentication;

/// <summary>JWT Bearer 认证装配。</summary>
public static class JwtBearerSetup
{
    /// <summary>
    /// 配置 JWT 校验，签名密钥经 OIDC 元数据端点自动发现与缓存。
    /// </summary>
    /// <param name="options">要配置的选项。</param>
    /// <param name="authority">
    /// IdP 的 issuer 地址（形如 <c>https://auth.example.com</c>）。
    /// 用它拼出 <c>{authority}/.well-known/openid-configuration</c> 去发现 JWKS 端点，
    /// 不需要单独配置 JWKS URL。
    /// </param>
    /// <param name="audience">
    /// 本 API 的受众标识（对应 IdP 侧的 resource/scope 配置）。
    /// </param>
    /// <remarks>
    /// 【与前身实现的核心差异——这是架构评审 R1，认证根基级别的修复】
    ///
    /// 前身的 <c>IssuerSigningKeyResolver</c> 每次验证 JWT 都：
    /// <list type="number">
    /// <item>用 <c>HttpClientHandler.DangerousAcceptAnyServerCertificateValidator</c>
    ///       **无条件关闭证书校验**去请求 JWKS——验证令牌签名的这条通道本身可被 MITM
    ///       篡改返回的公钥集，攻击者能伪造任意签名并被接受为合法。</item>
    /// <item><c>.GetAwaiter().GetResult()</c> **同步阻塞**发起 HTTP 请求，
    ///       高并发下会占满线程池，且**没有任何缓存**——每验证一个 token 就打一次 JWKS 端点。</item>
    /// </list>
    ///
    /// 现在改用 <see cref="ConfigurationManager{OpenIdConnectConfiguration}"/>——.NET 身份平台
    /// 自带的组件，专门为这个场景设计：证书校验走正常的 TLS 验证流程；内部带缓存与刷新退避
    /// （默认缓存 24 小时，遇到未知 kid 时才提前刷新），不会每次验证都打网络请求。
    ///
    /// 【顺带修复 Y1：ValidateAudience 恢复为 true】
    /// 前身关闭了受众校验，意味着同一 IdP 上任意其它注册客户端签发的令牌，只要签发者和
    /// 签名对得上就会被这个 API 接受——典型的 confused deputy。本方法要求消费方显式给出
    /// <paramref name="audience"/>，不提供默认放行的重载。
    /// </remarks>
    public static void ConfigureAppKitJwtBearer(this JwtBearerOptions options, string authority, string audience)
    {
        options.RequireHttpsMetadata = true;
        options.SaveToken = true;

        // ⚠️ MapInboundClaims 必须为 false：JwtBearer 默认把 role/sub/email 改写成
        // 长长的 WS-* URI，导致 RoleClaimType="role" 与直接读 claim 的代码全部失配，
        // 且这类失配是静默的——令牌里明明有角色，接口却一路 403。
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidIssuer = authority,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = "role",
        };

        options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority.TrimEnd('/')}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());
    }
}
