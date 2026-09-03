using Kimi.AppKit.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kimi.AppKit.Data.Settings;

/// <summary>设置服务的注册入口。</summary>
public static class KSettingsServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <see cref="ISettingService"/> 与 <see cref="SettingDefaults"/>。
    /// </summary>
    /// <typeparam name="TContext">应用的 <see cref="DbContext"/>。</typeparam>
    /// <typeparam name="TEntity">消费方自己声明的设置实体。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="registerDefaults">
    /// 登记默认值。**在启动期一次性调用**——<see cref="SettingDefaults"/> 内部是普通字典，
    /// 不是线程安全的。
    /// </param>
    /// <remarks>
    /// ⚠️ <see cref="SettingDefaults"/> 是 singleton、<see cref="ISettingService"/> 是 scoped：
    /// 前者只在启动期写、之后全是读；后者持有 <see cref="DbContext"/>，必须跟着请求走。
    /// 把服务错写成 singleton 会捕获一个 scoped 的 DbContext，
    /// 表现是并发请求上抛「A second operation was started on this context instance」。
    /// </remarks>
    public static IServiceCollection AddAppKitSettings<TContext, TEntity>(
        this IServiceCollection services,
        Action<SettingDefaults>? registerDefaults = null)
        where TContext : DbContext
        where TEntity : class, IKSettingEntity, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        var defaults = new SettingDefaults();
        registerDefaults?.Invoke(defaults);

        services.TryAddSingleton(defaults);
        services.TryAddScoped<ISettingService, KSettingService<TContext, TEntity>>();

        return services;
    }
}
