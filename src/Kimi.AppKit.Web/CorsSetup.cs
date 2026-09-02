using Microsoft.Extensions.DependencyInjection;

namespace Kimi.AppKit.Web;

/// <summary>CORS 装配。</summary>
public static class CorsSetup
{
    /// <summary>策略名，供 <c>app.UseCors(CorsSetup.PolicyName)</c> 使用。</summary>
    public const string PolicyName = "AppKitCors";

    /// <summary>
    /// 按 <paramref name="allowedOrigins"/> 注册 CORS 策略。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="allowedOrigins">
    /// 允许的来源列表。为空时本策略不放行任何跨域请求
    /// （<c>WithOrigins()</c> 传空数组等价于拒绝一切）。
    /// </param>
    /// <param name="allowedMethods">
    /// 允许的 HTTP 方法。默认 <c>null</c>，等价于放行任意方法——多数 API 场景下
    /// 限制来源已经是主要防线，方法通常不需要单独收紧，但需要更严格控制时可以传入。
    /// </param>
    /// <param name="allowedHeaders">允许的请求头。默认 <c>null</c>，等价于放行任意头。</param>
    /// <remarks>
    /// 【⚠️ 未配置时默认拒绝，不是放行】
    /// 前身实现是 <c>AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()</c>，且没有任何配置开关——
    /// 任意来源均可跨域调用 API，这不是环境相关的疏漏，是产品级的安全缺口。
    /// 这里的默认值只放宽了"来源"以外的两个维度（方法/头），而**来源永远需要显式给出**——
    /// 两者的风险量级不对等：放开方法/头顶多是多余的权限，放开来源是任意网站都能发起请求。
    /// </remarks>
    public static IServiceCollection AddAppCors(
        this IServiceCollection services,
        IReadOnlyList<string> allowedOrigins,
        IReadOnlyList<string>? allowedMethods = null,
        IReadOnlyList<string>? allowedHeaders = null)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                if (allowedOrigins.Count == 0)
                {
                    // 显式什么都不允许，而不是省略这段导致策略默认放行。
                    policy.WithOrigins([]);
                    return;
                }

                policy.WithOrigins([.. allowedOrigins]);

                if (allowedMethods is { Count: > 0 }) policy.WithMethods([.. allowedMethods]);
                else policy.AllowAnyMethod();

                if (allowedHeaders is { Count: > 0 }) policy.WithHeaders([.. allowedHeaders]);
                else policy.AllowAnyHeader();
            });
        });

        return services;
    }
}
