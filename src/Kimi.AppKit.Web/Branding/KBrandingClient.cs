using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Web.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Kimi.AppKit.Web.Branding;

/// <summary>
/// 从身份服务拉取企业标识（企业名与 Logo）。
/// </summary>
/// <remarks>
/// 【链路】顶栏 / 登录页 → 本类 → <c>GET {Issuer}/branding</c>
///
/// 【为什么向 Auth 要】一套部署里客户只该设一次企业名与 Logo。各服务各配一份的话，
/// 改名要改好几处，漏一处就出现两个名字并存。
///
/// ⚠️ **拉不到必须优雅回退。** 品牌只是装饰，身份服务抖一下不能让每个页面白屏。
/// 任何异常都吞掉并退回兜底值，且**只记 debug 日志**——记 warning 会在 Auth 重启期间刷屏。
///
/// ⚠️ **走服务端 HttpClient，不是浏览器 fetch**，因此不涉及 CORS，
/// 也不会把身份服务的地址暴露成一个浏览器可直接打的跨域端点。
///
/// ⚠️ 缓存用 <c>IMemoryCache</c>，这是进程内状态。多副本部署下各副本的缓存
/// 会在几分钟内不同步——对品牌这种纯展示数据可以接受，
/// 但**不要照抄这个模式去缓存有业务语义的东西**（见铁律「禁止新增进程内共享可变状态」）。
/// </remarks>
public sealed class KBrandingClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<KOidcOptions> oidcOptions,
    IMemoryCache cache,
    ILogger<KBrandingClient> logger)
{
    /// <summary>具名 HttpClient。</summary>
    public const string HttpClientName = "AppKitBranding";

    private const string CacheKey = "appkit:branding";

    /// <summary>
    /// 缓存时长。品牌改动是低频操作，管理员多等几分钟看到同步是可接受的；
    /// 而跨进程调用比查本地库贵得多。
    /// </summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 拉取失败后的退避时长。
    /// </summary>
    /// <remarks>
    /// ⚠️ 没有它的话，身份服务挂掉期间**每一次页面渲染**都会去撞一次并等满超时，
    /// 于是「Auth 挂了」会放大成「所有页面都很慢」。
    /// </remarks>
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);

    /// <summary>取企业标识。永远不抛，拿不到就回兜底值。</summary>
    public async Task<KBranding> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out KBranding? cached) && cached is not null) return cached;

        var issuer = oidcOptions.CurrentValue.Issuer;
        if (string.IsNullOrWhiteSpace(issuer)) return KBranding.Fallback;

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);

            // ⚠️ 超时必须短。它落在**页面渲染路径**上——默认的 100 秒会让
            //    身份服务不可达时每个页面都卡到近两分钟。
            client.Timeout = TimeSpan.FromSeconds(3);

            var url = new Uri(new Uri(issuer.TrimEnd('/') + "/"), "branding");
            var payload = await client.GetFromJsonAsync<BrandingPayload>(url, cancellationToken)
                .ConfigureAwait(false);

            var branding = payload is null
                ? KBranding.Fallback
                : new KBranding(
                    string.IsNullOrWhiteSpace(payload.CompanyName)
                        ? KBranding.Fallback.CompanyName
                        : payload.CompanyName,
                    ToAbsolute(issuer, payload.LogoUrl));

            cache.Set(CacheKey, branding, CacheLifetime);
            return branding;
        }
        catch (Exception ex)
        {
            // ⚠️ debug 而非 warning：Auth 每次重启都会走到这里，记高等级会刷屏，
            //    而刷屏的日志等于没有日志。
            logger.LogDebug(ex, "拉取企业标识失败，本次使用兜底值。");

            cache.Set(CacheKey, KBranding.Fallback, FailureBackoff);
            return KBranding.Fallback;
        }
    }

    /// <remarks>
    /// ⚠️ Logo 地址必须转成**绝对**地址。身份服务返回的是它自己站内的相对路径
    /// （如 <c>/branding/logo?v=…</c>），原样用在本站上会去请求**本站**的同名路径，
    /// 结果是 404 —— 表现为「Logo 配了却不显示」，而两边日志都正常。
    /// </remarks>
    private static string? ToAbsolute(string issuer, string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return null;

        return Uri.TryCreate(logoUrl, UriKind.Absolute, out var absolute)
            ? absolute.ToString()
            : new Uri(new Uri(issuer.TrimEnd('/') + "/"), logoUrl.TrimStart('/')).ToString();
    }

    private sealed record BrandingPayload(string? CompanyName, string? LogoUrl);
}

/// <summary>企业标识的注册入口。</summary>
public static class KBrandingServiceCollectionExtensions
{
    /// <summary>注册 <see cref="KBrandingClient"/> 与它的具名 HttpClient。</summary>
    public static IServiceCollection AddAppKitBranding(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(KBrandingClient.HttpClientName);
        services.AddMemoryCache();
        services.TryAddScoped<KBrandingClient>();

        return services;
    }
}
