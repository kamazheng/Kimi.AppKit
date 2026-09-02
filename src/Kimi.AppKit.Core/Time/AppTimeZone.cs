namespace Kimi.AppKit.Core.Time;

/// <summary>
/// <see cref="IAppTimeZone"/> 的默认实现。构造时只吃一个时区 ID 字符串，
/// **不认识 IConfiguration**——「从哪读这个字符串」是宿主装配的事，不是本包的事。
/// 这样 Core 才能保持零依赖，也让它在单元测试里可以直接 new。
/// </summary>
public sealed class AppTimeZone : IAppTimeZone
{
    /// <summary>默认时区 ID。</summary>
    /// <remarks>
    /// ⚠️ 默认值必须是 UTC，**不能是任何具体地区**。
    /// 前身模板把成都时区当默认值，于是漏配的部署会静默按 UTC+8 渲染所有时间——
    /// 界面上看着一切正常，只是每个时间都错 8 小时。UTC 至少是「明显不对」，会被立刻发现。
    /// </remarks>
    public const string DefaultZoneId = "UTC";

    /// <summary>
    /// 按时区 ID 构造。
    /// </summary>
    /// <param name="timeZoneId">
    /// IANA ID（如 <c>Asia/Shanghai</c>、<c>America/Chicago</c>）或 Windows ID
    /// （如 <c>China Standard Time</c>）。null / 空白按 <see cref="DefaultZoneId"/> 处理。
    /// </param>
    /// <exception cref="TimeZoneNotFoundException">ID 无法解析成任何已知时区。</exception>
    /// <remarks>
    /// ⚠️ **IANA 与 Windows 两套 ID 不通用**，且各平台只原生认识其中一套：
    /// Windows 上 <c>Asia/Shanghai</c> 曾长期不被识别，Linux/macOS 上
    /// <c>China Standard Time</c> 直接抛 <see cref="TimeZoneNotFoundException"/>。
    /// 前身模板正是踩了这个——<c>ChengduTimeProvider</c> 用 IANA、Hangfire 任务用 Windows ID，
    /// 两份代码写死了同一个时区却在 Linux 容器上只有一份能跑。
    ///
    /// .NET 6+ 在两个方向上都做了 ICU 映射，但**不保证覆盖全部 ID**，所以这里显式做一次
    /// <see cref="TimeZoneInfo.TryConvertIanaIdToWindowsId"/> / <c>TryConvertWindowsIdToIanaId</c> 兜底，
    /// 并在失败时抛出**带上下文**的异常——而不是让调用方拿到一句光秃秃的「时区未找到」。
    /// </remarks>
    public AppTimeZone(string? timeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultZoneId : timeZoneId.Trim();
        Zone = Resolve(id);
    }

    /// <summary>按已解析的时区构造。</summary>
    public AppTimeZone(TimeZoneInfo zone) => Zone = zone;

    /// <inheritdoc />
    public TimeZoneInfo Zone { get; }

    /// <inheritdoc />
    public DateTimeOffset ToLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Zone);

    /// <inheritdoc />
    public DateTimeOffset FromLocal(DateTime wallClock)
    {
        // Kind 必须是 Unspecified：Utc/Local 会让 GetUtcOffset 按别的时区解释，
        // 而调用方给的语义就是「这是展示时区里的墙上时间」。
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        var offset = Zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }

    private static TimeZoneInfo Resolve(string id)
    {
        if (TryFind(id, out var zone)) return zone;

        // 跨约定兜底：给的是 IANA 就试 Windows，反之亦然。
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId)
            && TryFind(windowsId, out zone)) return zone;

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId)
            && TryFind(ianaId, out zone)) return zone;

        throw new TimeZoneNotFoundException(
            $"无法解析时区 '{id}'。请用 IANA ID（如 Asia/Shanghai）或 Windows ID（如 China Standard Time）；" +
            $"注意两套 ID 不通用，且各平台原生只认识其中一套。当前平台：{Environment.OSVersion.Platform}。");
    }

    private static bool TryFind(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
