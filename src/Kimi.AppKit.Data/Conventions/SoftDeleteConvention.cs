using System.Linq.Expressions;
using Kimi.AppKit.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kimi.AppKit.Data.Conventions;

/// <summary>
/// 为所有实现 <see cref="ISoftDeleteEntity"/> 的实体自动挂上「只查有效行」的全局查询过滤器。
/// </summary>
/// <remarks>
/// 【它修的问题】<see cref="Auditing.AuditableDbContext"/> 只处理了软删除的**写**端
/// （把 <c>Remove()</c> 改写成 <c>Active = false</c>）。读端如果没有查询过滤器，
/// 软删除的行会照常出现在每一次查询里——数据看着「删了」，列表里却还在。
/// 靠开发者为每个实体记得手写 <c>HasQueryFilter(e =&gt; e.Active)</c> 是不可靠的：
/// 漏掉一个实体不会报错，只会让那张表的软删除**静默失效**。
///
/// 【⚠️ 用具名过滤器，不用匿名的】EF 的匿名 <c>HasQueryFilter</c> 是**覆盖**而非叠加：
/// 消费方为同一个实体再调一次（比如加一条「只看自己部门的数据」），
/// 我们这条软删除过滤器就被**静默顶掉**，软删除的行随即全部重新可见。
/// 具名过滤器各自独立、按 AND 组合，消费方加自己的过滤器不会影响这一条。
/// 消费方确实想去掉软删除过滤时，用 <see cref="FilterKey"/> 显式操作。
/// </remarks>
public static class SoftDeleteConvention
{
    /// <summary>软删除过滤器的名字。消费方需要显式禁用/覆盖这条过滤器时用它。</summary>
    public const string FilterKey = "AppKit:SoftDelete";

    /// <summary>
    /// 为模型中所有软删除实体挂上查询过滤器。
    /// </summary>
    /// <remarks>
    /// ⚠️ 必须在所有实体都已被模型发现之后调用（<c>OnModelCreating</c> 的末尾），
    /// 否则后注册的实体拿不到过滤器。
    /// </remarks>
    public static ModelBuilder ApplySoftDeleteFilter(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeleteEntity).IsAssignableFrom(entityType.ClrType)) continue;

            // 继承层次里只有根类型能挂过滤器，派生类型会继承它；对派生类型调用直接抛。
            if (entityType.BaseType is not null) continue;

            // 拥有类型（owned）不能有自己的查询过滤器，它跟随宿主。
            if (entityType.IsOwned()) continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var activeProperty = Expression.Property(parameter, nameof(ISoftDeleteEntity.Active));
            var lambda = Expression.Lambda(activeProperty, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(FilterKey, lambda);
        }

        return modelBuilder;
    }
}
