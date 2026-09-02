using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Kimi.AppKit.Data.Auditing;

/// <summary>
/// 带审计轨迹与软删除的 <see cref="DbContext"/> 基类。
/// </summary>
/// <remarks>
/// 【与前身实现的三处关键差异】
///
/// **1. 两阶段保存并入同一个事务。**
/// 实体带自增主键时，审计记录必须等数据库回填主键之后才能写，因此有第二次 <c>SaveChanges</c>。
/// 前身实现让这两次各自独立提交：调用方不包事务时（而框架约定的普通用法就是不包），
/// 业务数据落库成功而审计行插入失败 —— 得到一个**静默的审计缺口**。
/// 现在由本类自己开事务把两阶段裹住，调用方无需知道这件事。
///
/// **2. 当前用户经 <see cref="IKCurrentUser"/> 注入，而不是每次调用都手传。**
/// 前身要求 <c>SaveChangesAsync(userName)</c>，无参重载直接抛 <c>NotSupportedException</c> ——
/// 一个只在**运行期**才暴露的约束，编译器帮不上忙，新人必然踩一次。
/// 现在无参重载正常工作。
///
/// **3. 取用户名失败就抛，不退化成 "System"。**
/// 前身是 <c>try { ... } catch { byUser = null; }</c>。审计的全部价值在「谁干的」，
/// 把异常伪装成一次正常的系统操作，等于让审计表在最需要它的时候说谎。
/// </remarks>
public abstract class AuditableDbContext : DbContext
{
    private readonly IKCurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    /// <summary>构造。</summary>
    protected AuditableDbContext(
        DbContextOptions options,
        IKCurrentUser currentUser,
        TimeProvider timeProvider)
        : base(options)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <summary>审计轨迹。</summary>
    public DbSet<Trail> AuditTrails => Set<Trail>();

    /// <summary>
    /// 是否记录审计。派生类可覆盖以整体关闭（例如一次性的数据导入上下文）。
    /// </summary>
    protected virtual bool AuditingEnabled => true;

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // ⚠️ 全局强制，不给逐属性配的机会。
        // Npgsql 的 timestamptz 只接受 Offset == 0，漏配一个属性就会在 PG 上写入时抛，
        // 且只有在收到非 UTC 输入时才触发 —— 本地开发和集成测试通常都用 UtcNow，测不出来。
        builder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();

        base.ConfigureConventions(builder);
    }

    /// <inheritdoc />
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        // ⚠️ 取不到用户名就让它抛。见类注释第 3 条。
        var user = await _currentUser.GetUserNameAsync(cancellationToken).ConfigureAwait(false);

        ChangeTracker.DetectChanges();
        ApplySoftDelete(user, now);

        if (!AuditingEnabled)
        {
            return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var pending = CollectTrails();

        // 没有需要等主键回填的条目 → 一次保存就够，不必开事务。
        if (pending.TrueForAll(p => !p.HasTemporaryProperties))
        {
            AppendTrails(pending, user, now);
            return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await SaveInOneTransactionAsync(pending, user, now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 两阶段保存，裹在同一个事务里。
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>BeginTransactionAsync</c> **必须**放在 <c>CreateExecutionStrategy().ExecuteAsync</c> 内部。
    /// 一旦 provider 开了 <c>EnableRetryOnFailure</c>（生产上很常见），
    /// 裸开事务会直接抛 <c>InvalidOperationException: The configured execution strategy
    /// does not support user-initiated transactions</c>。
    /// 这个坑的特点是：开发和测试环境通常不开重试，所以**只在生产上炸**。
    /// </remarks>
    private async Task<int> SaveInOneTransactionAsync(
        List<PendingTrail> pending, string user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // 已在外层事务里时不要再开一个：嵌套事务在多数 provider 上不被支持。
            var owned = Database.CurrentTransaction is null;
            var tx = owned
                ? await Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            try
            {
                var affected = await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                // 主键已由数据库回填，补齐后写审计。
                foreach (var trail in pending.Where(p => p.HasTemporaryProperties))
                {
                    foreach (var prop in trail.TemporaryProperties)
                    {
                        var target = prop.Metadata.IsPrimaryKey() ? trail.KeyValues : trail.NewValues;
                        target[prop.Metadata.Name] = prop.CurrentValue;
                    }
                }

                AppendTrails(pending, user, now);
                await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                if (tx is not null) await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return affected;
            }
            finally
            {
                if (tx is not null) await tx.DisposeAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    private void AppendTrails(List<PendingTrail> pending, string user, DateTimeOffset now)
    {
        foreach (var trail in pending)
        {
            AuditTrails.Add(trail.ToTrail(user, now));
        }
    }

    /// <summary>
    /// 把 <c>Deleted</c> 状态改写成软删除。
    /// </summary>
    /// <remarks>
    /// 这是「删除一律走 <c>Remove()</c>、禁止手写 <c>Active = false</c>」这条约定的实现处。
    /// 手写 <c>Active = false</c> 会绕过这里，于是审计里记成一次普通更新而不是删除。
    /// </remarks>
    private void ApplySoftDelete(string user, DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<ISoftDeleteEntity>())
        {
            if (entry.State != EntityState.Deleted) continue;

            entry.State = EntityState.Modified;
            entry.Entity.Active = false;
        }

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
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

    private List<PendingTrail> CollectTrails()
    {
        var result = new List<PendingTrail>();

        foreach (var entry in ChangeTracker.Entries())
        {
            // Trail 自己的变更不记审计，否则无限递归。
            if (entry.Entity is Trail) continue;
            if (entry.State is EntityState.Detached or EntityState.Unchanged) continue;

            var trail = new PendingTrail(entry)
            {
                TableName = entry.Metadata.GetTableName(),
                Type = entry.State switch
                {
                    EntityState.Added => TrailType.Create,
                    EntityState.Deleted => TrailType.Delete,
                    _ => TrailType.Update
                }
            };

            foreach (var property in entry.Properties)
            {
                if (property.IsTemporary)
                {
                    trail.TemporaryProperties.Add(property);
                    continue;
                }

                var name = property.Metadata.Name;

                if (property.Metadata.IsPrimaryKey())
                {
                    trail.KeyValues[name] = property.CurrentValue;
                    continue;
                }

                switch (entry.State)
                {
                    case EntityState.Added:
                        trail.NewValues[name] = property.CurrentValue;
                        break;

                    case EntityState.Deleted:
                        trail.OldValues[name] = property.OriginalValue;
                        break;

                    case EntityState.Modified when property.IsModified:
                        trail.AffectedColumns.Add(name);
                        trail.OldValues[name] = property.OriginalValue;
                        trail.NewValues[name] = property.CurrentValue;
                        break;
                }
            }

            // 软删除被改写成了 Modified，这里还原成 Delete 语义。
            if (entry.Entity is ISoftDeleteEntity { Active: false } && trail.Type == TrailType.Update)
            {
                trail.Type = TrailType.Delete;
            }

            if (trail.KeyValues.Count > 0 || trail.NewValues.Count > 0
                || trail.OldValues.Count > 0 || trail.HasTemporaryProperties)
            {
                result.Add(trail);
            }
        }

        return result;
    }
}
