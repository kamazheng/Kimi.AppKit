using Kimi.AppKit.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kimi.AppKit.Data.Auditing;

/// <summary>
/// 从变更追踪器里采集待写的审计条目。
/// </summary>
internal static class TrailCollector
{
    /// <summary>
    /// 扫描变更追踪器，为每个新增/修改/删除的实体生成一条待写审计。
    /// </summary>
    /// <remarks>
    /// ⚠️ 必须在 <c>SaveChanges</c> **之前**调用：保存之后追踪器里的
    /// <c>OriginalValue</c> 已被刷新成当前值，「改之前是什么」就取不到了。
    /// </remarks>
    public static List<PendingTrail> Collect(DbContext context)
    {
        var result = new List<PendingTrail>();

        foreach (var entry in context.ChangeTracker.Entries())
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
