using System.Net;
using Kimi.AppKit.Core.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kimi.AppKit.Web.Authorization;

/// <summary>
/// 判断请求来源是否落在允许的网段内。
/// </summary>
/// <remarks>
/// 【为什么返回枚举而不是错误文案】前身返回的是一句中文错误串（<c>null</c> 表示放行）。
/// 那把展现层的措辞塞进了安全原语：换语言要改这里、想区分「没配置」与「不在网段」做不到，
/// 而且 <c>null == 通过</c> 这个约定读代码时极易反向理解。
/// 枚举让调用方自己决定给用户看什么、给日志记什么。
///
/// 【为什么是服务而不是 IAuthorizationRequirement】原计划写的是改成授权要求。
/// 实际不合适：唯一的消费方是**登录端点**，那里用户还没通过认证，
/// 而且失败时要返回一个特定的 HTML 页面而不是 403。
/// 授权策略给不出自定义响应体，硬套会变成「策略判完再在端点里判一遍」。
/// 等真出现「按网段保护某个已认证端点」的需求时再加要求与处理器，
/// 现在加就是本阶段刚立过的「0 使用的能力不抽」的反面。
/// </remarks>
public sealed class KNetworkGate
{
    private readonly IOptionsMonitor<KNetworkGateOptions> _options;
    private readonly ILogger<KNetworkGate> _logger;

    // ⚠️ CIDR 解析结果随配置快照缓存。前身在**每个请求、每个网段**上重新
    //    IPNetwork.TryParse 一次；那既是热路径上的重复解析，也意味着
    //    一条写错的 CIDR 永远不会被发现——TryParse 失败就静默跳过那一条。
    private readonly Lock _gate = new();
    private IReadOnlyList<IPNetwork>? _parsed;
    private IList<string>? _parsedFrom;

    public KNetworkGate(IOptionsMonitor<KNetworkGateOptions> options, ILogger<KNetworkGate> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>判定 <paramref name="context"/> 的来源是否允许。</summary>
    public KNetworkGateResult Evaluate(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = _options.CurrentValue;
        var networks = GetNetworks(options.AllowedSubnets);

        // ⚠️ 空清单 = 拒绝。这条方向是刻意的：忘了配等于更严，而不是更松。
        if (networks.Count == 0) return KNetworkGateResult.DeniedNotConfigured;

        // ⚠️ 只读已由 UseForwardedHeaders 还原过的 RemoteIpAddress。
        //    直接读 X-Forwarded-For 等于信任客户端可任意伪造的头。
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null) return KNetworkGateResult.DeniedUnknownAddress;

        // Kestrel 在双栈监听下会把 IPv4 报成 ::ffff:a.b.c.d，不还原就永远匹配不上 IPv4 网段。
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip))
            return options.AllowLoopback ? KNetworkGateResult.Allowed : KNetworkGateResult.DeniedOutOfRange;

        foreach (var network in networks)
        {
            if (network.Contains(ip)) return KNetworkGateResult.Allowed;
        }

        return KNetworkGateResult.DeniedOutOfRange;
    }

    private IReadOnlyList<IPNetwork> GetNetworks(IList<string> configured)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_parsedFrom, configured) && _parsed is not null) return _parsed;

            var parsed = new List<IPNetwork>();
            foreach (var entry in Split(configured))
            {
                if (IPNetwork.TryParse(entry, out var network))
                {
                    parsed.Add(network);
                    continue;
                }

                // 解析失败必须出声。静默跳过意味着一个打错的网段会让准入范围**变小**，
                // 表现是「明明配了却还是被拒」，而日志里什么都没有。
                _logger.LogError("网络准入白名单里的 {Entry} 不是合法的 CIDR，已忽略该条。", entry);
            }

            _parsed = parsed;
            _parsedFrom = configured;
            return parsed;
        }
    }

    /// <summary>兼容「一个元素里写逗号分隔多个网段」的配置写法。</summary>
    /// <remarks>
    /// 环境变量注入数组很别扭（<c>Auth__NetworkGate__AllowedSubnets__0</c>），
    /// 运维现场经常退化成一条逗号分隔的串。这里容忍它，比让人配错强。
    /// </remarks>
    private static IEnumerable<string> Split(IList<string> configured) =>
        configured
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
