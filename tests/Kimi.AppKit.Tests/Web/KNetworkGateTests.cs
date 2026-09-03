using System.Net;
using Kimi.AppKit.Web.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kimi.AppKit.Tests.Web;

/// <summary>
/// 网络准入的行为契约。
/// </summary>
/// <remarks>
/// 【为什么这组测试必须存在】这道门保护的是**明文 HTTP 上的密码登录**，
/// 而它的前身是 fail-open：白名单没配置就放行。仓库里从来没有任何 appsettings
/// 配过那一项，于是这道门在所有环境下都是全开的——代码读起来却像有防护。
/// 本组把「没配 = 拒绝」这个方向钉死，防止有人为了联调方便再翻回去。
/// </remarks>
public class KNetworkGateTests
{
    private static KNetworkGate Create(Action<KNetworkGateOptions>? configure = null)
    {
        var options = new KNetworkGateOptions();
        configure?.Invoke(options);
        return new KNetworkGate(
            new StaticMonitor(options), NullLogger<KNetworkGate>.Instance);
    }

    private static HttpContext ContextFrom(string? ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = ip is null ? null : IPAddress.Parse(ip);
        return context;
    }

    [Fact]
    public void 白名单为空时拒绝而不是放行()
    {
        // ⚠️ 这条是整组的核心。松了就等于「谁都能用明文密码登录」。
        Assert.Equal(KNetworkGateResult.DeniedNotConfigured,
            Create().Evaluate(ContextFrom("10.20.30.40")));
    }

    [Fact]
    public void 全部配成非法CIDR等同于没配置()
    {
        // 解析失败的条目会被丢弃，剩下空清单 —— 必须仍然落在「拒绝」一侧，
        // 而不是因为「用户明明配了」就放行。
        var gate = Create(o => o.AllowedSubnets.Add("这不是网段"));

        Assert.Equal(KNetworkGateResult.DeniedNotConfigured,
            gate.Evaluate(ContextFrom("10.20.30.40")));
    }

    [Theory]
    [InlineData("10.20.30.1")]
    [InlineData("10.20.30.255")]
    public void 网段内放行(string ip)
    {
        var gate = Create(o => o.AllowedSubnets.Add("10.20.30.0/24"));

        Assert.Equal(KNetworkGateResult.Allowed, gate.Evaluate(ContextFrom(ip)));
    }

    [Fact]
    public void 网段外拒绝()
    {
        var gate = Create(o => o.AllowedSubnets.Add("10.20.30.0/24"));

        Assert.Equal(KNetworkGateResult.DeniedOutOfRange, gate.Evaluate(ContextFrom("10.20.31.1")));
    }

    [Fact]
    public void IPv4映射地址被还原后仍能匹配IPv4网段()
    {
        // ⚠️ Kestrel 双栈监听时把 IPv4 报成 ::ffff:a.b.c.d。不还原的话
        //    网段永远匹配不上，表现是「配了车间网段却全被拒」，且看不出原因。
        var gate = Create(o => o.AllowedSubnets.Add("10.20.30.0/24"));

        Assert.Equal(KNetworkGateResult.Allowed, gate.Evaluate(ContextFrom("::ffff:10.20.30.5")));
    }

    [Fact]
    public void 环回默认拒绝且可显式放开()
    {
        // 前身无条件放行环回 —— 那是安全门上一个恒开的后门（本机进程、SSRF 打 localhost 都绕过）。
        var strict = Create(o => o.AllowedSubnets.Add("10.20.30.0/24"));
        Assert.Equal(KNetworkGateResult.DeniedOutOfRange, strict.Evaluate(ContextFrom("127.0.0.1")));

        var relaxed = Create(o =>
        {
            o.AllowedSubnets.Add("10.20.30.0/24");
            o.AllowLoopback = true;
        });
        Assert.Equal(KNetworkGateResult.Allowed, relaxed.Evaluate(ContextFrom("127.0.0.1")));
    }

    [Fact]
    public void 取不到来源IP时拒绝()
    {
        var gate = Create(o => o.AllowedSubnets.Add("10.20.30.0/24"));

        Assert.Equal(KNetworkGateResult.DeniedUnknownAddress, gate.Evaluate(ContextFrom(null)));
    }

    [Fact]
    public void 逗号分隔的单条目会被拆开()
    {
        // 环境变量注入数组很别扭，运维现场常退化成一条逗号分隔的串。
        var gate = Create(o => o.AllowedSubnets.Add("10.1.0.0/16, 10.2.0.0/16"));

        Assert.Equal(KNetworkGateResult.Allowed, gate.Evaluate(ContextFrom("10.2.3.4")));
    }

    [Fact]
    public void 显式配零点零可放开全部()
    {
        // 联调要放开时的正确写法：让「这里是敞开的」在配置里看得见。
        var gate = Create(o => o.AllowedSubnets.Add("0.0.0.0/0"));

        Assert.Equal(KNetworkGateResult.Allowed, gate.Evaluate(ContextFrom("203.0.113.7")));
    }

    private sealed class StaticMonitor(KNetworkGateOptions value) : IOptionsMonitor<KNetworkGateOptions>
    {
        public KNetworkGateOptions CurrentValue => value;
        public KNetworkGateOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<KNetworkGateOptions, string?> listener) => null;
    }
}
