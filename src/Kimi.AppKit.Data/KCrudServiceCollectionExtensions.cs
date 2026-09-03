using Kimi.AppKit.Core.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kimi.AppKit.Data;

/// <summary>
/// 登记「哪些实体开放 CRUD」。登记过的实体即可解析出 <see cref="ICrudDataSource{T}"/>，
/// 供 <c>KEntityCrudPage&lt;T&gt;</c>（直连宿主）与 <c>MapCrudEndpoints&lt;T&gt;</c>（HTTP 宿主）共用同一份数据源。
/// </summary>
/// <remarks>
/// 【⚠️ 登记是白名单，不是便利设施】
/// 没登记的实体解析不出数据源，端点也就映射不出来。这是刻意的：
/// 前身把「读写任意表」压缩成一个端点，于是**没有任何一处代码需要说明「哪些表可以被外部访问」**——
/// 权限判断因此无处可挂。改成显式登记后，「开放了什么」是一份可以逐行审的清单。
///
/// 【⚠️ 登记 ≠ 授权】本方法只决定「这个实体有没有数据源」，不决定「谁能访问」。
/// 授权挂在端点上（见 <c>MapCrudEndpoints</c> 的 <c>RequireReadAuthorization</c> /
/// <c>RequireWriteAuthorization</c>），或由组件所在页面自己保证。
/// </remarks>
public static class KCrudServiceCollectionExtensions
{
    /// <summary>
    /// 开始登记基于 <typeparamref name="TContext"/> 的 CRUD 数据源。
    /// </summary>
    /// <remarks>
    /// ⚠️ 需要 <see cref="IDbContextFactory{TContext}"/> 已注册。
    /// 用 <c>AddDbContextFactory</c> 而不是 <c>AddDbContext</c>——自 EF Core 5 起
    /// 前者**同时**把上下文注册成 scoped 服务，既有的构造注入一处都不用改，
    /// 而数据源刻意只接受工厂：Blazor 会并发渲染同一棵树里的组件，
    /// 抢同一个 scoped <c>DbContext</c> 会抛
    /// <c>A second operation was started on this context instance</c>。
    /// </remarks>
    public static KCrudBuilder<TContext> AddKCrud<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        return new KCrudBuilder<TContext>(services);
    }
}

/// <summary>
/// <see cref="KCrudServiceCollectionExtensions.AddKCrud{TContext}"/> 的链式登记器。
/// </summary>
public sealed class KCrudBuilder<TContext>(IServiceCollection services)
    where TContext : DbContext
{
    /// <summary>底层的服务集合，供需要额外注册的调用方使用。</summary>
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// 登记一个实体，用默认的 <see cref="ReflectiveCrudDataSource{TContext,TEntity}"/>。
    /// </summary>
    /// <remarks>
    /// ⚠️ 用 <c>TryAdd</c> 语义：**先注册的胜出**。想换成自己的实现，
    /// 在调用本方法**之前**注册 <c>ICrudDataSource&lt;T&gt;</c> 即可，不必绕开登记流程
    /// （绕开会让端点映射同时失效，那是两件事）。
    /// </remarks>
    public KCrudBuilder<TContext> AddEntity<TEntity>() where TEntity : class
    {
        Services.TryAddScoped<ICrudDataSource<TEntity>>(sp =>
            new ReflectiveCrudDataSource<TContext, TEntity>(
                sp.GetRequiredService<IDbContextFactory<TContext>>()));

        Services.AddSingleton(new KCrudEntityRegistration(typeof(TEntity)));
        return this;
    }

    /// <summary>
    /// 登记一个实体并指定自定义数据源实现（例如需要覆写 <c>Filter</c> 做行级过滤）。
    /// </summary>
    public KCrudBuilder<TContext> AddEntity<TEntity, TDataSource>()
        where TEntity : class
        where TDataSource : class, ICrudDataSource<TEntity>
    {
        Services.TryAddScoped<ICrudDataSource<TEntity>, TDataSource>();
        Services.AddSingleton(new KCrudEntityRegistration(typeof(TEntity)));
        return this;
    }
}

/// <summary>
/// 一条实体登记记录。存在的意义是让「开放了哪些实体」可被运行期枚举——
/// 通用浏览页据此列出可维护的表，而不是靠各页面各自再抄一份清单。
/// </summary>
/// <param name="EntityType">被登记的实体 CLR 类型。</param>
public sealed record KCrudEntityRegistration(Type EntityType);
