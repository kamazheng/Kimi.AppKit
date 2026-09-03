using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Kimi.AppKit.Observability;

/// <summary>
/// OpenTelemetry 装配：链路、指标、日志三条信号一次接好。
/// </summary>
/// <remarks>
/// 【⚠️ 端点从环境变量读，不在代码里拼】
/// 导出地址走 OTel 的标准约定（<c>OTEL_EXPORTER_OTLP_ENDPOINT</c> /
/// <c>OTEL_EXPORTER_OTLP_PROTOCOL</c> / <c>OTEL_EXPORTER_OTLP_HEADERS</c>），
/// 由 <c>UseOtlpExporter()</c> 统一处理。
/// 前身对三条信号各写一次 <c>new Uri($"{base}/v1/logs")</c>——
/// 除了重复，它还假定后端一定按 <c>/v1/{signal}</c> 分路径，而 gRPC 端点并不是这样。
///
/// 【⚠️ 未配置端点时不注册，而不是抛】遥测是可降级能力。
/// 前身早期版本在缺配置时让 <c>new Uri(null)</c> 抛 <c>UriFormatException</c>，
/// 于是**启动期**就崩，而排查者在本地代码里根本看不出这个 key 该从哪来。
/// </remarks>
public static class KObservabilitySetup
{
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// 接上 OpenTelemetry。未配置 OTLP 端点时**跳过注册**并打印提示，不阻断启动。
    /// </summary>
    public static IHostApplicationBuilder AddAppKitObservability(
        this IHostApplicationBuilder builder,
        Action<KObservabilityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new KObservabilityOptions();
        configure?.Invoke(options);

        var endpoint = builder.Configuration[OtlpEndpointVariable]
                       ?? builder.Configuration["Otlp:Endpoint"];

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Console.WriteLine(
                $"[启动提示] 未配置 {OtlpEndpointVariable}（或 Otlp:Endpoint），已跳过 OpenTelemetry 注册：" +
                "本次运行不会上报链路、指标与日志。应用其余功能不受影响。");
            return builder;
        }

        var serviceName = options.ServiceName ?? builder.Environment.ApplicationName;
        var serviceVersion = options.ServiceVersion ?? ResolveAssemblyVersion();

