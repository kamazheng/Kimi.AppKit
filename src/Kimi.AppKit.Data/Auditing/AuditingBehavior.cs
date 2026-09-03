using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kimi.AppKit.Data.Auditing;

/// <summary>
/// 审计轨迹与软删除的实现本体，以**组合**方式接入任意 <see cref="DbContext"/>。
/// </summary>
/// <remarks>
/// 【业务位置】数据层横切关注点。<see cref="AuditableDbContext"/> 只是它的一层便利外壳。
///
/// 【为什么是组合而不是基类】审计是横切关注点，而继承是最强的耦合。
/// 一旦做成基类，DbContext 那唯一的基类名额就被占掉了——
/// 于是 <c>IdentityDbContext</c>（ASP.NET Core Identity 必需）与审计**二选一**，
/// 且没有任何绕法。这个冲突在自己写的样例上永远不会暴露，
/// 因为样例的 DbContext 天生没有基类之争；只有接入真实服务时才撞上。
///
/// 【为什么不用 <c>ISaveChangesInterceptor</c>】那是 EF Core 为此提供的官方扩展点，
/// 但在这里会**倒退**：<c>SavingChanges</c> 发生在事务开启之前、<c>SavedChanges</c> 发生在提交之后，
/// 两个钩子之间无法共享事务。而自增主键必须等回填才能写审计，
/// 于是审计行只能落在业务数据**已经提交之后**的另一个事务里——
/// 那正是下面「两阶段并入同一事务」要修的那个静默审计缺口。
/// EF Core 官方文档自己也提示：多数情况下重写 <c>SaveChanges</c> 比拦截器更合适。
///
/// 【接入方式】持有一个实例，在自己的 <c>SaveChangesAsync</c> 里转交：
/// <code>
/// public override Task&lt;int&gt; SaveChangesAsync(CancellationToken ct = default)
///     =&gt; _auditing.SaveChangesAsync(this, base.SaveChangesAsync, ct);
/// </code>
/// ⚠️ 第二个参数必须传 <c>base.SaveChangesAsync</c> 而不是 <c>this.SaveChangesAsync</c>，
/// 否则无限递归。
///
/// 【审计**不覆盖**什么——这条边界无法消除，只能被知道】
/// 审计、软删除改写、<c>Updated</c>/<c>UpdatedBy</c> 三样能力全部挂在 <c>SaveChanges</c> 上，
/// 靠扫描变更追踪器工作。**任何绕过变更追踪器的写入，这三样一起失效，且不报错。**
///
/// <list type="table">
///   <item>
///     <term><c>ExecuteUpdate</c> / <c>ExecuteDelete</c></term>
///     <description>
///       EF Core 官方原话：它们「completely unaware of EF's change tracker,
///       and have no interaction with it whatsoever」。
///       其中 <c>ExecuteDelete</c> 最危险——它是**物理 DELETE**，
///       把软删除彻底绕过，行是真的没了。
///       （另：这两个方法各自不开事务，连调两次时前一次的结果不会因后一次失败而回滚。）
///     </description>
///   </item>
///   <item>
///     <term><c>ExecuteSqlRaw</c> 等原生 SQL</term>
///     <description>同理，完全不经过追踪器。</description>
///   </item>
///   <item>
///     <term><c>AsNoTracking</c> 查出的实体</term>
///     <description>不在追踪器里，改了既不会保存也不会审计。</description>
///   </item>
///   <item>
///     <term><c>CurrentValues.SetValues(dto)</c> 且值完全相同</term>
///     <description>
///       实体状态保持 <c>Unchanged</c>，采集时被跳过，一条审计都不产生。
///       这是正确行为，但容易被误读成「我明明调了保存，为什么没记录」。
///     </description>
///   </item>
/// </list>
///
/// 这不是可以修掉的缺陷：要审计就必须知道旧值，而批量操作的全部意义
/// 恰恰是**不把行读进内存**。两者语义上不可兼得。
/// 真正的危害不是「批量操作没有审计」，而是**它静默**——
/// 实测某个现役服务里有 59 处 <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>，
/// 每一处都在悄悄绕过去。
/// 边界的回归用例见 <c>AuditingBoundaryTests</c>。
/// </remarks>
public sealed class AuditingBehavior(IKCurrentUser currentUser, TimeProvider timeProvider)
{
    /// <summary>
    /// 采集审计、应用软删除，然后保存。
    /// </summary>
    /// <param name="context">要审计的上下文。</param>
    /// <param name="saveChanges">**基类的**保存实现，不能是调用方自己的重写。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="recordTrails">
    /// 是否写审计轨迹。置 <c>false</c> 时**仍然**应用软删除与审计字段——
    /// 关掉的只是 <see cref="Trail"/> 行，不是数据一致性。用于一次性数据导入等场景。
    /// </param>
    public async Task<int> SaveChangesAsync(
        DbContext context,
        Func<CancellationToken, Task<int>> saveChanges,
        CancellationToken cancellationToken = default,
        bool recordTrails = true)
    {
        var now = timeProvider.GetUtcNow();

        // ⚠️ 取不到用户名就让它抛。审计的全部价值在「谁干的」，
        //    退化成 "System" 等于让审计表在最需要它的时候说谎。
        var user = await currentUser.GetUserNameAsync(cancellationToken).ConfigureAwait(false);

        context.ChangeTracker.DetectChanges();
        ApplySoftDeleteAndStamps(context, user, now);

        if (!recordTrails)
        {
            return await saveChanges(cancellationToken).ConfigureAwait(false);
        }

        var pending = TrailCollector.Collect(context);

        // 没有等主键回填的条目 → 一次保存就够，不必开事务。
        if (pending.TrueForAll(p => !p.HasTemporaryProperties))
        {
            AppendTrails(context, pending, user, now);
            return await saveChanges(cancellationToken).ConfigureAwait(false);
        }

        return await SaveInOneTransactionAsync(context, saveChanges, pending, user, now, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 两阶段保存，裹在同一个事务里。
    /// </summary>
    /// <remarks>
    /// 【为什么要开事务】实体带自增主键时，审计记录必须等数据库回填主键之后才能写，
    /// 因此有第二次保存。若两次各自独立提交（而框架约定的普通用法就是调用方不包事务），
    /// 业务数据落库成功而审计行插入失败，得到一个**静默的审计缺口**。
    ///
    /// ⚠️ <c>BeginTransactionAsync</c> **必须**放在 <c>CreateExecutionStrategy().ExecuteAsync</c> 内部。
    /// 一旦 provider 开了 <c>EnableRetryOnFailure</c>（生产上很常见），
    /// 裸开事务会直接抛 <c>InvalidOperationException: The configured execution strategy
    /// does not support user-initiated transactions</c>。
    /// 这个坑的特点是：开发和测试环境通常不开重试，所以**只在生产上炸**。
    /// </remarks>
    private void AppendTrails(DbContext context, List<PendingTrail> pending, string user, DateTimeOffset now)
    {
        var trails = context.Set<Trail>();

        foreach (var trail in pending)
        {
            trails.Add(trail.ToTrail(user, now));
        }
    }

    private async Task<int> SaveInOneTransactionAsync(
        DbContext context,
        Func<CancellationToken, Task<int>> saveChanges,
        List<PendingTrail> pending,
        string user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // 已在外层事务里时不要再开一个：嵌套事务在多数 provider 上不被支持。
            var owned = context.Database.CurrentTransaction is null;
            var tx = owned
                ? await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            try
            {
                var affected = await saveChanges(cancellationToken).ConfigureAwait(false);

                // 主键已由数据库回填，补齐后写审计。
                foreach (var trail in pending.Where(p => p.HasTemporaryProperties))
                {
                    foreach (var prop in trail.TemporaryProperties)
                    {
                        var target = prop.Metadata.IsPrimaryKey() ? trail.KeyValues : trail.NewValues;
                        target[prop.Metadata.Name] = prop.CurrentValue;
                    }
                }

                AppendTrails(context, pending, user, now);
                await saveChanges(cancellationToken).ConfigureAwait(false);

                if (tx is not null) await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return affected;
            }
            finally
            {
                if (tx is not null) await tx.DisposeAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 把 <c>Deleted</c> 状态改写成软删除，并盖上审计字段。
    /// </summary>
    /// <remarks>
    /// 这是「删除一律走 <c>Remove()</c>、禁止手写 <c>Active = false</c>」这条约定的实现处。
    /// 手写 <c>Active = false</c> 会绕过这里，于是审计里记成一次普通更新而不是删除。
    /// </remarks>
    private static void ApplySoftDeleteAndStamps(DbContext context, string user, DateTimeOffset now)
    {
        foreach (var entry in context.ChangeTracker.Entries<ISoftDeleteEntity>())
        {
            if (entry.State != EntityState.Deleted) continue;

            entry.State = EntityState.Modified;
            entry.Entity.Active = false;
        }

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedOn = now;
                    entry.Entity.CreatedBy = user;
                    entry.Entity.Updated = now;
                    entry.Entity.UpdatedBy = user;
                    break;
                case EntityState.Modified:
                    entry.Entity.Updated = now;
                    entry.Entity.UpdatedBy = user;
                    break;
            }
        }
    }
}
