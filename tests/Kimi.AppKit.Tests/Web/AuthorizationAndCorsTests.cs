using Kimi.AppKit.Web;
using Kimi.AppKit.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kimi.AppKit.Tests.Web;

/// <summary>授权与 CORS 的默认行为——架构评审 R3 与安全隐患 #1。</summary>
public class AuthorizationDefaultDenyTests
{
    [Fact]
    public void 未显式授权的端点默认要求登录而不是裸奔()
    {
        // 前身没有设置 FallbackPolicy（默认 null = 允许匿名），
        // 通用查询端点的读操作因此忘了加 [Authorize] 而变成匿名可读全库。
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDefaultDenyAuthorization();

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        Assert.NotNull(options.FallbackPolicy);
        Assert.Contains(options.FallbackPolicy!.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task 持有其中任意角色即通过角色要求()
    {
        var handler = new AppRoleAuthorizationHandler();
        var requirement = new AppRoleRequirement("Admin", "Root");

        var user = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Root")],
                authenticationType: "Test"));

        var context = new AuthorizationHandlerContext([requirement], user, resource: null);

        // AuthorizationHandler<T> 的 HandleRequirementAsync 是 protected，经公开的 HandleAsync 触发。
        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task 不持有任何列出角色时不通过()
    {
        var handler = new AppRoleAuthorizationHandler();
        var requirement = new AppRoleRequirement("Admin");
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());

        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}

public class CorsDefaultDenyTests
{
    [Fact]
    public void 未配置任何来源时默认拒绝跨域()
    {
        // 前身是 AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader() 且无任何配置开关——
        // 任意来源均可跨域调用 API，是产品级的安全缺口，不是环境相关的疏漏。
        var services = new ServiceCollection();
        services.AddAppCors(allowedOrigins: []);

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<CorsOptions>>().Value;

        var policy = options.GetPolicy(CorsSetup.PolicyName);
        Assert.NotNull(policy);
        Assert.False(policy!.AllowAnyOrigin);
        Assert.Empty(policy.Origins);
    }

    [Fact]
    public void 配置了来源后只允许这些来源()
    {
        var services = new ServiceCollection();
        services.AddAppCors(["https://app.example.com"]);

        var policy = services.BuildServiceProvider()
            .GetRequiredService<IOptions<CorsOptions>>().Value
            .GetPolicy(CorsSetup.PolicyName)!;

        Assert.Contains("https://app.example.com", policy.Origins);
        Assert.False(policy.AllowAnyOrigin);
    }
}
