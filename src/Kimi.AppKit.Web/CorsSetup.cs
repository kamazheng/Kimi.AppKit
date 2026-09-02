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
    /// <remarks>
    /// 【⚠️ 未配置时默认拒绝，不是放行】
    /// 前身实现是 <c>AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()</c>，且没有任何配置开关——
    /// 任意来源均可跨域调用 API，这不是环境相关的疏漏，是产品级的安全缺口。
    /// <paramref name="allowedOrigins"/> 为空时，本策略不放行任何跨域请求
    /// （<c>WithOrigins()</c> 传空数组等价于拒绝一切）。
    /// </remarks>
    public static IServiceCollection AddAppCors(this IServiceCollection services, IReadOnlyList<string> allowedOrigins)
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

                policy.WithOrigins([.. allowedOrigins])
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        return services;
    }
}
