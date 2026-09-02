namespace Kimi.AppKit.Core.Entities;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// 带唯一名称的可审计实体。数据层会自动为派生类建立 <c>Name</c> 的条件唯一索引。
/// </summary>
/// <remarks>
/// 【⚠️ 唯一索引在两个 provider 上的行为不一致】
/// 索引条件是「未软删除的行里 Name 唯一」。PostgreSQL 与 SQL Server 对唯一索引中的 NULL
/// 语义**相反**：SQL Server 视多个 NULL 为相同（只允许一行），PostgreSQL 视为不同（允许多行）。
/// 所以 <see cref="Name"/> 必须 <c>NOT NULL</c>，用空串而不是 null 表达「没填」。
///
/// 【⚠️ 大小写语义也不一致】PostgreSQL 区分大小写、SQL Server 默认不区分，
/// 同一句 <c>Where(x =&gt; x.Name == input)</c> 在两边给出不同结果，**编译和单测全过**。
/// 需要大小写不敏感的精确匹配时，比较**规范化后的列**，不要依赖数据库排序规则。
///
/// 派生类若确实不需要这条唯一约束，实现 <see cref="ISkipNameUnique"/> 豁免。
/// </remarks>
public abstract class AuditableEntityWithName : BaseAuditableEntity
{
    /// <summary>名称。参与唯一索引，因此不可为 null（没填用空串）。</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>说明。</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }
}

/// <summary>
/// 标记：豁免 <see cref="AuditableEntityWithName"/> 的名称唯一约束。
/// 用于「同名记录合法」的场景（如按版本存多份同名配置）。
/// </summary>
public interface ISkipNameUnique;
