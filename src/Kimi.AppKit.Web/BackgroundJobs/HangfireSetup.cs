using Microsoft.Extensions.DependencyInjection;
using Hangfire;
using Hangfire.Console;
using Kimi.AppKit.Data.Providers;

namespace Kimi.AppKit.Web.BackgroundJobs;

/// <summary>Hangfire 装配：按 <see cref="DatabaseProvider"/> 选存储。</summary>
/// <remarks>
/// 【它修的问题】前身的 <c>AddHirefire()</c> 硬编码 <c>UseSqlServerStorage(sqlString)</c>——
/// 一个声称支持双 provider 的应用，只要切到 PostgreSQL，Hangfire 这一块就直接连不上。
/// EF Core 侧的 provider 切换生效了，Hangfire 侧完全没跟上，而这个割裂只有在
/// 部署到 PG 环境、且用到后台任务时才会暴露。
///
/// 【⚠️ PostgreSQL 存储需要额外的 NuGet 包】本方法本身不引用 <c>Hangfire.PostgreSql</c>——
/// 那样会把 PostgreSQL 客户端驱动强加给所有消费方，即便他们只用 SQL Server。
/// 消费方需要 PG 支持时自己引用该包，并通过 <see cref="AddAppKitHangfire"/> 的
/// <c>configurePostgres</c> 参数调用它提供的扩展方法。
/// </remarks>
public static class HangfireSetup
{
    /// <summary>
    /// 注册 Hangfire 并按 provider 选存储。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="provider">当前使用的数据库 provider。</param>
    /// <param name="connectionString">连接串。</param>
    /// <param name="configurePostgres">
    /// PostgreSQL 存储的接线方式，通常是 <c>c => c.UsePostgreSqlStorage(cs)</c>
    /// （来自 <c>Hangfire.PostgreSql</c> 包）。<paramref name="provider"/> 为 SqlServer 时不会被调用。
    /// </param>
    public static IServiceCollection AddAppKitHangfire(
        this IServiceCollection services,
        DatabaseProvider provider,
        string connectionString,
        Action<IGlobalConfiguration> configurePostgres)
    {
        services.AddHangfire((serviceProvider, configuration) =>
        {
            configuration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseConsole();

            if (provider == DatabaseProvider.SqlServer)
            {
                configuration.UseSqlServerStorage(connectionString);
            }
            else
            {
                configurePostgres(configuration);
            }
        });

        services.AddHangfireServer();
        return services;
    }
}
