using Kimi.AppKit.Web;
using Kimi.AppKit.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Xunit;

namespace Kimi.AppKit.Tests.Web;

/// <summary>授权与 CORS 的默认行为——架构评审 R3 与安全隐患 #1。</summary>
public class AuthorizationDefaultDenyTests
{
    [Fact]
    public async Task 未显式授权的端点默认要求登录而不是裸奔()
    {
        // 前身没有设置 FallbackPolicy（默认 null = 允许匿名），
        // 通用查询端点的读操作因此忘了加 [Authorize] 而变成匿名可读全库。
        // ⚠️ 断言的是**行为**，不是 policy 里挂了哪个 requirement 类型——
        //    后者是实现细节，换一种等价写法就会误报失败。
        Assert.False(await EvaluateFallbackAsync(Anonymous, "/api/data"));
        Assert.True(await EvaluateFallbackAsync(Authenticated, "/api/data"));
    }

    [Theory]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_framework/resource-collection.js")]
    [InlineData("/_framework/MyApp.Client.wasm")]
    public async Task Blazor框架资源对匿名放行(string path)
    {
        // ⚠️ 浏览器在任何人登录之前就要下载这些——登录页自己就是一个 WASM 页面，
        //    拦住它们等于让应用永远走不到登录那一步。
        //    而且光给 MapStaticAssets() 加 AllowAnonymous 覆盖不到 resource-collection：
        //    那组端点由 Blazor 单独创建，不在它的约定范围内。
        Assert.True(await EvaluateFallbackAsync(Anonymous, path));
    }

    [Fact]
    public async Task 前缀相同但不是框架路径的不放行()
    {
        // StartsWithSegments 按**路径段**比较，"/_frameworkX" 不该被当成 "/_framework"。
        Assert.False(await EvaluateFallbackAsync(Anonymous, "/_frameworkX/secret"));
    }

    private static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());

    private static ClaimsPrincipal Authenticated =>
        new(new ClaimsIdentity(authenticationType: "Test"));

    private static async Task<bool> EvaluateFallbackAsync(ClaimsPrincipal user, string path)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDefaultDenyAuthorization();
        var provider = services.BuildServiceProvider();

        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy;
        Assert.NotNull(policy);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;

        var result = await provider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(user, httpContext, policy!);

        return result.Succeeded;
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
