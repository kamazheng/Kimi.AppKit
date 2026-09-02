using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Kimi.AppKit.Data.Conventions;

/// <summary>
/// 给所有 <see cref="AuditableEntityWithName"/> 的派生类挂上「未软删除的行里 Name 唯一」的条件索引。
/// </summary>
/// <remarks>
/// 【⚠️ 两个 provider 的写法完全不同，但语义必须一致】
/// SQL Server 用 Filtered Index，PostgreSQL 用 Partial Index —— 都是「只对满足条件的行建索引」，
/// 但 <c>HasFilter</c> 接受的是**各自方言的原始 SQL 片段**，不经过 EF 的跨库翻译。
/// 布尔字面量、标识符引号规则两边都不同：SQL Server 写 <c>[Active] = 1</c>，
/// PostgreSQL 写 <c>"Active" = true</c>。写错的后果不是报错，而是这条过滤条件**被数据库忽略**，
/// 唯一索引退化成全表唯一——这在两个 provider 上表现完全不同且都不会在建表时报错。
/// </remarks>
public static class UniqueNameConvention
{
    /// <summary>为所有符合条件的实体挂上唯一名称约束。</summary>
    public static ModelBuilder ApplyUniqueNameConstraint(this ModelBuilder modelBuilder, DatabaseFacade database)
    {
        var targets = modelBuilder.Model.GetEntityTypes()
            .Where(et => !et.IsOwned()
                         && !et.ClrType.IsAbstract
                         && typeof(AuditableEntityWithName).IsAssignableFrom(et.ClrType)
                         && !typeof(ISkipNameUnique).IsAssignableFrom(et.ClrType));

        foreach (var entityType in targets)
        {
            ApplyTo(modelBuilder.Entity(entityType.ClrType), database);
        }

        return modelBuilder;
    }

    private static void ApplyTo(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder entity, DatabaseFacade database)
    {
        const string name = nameof(AuditableEntityWithName.Name);
        const string active = nameof(ISoftDeleteEntity.Active);

        var filter = database.IsSqlServer()
            ? $"[{name}] IS NOT NULL AND [{active}] = 1"
            : $"\"{name}\" IS NOT NULL AND \"{active}\" = true";

        entity.HasIndex(name).IsUnique().HasFilter(filter);
    }
}
