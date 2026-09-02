namespace Kimi.AppKit.Core.Time;

/// <summary>
/// 应用的**展示时区**。回答的是「把一个时刻显示给用户时，该按哪个时区渲染」。
/// </summary>
/// <remarks>
/// 【与 TimeProvider 的分工】<see cref="System.TimeProvider"/> 回答「现在几点」，
/// 本接口回答「按哪个时区显示」。两者都要，不能互相替代。
///
/// 【为什么必须有这么个东西】前身模板把「工厂在成都，UTC+8」写死在了 **6 个互不复用的地方**，
/// 写法还不一致（有的 IANA <c>Asia/Shanghai</c>、有的 Windows <c>China Standard Time</c>，
/// 后者在 Linux 容器上直接抛）。其中最要命的一处不只用于展示——它参与**日期选择器写库前的换算**，
/// 于是非中国时区的用户在界面上选的日期，存进数据库的 UTC 时刻是错的，**全程不报错**。
///
/// 【⚠️ 落库一律 UTC】本接口只管展示层的换算。写库的值必须是 UTC
/// （Npgsql 的 <c>timestamptz</c> 只接受 <c>Offset == 0</c>，传本地偏移直接抛
/// <c>ArgumentException: only offset 0 (UTC) is supported</c>；而 SQL Server 的
/// <c>datetimeoffset</c> 照单全收并保留偏移——**同一段代码在 SQL Server 上跑得好好的，
/// 换到 PostgreSQL 就在写入时崩**，且通常要等生产环境第一次收到带本地偏移的输入才暴露）。
/// </remarks>
public interface IAppTimeZone
{
    /// <summary>展示时区。</summary>
    TimeZoneInfo Zone { get; }

    /// <summary>把任意时刻换算到展示时区。</summary>
    DateTimeOffset ToLocal(DateTimeOffset instant);

    /// <summary>
    /// 把用户在界面上输入的「本地墙上时间」解释为一个确定的时刻。
    /// </summary>
    /// <remarks>
    /// ⚠️ 夏令时切换会让某些墙上时间**不存在**（春季跳过的那一小时）或**出现两次**（秋季重复的那一小时）。
    /// 不存在时按该时区切换后的偏移解释；重复时取**标准时**（较晚的那次），与 .NET 的
    /// <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/> 行为一致。
    /// 中国大陆自 1991 年起不用夏令时，但这个包不假设部署在中国。
    /// </remarks>
    DateTimeOffset FromLocal(DateTime wallClock);
}
