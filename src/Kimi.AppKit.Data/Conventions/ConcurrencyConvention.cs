using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kimi.AppKit.Data.Conventions;

/// <summary>
/// 给全部实体挂上影子并发令牌（乐观并发）。
/// </summary>
/// <remarks>
/// 【⚠️ 机制配对了不等于业务代码会用对】
/// 本约定只负责让并发令牌**存在并参与保存时的 WHERE 子句**。
/// 它不能防止业务代码绕过它——比如查出实体后只用同名 CLR 属性覆盖（不碰这个影子属性），
/// 那样并发检查照样通过，只是通过了一个没有意义的检查。
/// 真正的防线在数据访问层：<see cref="Kimi.AppKit.Data.EfCrudDataSource{TContext,TEntity}"/>
/// 的 <c>UpsertAsync</c> 用 Attach + Modified 让传入实体携带的令牌值参与比对。
///
/// 【为什么两个 provider 用不同的列名和类型】
/// SQL Server 有原生 <c>rowversion</c>（自动递增的字节数组）；PostgreSQL 没有等价类型，
/// 借用它本来就存在、每次更新都会变的系统列 <c>xmin</c>。两者语义相同（每次更新后旧值必然不同），
/// 实现手段不同。
/// </remarks>
public static class ConcurrencyConvention
{
    /// <summary>为模型中所有具体实体挂上并发令牌。</summary>
    public static ModelBuilder ApplyConcurrencyTokens(this ModelBuilder modelBuilder, DatabaseFacade database)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned() || entityType.FindPrimaryKey() is null) continue;

            var entity = modelBuilder.Entity(entityType.ClrType);

            // ⚠️ 实体显式实现 IConcurrencyStamped 时用它的**真实属性**，不要再挂影子属性。
            //    影子属性序列化不出来，经 HTTP 往返的编辑必然失败——客户端拿不到令牌、
            //    回传的是默认值、WHERE 子句匹配不到行，于是报「已被他人修改」而实际无冲突。
            //    令牌值由 AuditingBehavior 在保存时换新，两个 provider 行为一致。
            if (typeof(IConcurrencyStamped).IsAssignableFrom(entityType.ClrType))
            {
                entity.Property(nameof(IConcurrencyStamped.ConcurrencyStamp))
                    .HasMaxLength(36)
                    .IsConcurrencyToken();
                continue;
            }

            if (database.IsSqlServer())
            {
                entity.Property<byte[]>("RowVersion")
                    .IsRowVersion()
                    .IsConcurrencyToken();
            }
            else if (database.IsPostgres())
            {
                // xmin 是 PostgreSQL 的系统列，每次 UPDATE 都会变，天然适合当并发令牌。
                // ⚠️ 迁移里不要真的去 "创建" 这一列——它一直存在，只是告诉 EF 去读它。
                entity.Property<uint>("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsRowVersion()
                    .IsConcurrencyToken();
            }
            else
            {
                // 测试用 SQLite：手动维护的版本列，行为上等价，见 EfCrudDataSourceTests。
                entity.Property<Guid>("RowVersion")
                    .IsConcurrencyToken();
            }
        }

        return modelBuilder;
    }
}
