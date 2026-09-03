using System.Globalization;

namespace Kimi.AppKit.Core.Naming;

/// <summary>
/// 单号/编号的**前缀**构建器：把前缀、日期片段、条件片段拼成一个基础名称，
/// 如 <c>WO-2026-Q1</c>。真正的流水号由数据层的唯一名称生成器追加。
/// </summary>
/// <remarks>
/// 【与前身实现的两处差异，都是刻意的】
///
/// 1. **不再默认取 <c>DateTime.Now</c>**。前身的每个日期方法都有
///    <c>date ?? DateTime.Now</c> 兜底，后果有二：单元测试无法固定时间，只能在跨年/跨月的
///    午夜附近偶发失败；而且 <c>DateTime.Now</c> 取的是**服务器本地时区**，容器默认 UTC，
///    于是「按当天日期生成单号」在北京时间早上 8 点之前会给出前一天的日期——
///    这类 bug 每天只有 8 小时窗口，极难复现。
///    现在日期必须显式传入，或用 <see cref="From(System.TimeProvider, System.TimeZoneInfo)"/> 从
///    <see cref="System.TimeProvider"/> 取。
///
/// 2. **不含任何数据库操作**。前身的 <c>BuildUniqueAsync</c> 直接吃 <c>DbContext</c>，
///    把一个纯字符串构建器绑死在 EF 上，也让本类型无法进入零依赖的 Core 包。
///    「查库找下一个可用序号」属于数据层，见 <c>Kimi.AppKit.Data</c>。
/// </remarks>
public sealed class NameBuilder
{
    private readonly List<string> _segments = [];
    private string _separator = string.Empty;

    private NameBuilder() { }

    /// <summary>新建一个构建器。</summary>
    public static NameBuilder Create() => new();

    /// <summary>
    /// 从 <see cref="System.TimeProvider"/> 取当前时刻并换算到指定时区，
    /// 供后续的日期片段使用。
    /// </summary>
    /// <remarks>
    /// ⚠️ 时区必须显式给：容器默认跑在 UTC，用服务器本地时区会让「今天的单号」
    /// 在业务日的头几个小时里落到前一天。
    /// </remarks>
    public static DateTimeOffset From(TimeProvider timeProvider, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);

    /// <summary>设置片段之间的分隔符。默认无分隔。</summary>
    public NameBuilder WithSeparator(string separator)
    {
        _separator = separator;
        return this;
    }

    /// <summary>追加一段固定文本。<c>null</c> 与空串会被忽略。</summary>
    /// <remarks>
    /// ⚠️ 前身另有一个 <c>Optional(string?)</c>，文档写着「值非空时才追加」，
    /// 实现却是 <c>=&gt; Text(text)</c> ——与本方法**完全等价**。
    /// 两个名字做同一件事，读代码的人会以为 <c>Text</c> 不忽略空值，
    /// 于是在不需要的地方到处改用 <c>Optional</c>。已删除，空值处理就在这里。
    /// </remarks>
    public NameBuilder Text(string? text)
    {
        if (!string.IsNullOrEmpty(text)) _segments.Add(text);
        return this;
    }

    /// <summary>四位年份，如 <c>2026</c>。</summary>
    public NameBuilder Year(DateTimeOffset date) => Text(date.Year.ToString(CultureInfo.InvariantCulture));

    /// <summary>两位年份，如 <c>26</c>。</summary>
    public NameBuilder YearShort(DateTimeOffset date) => Text(date.ToString("yy", CultureInfo.InvariantCulture));

    /// <summary>年月，如 <c>202603</c>。</summary>
    public NameBuilder YearMonth(DateTimeOffset date, string separator = "") =>
        Text($"{date.Year}{separator}{date.Month:D2}");

    /// <summary>年月日，如 <c>20260315</c>。</summary>
    public NameBuilder Date(DateTimeOffset date, string separator = "") =>
        Text($"{date.Year}{separator}{date.Month:D2}{separator}{date.Day:D2}");

    /// <summary>自定义日期格式。始终按 <see cref="CultureInfo.InvariantCulture"/> 格式化。</summary>
    /// <remarks>
    /// ⚠️ 不用当前区域性：单号是**机器标识**不是给人读的文本，跟着服务器区域性变会让
    /// 同一批数据出现两种格式的单号（如泰语历的年份）。
    /// </remarks>
    public NameBuilder DateFormat(string format, DateTimeOffset date) =>
        Text(date.ToString(format, CultureInfo.InvariantCulture));

    /// <summary>ISO 周序号，如 <c>W12</c>。</summary>
    /// <remarks>
    /// 用 <see cref="ISOWeek"/> 而非 <c>Calendar.GetWeekOfYear</c>：后者的结果取决于
    /// 当前区域性的「一周从周几开始」，跨区域部署会给出不同的周号。
    /// </remarks>
    public NameBuilder Week(DateTimeOffset date) => Text($"W{ISOWeek.GetWeekOfYear(date.DateTime):D2}");

    /// <summary>季度，如 <c>Q1</c>。</summary>
    public NameBuilder Quarter(DateTimeOffset date) => Text($"Q{(date.Month - 1) / 3 + 1}");

    /// <summary>条件追加。</summary>
    public NameBuilder When(bool condition, string? text) => condition ? Text(text) : this;

    /// <summary>拼接成最终字符串。</summary>
    public string Build() => string.Join(_separator, _segments);

    /// <inheritdoc />
    public override string ToString() => Build();
}
