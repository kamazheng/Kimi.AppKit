using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Kimi.AppKit.Data.Auditing;

/// <summary>
/// 保存过程中暂存的一条审计。它在 <c>SaveChanges</c> **之前**从变更追踪器里采集，
/// 在保存**之后**补齐由数据库生成的值（自增主键），最后落成 <see cref="Trail"/>。
/// </summary>
/// <remarks>
/// 【为什么必须分两步】自增主键在 <c>SaveChanges</c> 之前是临时值（负数），
/// 那时写进审计就等于记了一个不存在的行号。所以必须等数据库回填之后再补。
/// <see cref="HasTemporaryProperties"/> 标记出「这条审计还欠着值」。
/// </remarks>
internal sealed class PendingTrail(EntityEntry entry)
{
    /// <summary>变更追踪器里的条目。</summary>
    public EntityEntry Entry { get; } = entry;

    /// <summary>主键值。</summary>
    public Dictionary<string, object?> KeyValues { get; } = [];

    /// <summary>变更前的值。</summary>
    public Dictionary<string, object?> OldValues { get; } = [];

    /// <summary>变更后的值。</summary>
    public Dictionary<string, object?> NewValues { get; } = [];

    /// <summary>保存后才拿得到值的属性（自增主键等）。</summary>
    public List<PropertyEntry> TemporaryProperties { get; } = [];

    /// <summary>被改动的列名。</summary>
    public List<string> AffectedColumns { get; } = [];

    /// <summary>变更类型。</summary>
    public TrailType Type { get; set; }

    /// <summary>表名。</summary>
    public string? TableName { get; set; }

    /// <summary>是否还有等待数据库回填的属性。</summary>
    public bool HasTemporaryProperties => TemporaryProperties.Count > 0;

    /// <summary>落成可持久化的审计记录。</summary>
    public Trail ToTrail(string userId, DateTimeOffset auditOn) => new()
    {
        UserId = userId,
        AuditOn = auditOn,
        Type = Type,
        TableName = TableName,
        Name = Entry.Entity.GetType().Name,
        PrimaryKey = KeyValues.Count == 0 ? null : JsonSerializer.Serialize(KeyValues),
        OldValues = OldValues.Count == 0 ? null : JsonSerializer.Serialize(OldValues),
        NewValues = NewValues.Count == 0 ? null : JsonSerializer.Serialize(NewValues),
        AffectedColumns = AffectedColumns.Count == 0 ? null : JsonSerializer.Serialize(AffectedColumns)
    };
}
