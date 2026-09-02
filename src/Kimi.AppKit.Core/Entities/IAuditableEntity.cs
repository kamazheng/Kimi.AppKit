namespace Kimi.AppKit.Core.Entities;

/// <summary>
/// 可审计实体。数据层在 SaveChanges 时自动填充这些字段。
/// </summary>
/// <remarks>
/// ⚠️ 时间字段一律 <see cref="DateTimeOffset"/> 而非 <c>DateTime</c>，且落库前必须归一到 UTC。
/// Npgsql 的 <c>timestamptz</c> **只接受 Offset == 0**，传 <c>+08:00</c> 直接抛
/// <c>ArgumentException: only offset 0 (UTC) is supported</c>；而 SQL Server 的
/// <c>datetimeoffset</c> 照单全收并保留偏移。也就是说同一段代码在 SQL Server 上跑得好好的，
/// 换到 PostgreSQL 就在写入时崩，且通常要等生产环境第一次收到带本地偏移的输入才暴露。
/// 数据层用全局值转换器兜住这一点。
/// </remarks>
public interface IAuditableEntity
{
    /// <summary>最后修改时间（UTC）。</summary>
    DateTimeOffset Updated { get; set; }

    /// <summary>最后修改人。</summary>
    string? UpdatedBy { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    DateTimeOffset CreatedOn { get; set; }

    /// <summary>创建人。</summary>
    string? CreatedBy { get; set; }
}