        builder.Services.AddSingleton(options);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.AddProcessor(new KRedactingLogProcessor(options));
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(serviceName, options.ServiceNamespace, serviceVersion,
                    autoGenerateServiceInstanceId: false, Environment.MachineName);
                resource.AddAttributes([
                    new KeyValuePair<string, object>("deployment.environment.name", builder.Environment.EnvironmentName),
                ]);
            })
            .WithTracing(tracing => ConfigureTracing(tracing, options))
            .WithMetrics(ConfigureMetrics)
            // ⚠️ 一次调用同时给三条信号接上导出器，并按 OTel 标准环境变量解析地址与协议。
            //    不要退回到"每条信号各自 AddOtlpExporter 并手拼 /v1/xxx 路径"。
            .UseOtlpExporter();

        return builder;
    }

    private static void ConfigureTracing(TracerProviderBuilder tracing, KObservabilityOptions options)
    {
        tracing
            .AddAspNetCoreInstrumentation(instrumentation =>
            {
                instrumentation.Filter = context =>
                {
                    var path = context.Request.Path.Value;
                    return !string.IsNullOrEmpty(path)
                           && !options.ExcludedPathPrefixes.Exists(prefix =>
                               path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                };

                instrumentation.EnrichWithHttpRequest = (activity, request) =>
                {
                    // ⚠️ 这是 Action<Activity, HttpRequest>，**不能传 async lambda**——
                    //    那会变成 async void，异常无人接管，且 activity 可能在异步操作完成前就结束。
                    //    前身在这里做反向 DNS 查询（每请求 300ms 超时），还在回调内
                    //    `new MemoryCache(...)`：缓存每次都是新的、永远为空，注释却写着
                    //    "cache for 10 minutes"；而 MemoryCache 是 IDisposable 且内含 Timer，
                    //    从不释放 = 每请求泄漏一个。整段删除，不做主机名反查。
                    activity.DisplayName = $"{request.Method} {request.Path}";

                    var clientIp = ResolveClientIp(request.HttpContext);
                    activity.SetTag("client.address", clientIp ?? "unknown");
                };
            })
            .AddHttpClientInstrumentation(instrumentation =>
            {
                instrumentation.FilterHttpRequestMessage = message =>
                {
                    var host = message.RequestUri?.Host;
                    return host is not null
                           && !options.ExcludedOutboundHosts.Exists(excluded =>
                               host.Contains(excluded, StringComparison.OrdinalIgnoreCase));
                };

                instrumentation.EnrichWithHttpRequestMessage = (activity, message) =>
                {
                    activity.DisplayName = $"{message.Method} {message.RequestUri?.AbsolutePath}";
                    // URL 里可能带凭据（?access_token=…），一律先脱敏。
                    activity.SetTag("url.full",
                        KSensitiveDataRedactor.Redact(message.RequestUri?.ToString(), options.AdditionalSensitiveKeys));
                };
            })
            .AddSqlClientInstrumentation(instrumentation =>
            {
                // ⚠️ 按语句内容过滤，**不按 DbCommand 的具体类型分派**。
                //    前身写 `command is Microsoft.Data.SqlClient.SqlCommand`，
                //    切到 PostgreSQL 后整个过滤器静默失效。
                instrumentation.Filter = command =>
                {
                    if (command is not System.Data.Common.DbCommand db) return true;

                    var text = db.CommandText;
                    if (string.IsNullOrEmpty(text)) return true;

                    return !options.ExcludedSqlFragments.Exists(fragment =>
                        text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                };
            });
    }

    private static void ConfigureMetrics(MeterProviderBuilder metrics) =>
        metrics
            .AddRuntimeInstrumentation()
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

    /// <summary>取入口程序集的 informational version。</summary>
    /// <remarks>
    /// ⚠️ 前身这里写死 <c>"1.0"</c>，于是所有版本的遥测数据混在一起，
    /// 「这个问题是哪个版本引入的」这类问题无从回答。
    /// </remarks>
    private static string ResolveAssemblyVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        return assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly?.GetName().Version?.ToString()
               ?? "unknown";
    }

    /// <summary>
    /// 把当前登录用户打到 span 上（<c>enduser.id</c>）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 必须放在 <c>UseAuthentication()</c> **之后**，否则 <c>HttpContext.User</c> 还是匿名的，
    /// 每条 span 都不带用户——这个失败形态是静默的：遥测照常上报，只是永远查不出"谁触发的"。
    /// </remarks>
    public static IApplicationBuilder UseAppKitEndUserTag(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            await next();

            var user = context.User;
            var endUserId = user?.FindFirst("name")?.Value
                            ?? user?.FindFirst("email")?.Value
                            ?? user?.Identity?.Name;

            if (!string.IsNullOrWhiteSpace(endUserId) && Activity.Current is { } activity)
            {
                activity.SetTag("enduser.id", endUserId);
            }
        });
    }

    private static string? ResolveClientIp(HttpContext context)
    {
        // ⚠️ 只读已由 UseForwardedHeaders 处理过的 RemoteIpAddress。
        //    直接读 X-Forwarded-For 等于信任客户端可伪造的头——
        //    转发头的信任边界应当只在中间件里配置一次，不要在这里再判一遍。
        return context.Connection.RemoteIpAddress?.ToString();
    }
}

/// <summary>把日志正文里的凭据抹掉再上报。</summary>
internal sealed class KRedactingLogProcessor(KObservabilityOptions options)
    : OpenTelemetry.BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        if (data.FormattedMessage is { Length: > 0 } message)
        {
            data.FormattedMessage = KSensitiveDataRedactor.Redact(message, options.AdditionalSensitiveKeys);
        }

        base.OnEnd(data);
    }
}
