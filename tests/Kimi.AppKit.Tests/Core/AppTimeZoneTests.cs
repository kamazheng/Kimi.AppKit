using Kimi.AppKit.Core.Time;
using Xunit;

namespace Kimi.AppKit.Tests.Core;

/// <summary>
/// 时区服务的行为契约。
///
/// 【为什么这些用例值得写】前身模板把「UTC+8」写死在 6 个互不复用的地方，写法还不一致
/// （IANA 与 Windows 两套 ID 混用），其中一处参与写库前换算 —— 非中国时区的用户存进
/// 数据库的时刻是错的，且全程不报错。这里的每条断言都对应其中一个真实故障。
/// </summary>
public class AppTimeZoneTests
{
    [Fact]
    public void 默认时区是_UTC_而不是任何具体地区()
    {
        // 漏配时按 UTC 渲染是「明显不对、会被立刻发现」；
        // 按某个具体地区渲染则是「看着正常、只是每个时间都错几小时」。
        Assert.Equal(TimeZoneInfo.Utc, new AppTimeZone((string?)null).Zone);
        Assert.Equal(TimeZoneInfo.Utc, new AppTimeZone("").Zone);
        Assert.Equal(TimeZoneInfo.Utc, new AppTimeZone("   ").Zone);
    }

    [Theory]
    [InlineData("Asia/Shanghai")]        // IANA
    [InlineData("China Standard Time")]  // Windows
    public void 两套时区_ID_都能解析(string id)
    {
        // ⚠️ 这正是前身模板踩的坑：ChengduTimeProvider 用 IANA、Hangfire 任务用 Windows ID，
        // 两份代码写死同一个时区，却在 Linux 容器上只有一份能跑。
        var tz = new AppTimeZone(id);
        Assert.Equal(TimeSpan.FromHours(8), tz.Zone.GetUtcOffset(new DateTime(2026, 1, 1)));
    }

    [Fact]
    public void 无法解析的_ID_抛出带上下文的异常()
    {
        var ex = Assert.Throws<TimeZoneNotFoundException>(() => new AppTimeZone("Mars/Olympus"));

        // 光秃秃一句「时区未找到」会让人以为是代码 bug；要提示两套 ID 不通用这件事。
        Assert.Contains("Mars/Olympus", ex.Message);
        Assert.Contains("IANA", ex.Message);
    }

    [Fact]
    public void ToLocal_保留时刻只改变偏移()
    {
        var tz = new AppTimeZone("Asia/Shanghai");
        var utc = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        var local = tz.ToLocal(utc);

        Assert.Equal(utc, local);                                  // 同一时刻
        Assert.Equal(TimeSpan.FromHours(8), local.Offset);         // 不同偏移
        Assert.Equal(8, local.Hour);
    }

    [Fact]
    public void FromLocal_把墙上时间解释成正确的时刻()
    {
        // 这条对应最隐蔽的那个真实缺陷：用户在日期选择器里选「3 月 1 日」，
        // 前身实现无条件按 UTC+8 换算，于是非中国时区的部署存进库的是 2 月 28 日 16:00Z。
        var tz = new AppTimeZone("America/Chicago");   // UTC-6（冬令时）
        var wall = new DateTime(2026, 1, 15, 9, 0, 0);

        var instant = tz.FromLocal(wall);

        Assert.Equal(TimeSpan.FromHours(-6), instant.Offset);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 15, 0, 0, TimeSpan.Zero), instant.ToUniversalTime());
    }

    [Fact]
    public void FromLocal_在夏令时切换处不抛异常()
    {
        // 春季跳过的那一小时里，墙上时间「不存在」。实现必须给出一个确定的答案而不是崩掉——
        // 用户完全可能在日期选择器里选到这个时刻。
        var tz = new AppTimeZone("America/Chicago");
        var skipped = new DateTime(2026, 3, 8, 2, 30, 0);   // 2026 年美国夏令时切换日

        var instant = tz.FromLocal(skipped);

        Assert.NotEqual(default, instant);
    }

    [Fact]
    public void 按_TimeZoneInfo_构造时原样保留()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("UTC");
        Assert.Same(zone, new AppTimeZone(zone).Zone);
    }
}
