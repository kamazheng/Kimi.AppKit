using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Kimi.AppKit.Data.Auditing;

/// <summary>
/// 一条审计轨迹：谁、在什么时候、把哪张表的哪一行的哪些字段、从什么改成了什么。
/// </summary>
/// <remarks>
/// 【表名与列名保持与前身一致】<c>AuditTrail</c> 表、各列名与顺序都沿用旧实现，
/// 让已有系统迁入时不必重建审计表、不必迁移历史数据。
///
/// 【⚠️ 一处刻意的破坏性改动：<see cref="AuditOn"/> 从 <c>DateTime</c> 改成 <see cref="DateTimeOffset"/>】
/// 旧的 <c>DateTime</c> 不带偏移信息，跨时区部署时「这条记录是几点写的」无法回答。
/// 迁入既有系统时需要一次列类型迁移。这与实体基类的时间字段改动是同一个决定，
/// 不单独为审计表破例。
///
/// 【⚠️ 刻意不建外键指向用户表】用户被删除后审计记录必须留存。
/// 加了外键就等于「想删用户必须先删他的审计」，那正好把审计最该覆盖的场景挖空了。
/// </remarks>
[Table("AuditTrail")]
[Index(nameof(TableName), nameof(AuditOn), nameof(UserId))]
public class Trail
{
    /// <summary>主键。</summary>
    [Key]
    [Column(Order = 1)]
    public int Id { get; set; }

    /// <summary>被变更实体的显示名，便于人读。</summary>
    [MaxLength(256)]
    [Column(Order = 2)]
    public string Name { get; set; } = string.Empty;

    /// <summary>补充说明。</summary>
    [MaxLength(512)]
    [Column(Order = 3)]
    public string? Description { get; set; }

    /// <summary>操作者。</summary>
    /// <remarks>⚠️ 取不到时不要写 "System" 顶上——那是把异常伪装成一次正常的系统操作。</remarks>
    [MaxLength(100)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>变更类型。以字符串存库，列上有 CHECK 约束。</summary>
    [MaxLength(50)]
    public TrailType Type { get; set; }

    /// <summary>被变更的表名。</summary>
    [MaxLength(100)]
    public string? TableName { get; set; }

    /// <summary>变更发生的时刻（UTC）。</summary>
    [Precision(3)]
    public DateTimeOffset AuditOn { get; set; }

    /// <summary>变更前的值（JSON）。</summary>
    public string? OldValues { get; set; }

    /// <summary>变更后的值（JSON）。</summary>
    public string? NewValues { get; set; }

    /// <summary>被改动的列名（JSON 数组）。</summary>
    [MaxLength(1000)]
    public string? AffectedColumns { get; set; }

    /// <summary>被变更行的主键（JSON）。</summary>
    [MaxLength(200)]
    public string? PrimaryKey { get; set; }
}
