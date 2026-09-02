using Kimi.AppKit.Core.Naming;
using Xunit;

namespace Kimi.AppKit.Tests.Core;

/// <summary>
/// 编号构建器的行为契约。
///
/// 【为什么每条都要固定时间】前身实现的每个日期方法都有 <c>date ?? DateTime.Now</c> 兜底，
/// 于是它的测试要么无法写，要么在跨年/跨月的午夜附近偶发失败。现在时间必须显式传入。
/// </summary>
public class NameBuilderTests
{
    // 2026-03-15 是周日，属于 ISO 第 11 周；Q1。
    private static readonly DateTimeOffset Sample = new(2026, 3, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void 空构建器返回空串而不是抛异常()
    {
        Assert.Equal(string.Empty, NameBuilder.Create().Build());
    }

    [Fact]
    public void 链式拼接按调用顺序并用分隔符连接()
    {
        var name = NameBuilder.Create()
            .WithSeparator("-")
            .Text("WO")
            .Year(Sample)
            .Quarter(Sample)
            .Build();

        Assert.Equal("WO-2026-Q1", name);
    }

    [Fact]
    public void 空片段被跳过不会产生连续分隔符()
    {
        // 否则会拼出 "WO--2026" 这种，且因为单号是唯一索引的一部分，
        // 格式漂移会让「同一个业务日」出现两种前缀。
        var name = NameBuilder.Create()
            .WithSeparator("-")
            .Text("WO")
            .Optional(null)
            .Optional("")
            .Year(Sample)
            .Build();

        Assert.Equal("WO-2026", name);
    }

    [Theory]
    [InlineData("2026")]
    public void Year_取四位年份(string expected) =>
        Assert.Equal(expected, NameBuilder.Create().Year(Sample).Build());

    [Fact]
    public void YearShort_取两位年份() =>
        Assert.Equal("26", NameBuilder.Create().YearShort(Sample).Build());

    [Fact]
    public void YearMonth_月份补零() =>
        Assert.Equal("202603", NameBuilder.Create().YearMonth(Sample).Build());

    [Fact]
    public void Date_年月日各自补零() =>
        Assert.Equal("20260315", NameBuilder.Create().Date(Sample).Build());

    [Fact]
    public void Week_用_ISO_周号而非区域性相关的周号()
    {
        // Calendar.GetWeekOfYear 的结果取决于当前区域性的「一周从周几开始」，
        // 跨区域部署会给出不同的周号 —— 单号因此会在不同服务器上不一致。
        Assert.Equal("W11", NameBuilder.Create().Week(Sample).Build());
    }

    [Theory]
    [InlineData(1, "Q1")]
    [InlineData(3, "Q1")]
    [InlineData(4, "Q2")]
    [InlineData(9, "Q3")]
    [InlineData(12, "Q4")]
    public void Quarter_按月份分档(int month, string expected)
    {
        var date = new DateTimeOffset(2026, month, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, NameBuilder.Create().Quarter(date).Build());
    }

    [Fact]
    public void When_条件为假时不追加()
    {
        Assert.Equal("WO", NameBuilder.Create().Text("WO").When(false, "X").Build());
        Assert.Equal("WOX", NameBuilder.Create().Text("WO").When(true, "X").Build());
    }

    [Fact]
    public void From_按给定时区换算而不是服务器本地时区()
    {
        // 这条守的是一个每天只有几小时窗口的真实缺陷：容器默认跑 UTC，
        // 用 DateTime.Now 会让「今天的单号」在业务日的头几个小时里落到前一天。
        var utcMidnightPlus2 = new DateTimeOffset(2026, 3, 15, 2, 0, 0, TimeSpan.Zero);
        var provider = new FixedTimeProvider(utcMidnightPlus2);
        var shanghai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

        var local = NameBuilder.From(provider, shanghai);

        Assert.Equal(15, local.Day);        // 上海已是 10:00，仍是 15 日
        Assert.Equal(10, local.Hour);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
