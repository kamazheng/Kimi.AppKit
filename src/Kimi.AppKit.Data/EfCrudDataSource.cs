using System.Linq.Expressions;
using System.Reflection;
using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kimi.AppKit.Data;

/// <summary>
/// <see cref="ICrudDataSource{T}"/> 的 EF Core 实现。给 Blazor Server / 静态 SSR 宿主用——
/// 它们能直连数据库，不必为自己的页面造一套 REST API 再自己调自己。
/// </summary>
/// <typeparam name="TContext">数据上下文类型。</typeparam>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <remarks>
/// 【⚠️ 用 <see cref="IDbContextFactory{TContext}"/> 而不是直接注入 <typeparamref name="TContext"/>】
/// Blazor 会**并发渲染同一棵树里的组件**，布局与页面会抢同一个 scoped <c>DbContext</c>，
/// 抛 <c>A second operation was started on this context instance</c> 并让整页 500。
/// 这个坑只在特定组合下触发（例如布局里恰好也读了数据），裸访问首页测不出来。
///
/// 【⚠️ 授权不在这一层】本类不做任何权限判断。它是「直连数据库」的实现，
/// 调用它的组件/页面必须自己保证鉴权。走 HTTP 的那个实现更要小心——
/// 前身把「读任意表」压缩成一个端点，结果读端点忘了加 <c>[Authorize]</c> 而写端点加了，
/// 变成任何人可匿名读全库。
/// </remarks>
public class EfCrudDataSource<TContext, TEntity>(IDbContextFactory<TContext> contextFactory)
    : ICrudDataSource<TEntity>
    where TContext : DbContext
    where TEntity : class
{
    /// <summary>数据上下文工厂。</summary>
    protected IDbContextFactory<TContext> ContextFactory { get; } = contextFactory;

    /// <summary>
    /// 供派生类插入额外的查询条件（多租户过滤、只看自己的数据等）。
    /// 默认不加任何限制。
    /// </summary>
    protected virtual IQueryable<TEntity> Filter(IQueryable<TEntity> source, KQuery query) => source;

    /// <inheritdoc />
    public virtual async Task<KPage<TEntity>> LoadAsync(
        KQuery query, CancellationToken cancellationToken = default)
    {
        await using var db = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // 只读查询一律 AsNoTracking：列表页把结果放进变更追踪器毫无意义，
        // 大结果集下的追踪开销与内存占用都很可观。
        var source = Filter(db.Set<TEntity>().AsNoTracking(), query);

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0) return KPage<TEntity>.Empty(query);

        source = ApplySort(source, query);

        var items = await source
            .Skip(query.Skip)
            .Take(query.PageSize)   // KQuery 已在契约层钳制过上限，这里不必再判
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new KPage<TEntity>(items, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetAsync(object id, CancellationToken cancellationToken = default)
    {
        await using var db = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<TEntity>().FindAsync([id], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 【⚠️ 并发令牌必须真的参与比对】
    /// 前身实现是 <c>db.Entry(existing).CurrentValues.SetValues(input)</c>：
    /// 先按主键查出实体，再用传入对象覆盖同名 CLR 属性。
    /// 问题在于并发令牌通常配成**影子属性**（<c>RowVersion</c> / <c>xmin</c>），
    /// DTO 上根本没有这个属性，<c>SetValues</c> 覆盖不到它，
    /// 于是 <c>OriginalValue</c> 永远是「刚查出来的那一刻」的值 —— 并发检查必然通过。
    /// 结果是典型的 lost update：两个用户先后编辑，后者**永远无感覆盖**前者，
    /// 而模型里明明配了并发令牌，看代码只会觉得「已经防住了」。
    ///
    /// 本实现把传入实体直接 <c>Attach</c> 并标记为 <c>Modified</c>，
    /// 让调用方携带的并发令牌成为 <c>OriginalValue</c>，真正参与 <c>WHERE</c> 子句。
    /// </remarks>
    public virtual async Task<KResult> UpsertAsync(
        TEntity item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await using var db = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var isNew = IsTransient(db, item);
        if (isNew) db.Set<TEntity>().Add(item);
        else db.Set<TEntity>().Update(item);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return KResult.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            // 这是**预期内**的业务结果，不是意外。返回可读的提示而不是让异常冒到全局处理器——
            // 后者会给用户一个「服务器内部错误」，而实际上他只需要刷新后重试。
            return KResult.Fail("这条记录已被其他人修改，请刷新后重试。");
        }
    }

    /// <inheritdoc />
    public virtual async Task<KResult> DeleteAsync(object id, CancellationToken cancellationToken = default)
    {
        await using var db = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var entity = await db.Set<TEntity>().FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (entity is null) return KResult.Fail("记录不存在，可能已被删除。");

        // ⚠️ 一律走 Remove()。审计上下文会把 Deleted 状态改写成软删除；
        // 手写 Active = false 会绕过那层拦截，审计里只剩一次「普通更新」。
        db.Set<TEntity>().Remove(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return KResult.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            return KResult.Fail("这条记录已被其他人修改，请刷新后重试。");
        }
    }

    private static IQueryable<TEntity> ApplySort(IQueryable<TEntity> source, KQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.SortBy)) return source;

        // ⚠️ 排序字段名来自客户端，必须按**已知属性白名单**校验后再用。
        // 直接把字符串拼进 SQL 或交给动态 LINQ 解析，就是一个注入面。
        var property = typeof(TEntity).GetProperty(
            query.SortBy,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null) return source;   // 未知字段静默忽略，不要因此让整个查询失败

        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var selector = Expression.Lambda(Expression.Property(parameter, property), parameter);

        var method = query.SortDescending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy);
        var call = Expression.Call(
            typeof(Queryable), method, [typeof(TEntity), property.PropertyType],
            source.Expression, Expression.Quote(selector));

        return source.Provider.CreateQuery<TEntity>(call);
    }

    private static bool IsTransient(TContext db, TEntity item)
    {
        var key = db.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey();
        if (key is null) return true;

        return key.Properties.All(p =>
        {
            var value = p.PropertyInfo?.GetValue(item);
            return value is null
                || (value is int i && i == 0)
                || (value is long l && l == 0)
                || (value is Guid g && g == Guid.Empty);
        });
    }
}
