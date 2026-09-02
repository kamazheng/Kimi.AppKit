using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Kimi.AppKit.Data.Conventions;

/// <summary>
/// 落库前把 <see cref="DateTimeOffset"/> 统一归一到 UTC 偏移。
/// </summary>
/// <remarks>
/// 【为什么必须有，且必须是全局强制】
/// 这是 PostgreSQL 与 SQL Server 之间**最凶**的一处分歧：
/// Npgsql 对 <c>timestamptz</c> 只接受 <c>Offset == 0</c>，传 <c>+08:00</c> 直接抛
/// <c>ArgumentException: only offset 0 (UTC) is supported</c>；
/// 而 SQL Server 的 <c>datetimeoffset</c> 照单全收并保留偏移。
///
/// 也就是说，**同一段代码在 SQL Server 上跑得好好的，换到 PostgreSQL 就在写入时崩**，
/// 且通常要等生产环境第一次收到带本地偏移的输入才暴露——本地开发和集成测试里
/// 的时间往往都是 <c>UtcNow</c>，根本触发不了。
///
/// 【为什么归一而不是拒绝】时间语义上只关心瞬时值，偏移是表示形式。
/// 统一成 UTC 后两个 provider 行为完全一致，调用方传什么偏移都不再有影响。
///
/// 【代价】SQL Server 上不再保留原始偏移。展示层用 <c>IAppTimeZone</c> 换算，
/// 这个代价换来的是「代码在两个 provider 上行为相同」，划算。
///
/// 【⚠️ 必须挂在 ConfigureConventions 上，不要逐属性配】
/// 逐属性配的话，任何人新增一个 <see cref="DateTimeOffset"/> 属性忘了配，
/// 这个坑就重新出现一次，而且只在 PG 上、只在收到非 UTC 输入时才炸。
/// </remarks>
public sealed class UtcDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset, DateTimeOffset>(
        toDatabase => toDatabase.ToUniversalTime(),
        fromDatabase => fromDatabase.ToUniversalTime());
