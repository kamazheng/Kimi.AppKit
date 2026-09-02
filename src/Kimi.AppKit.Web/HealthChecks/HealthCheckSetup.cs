using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kimi.AppKit.Web.HealthChecks;

/// <summary>
/// 健康检查端点，供容器编排（k8s / docker compose）做存活与就绪探测。
/// </summary>
/// <remarks>
/// 【为什么分两个端点——这条设计被架构评审认可，原样保留】
/// <c>/health/live</c> 只回答「进程还活着吗」，**绝不能掺入数据库等外部依赖**——
/// 否则数据库一抖动，编排系统就会把本来好好的实例判定为死亡并杀掉重启，
/// 把一次外部故障放大成一场滚动重启风暴。
/// <c>/health/ready</c> 才回答「现在能接流量吗」，包含外部依赖检查；不就绪时编排系统
/// 只是把它摘出负载均衡，等依赖恢复后自动重新挂回，不会重启进程。
///
/// 【必须匿名】探针请求不带任何凭据，端点若要求鉴权会一律返回 401 → 被判定为不健康 → 无限重启。
/// </remarks>
public static class HealthCheckSetup
{
    private const string ReadyTag = "ready";

    /// <summary>
    /// 注册健康检查项，含数据库连通性检查（打 <c>ready</c> 标签）。
    /// 新增外部依赖（Redis、消息队列等）时用 <see cref="IHealthChecksBuilder"/> 追加，
    /// 并同样打 <c>ready</c> 标签才会被 <c>/health/ready</c> 纳入。
    /// </summary>
    public static IHealthChecksBuilder AddAppHealthChecks<TContext>(this IServiceCollection services)
        where TContext : DbContext =>
        services.AddHealthChecks().AddDbContextCheck<TContext>("database", tags: [ReadyTag]);

    /// <summary>映射 <c>/health/live</c> 与 <c>/health/ready</c> 两个端点。</summary>
    public static WebApplication MapAppHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,   // 不执行任何检查项，进程能响应本请求即视为存活
            ResponseWriter = WriteResponse
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponse
        }).AllowAnonymous();

        return app;
    }

    /// <remarks>
    /// ⚠️ 刻意不输出异常详情——该端点匿名可访问，异常消息可能含连接串等敏感信息。
    /// </remarks>
    private static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = e.Value.Duration.TotalMilliseconds
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
