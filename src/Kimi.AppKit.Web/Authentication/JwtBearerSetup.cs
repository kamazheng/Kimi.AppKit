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
    /// <param name="requireHttpsMetadata">
    /// 元数据端点是否必须走 HTTPS。默认 <c>true</c>。
    /// ⚠️ 只应在本地开发对接跑在 http 上的自建 IdP 时关闭，生产环境不要改。
    /// </param>
    /// <param name="roleClaimType">
    /// 角色声明的 claim 名。默认 <c>"role"</c>，需与 IdP 签发令牌时使用的名字一致——
    /// 不同 IdP 约定不同（Okta/Auth0 等常用别的命名空间前缀），配错的后果是**静默**的：
    /// 令牌里明明有角色，<c>User.IsInRole(...)</c> 却一路返回 false。
    /// </param>
    /// <param name="clockSkew">
    /// 时钟偏移容忍度。默认 <see cref="TimeSpan.Zero"/>（不放宽）。
    /// 多副本部署且宿主机时钟没对齐 NTP 时，可能需要放宽到几十秒。
    /// </param>
    /// <param name="configureValidation">
    /// 逃生舱：在应用上述默认值之后，对 <see cref="TokenValidationParameters"/> 做进一步调整。
    /// 用于本方法未覆盖到的场景，不要为了改一个字段就绕过本方法重新写一遍装配。
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
    ///
    /// 【⚠️ 首版实现曾把 <paramref name="requireHttpsMetadata"/>/<paramref name="roleClaimType"/>/
    /// <paramref name="clockSkew"/> 写死在方法体里，只是把「无条件关证书校验」换成了另一种
    /// 「无条件用这三个值」——同一类问题换了个位置。这三项在不同部署（本地开发、对接非
    /// <c>role</c> 命名约定的 IdP、多副本时钟未对齐）下确实需要不同的值，因此改成
    /// 带安全默认值的可选参数，而不是继续硬编码。
    /// </remarks>
    public static void ConfigureAppKitJwtBearer(
        this JwtBearerOptions options,
        string authority,
        string audience,
        bool requireHttpsMetadata = true,
        string roleClaimType = "role",
        TimeSpan? clockSkew = null,
        Action<TokenValidationParameters>? configureValidation = null)
    {
        options.RequireHttpsMetadata = requireHttpsMetadata;
        options.SaveToken = true;

        // ⚠️ MapInboundClaims 必须为 false：JwtBearer 默认把 role/sub/email 改写成
        // 长长的 WS-* URI，导致 RoleClaimType 与直接读 claim 的代码全部失配，
        // 且这类失配是静默的——令牌里明明有角色，接口却一路 403。这一条不开放为参数：
        // 它不是"不同部署需要不同值"的配置项，是让 RoleClaimType 参数本身生效的前提。
        options.MapInboundClaims = false;

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidIssuer = authority,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = clockSkew ?? TimeSpan.Zero,
            RoleClaimType = roleClaimType,
        };

        configureValidation?.Invoke(validationParameters);
        options.TokenValidationParameters = validationParameters;

        options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority.TrimEnd('/')}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());
    }
}
