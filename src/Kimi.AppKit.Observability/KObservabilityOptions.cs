namespace Kimi.AppKit.Observability;

/// <summary>
/// 可观测性装配的配置。
/// </summary>
/// <remarks>
/// 【⚠️ 这里每一项都曾经是硬编码】前身的 OTel 装配里写死了：
/// <list type="bullet">
/// <item><c>service.namespace = "Company-{env}"</c>——会出现在**每一条**遥测数据上，
///       客户在自己的 Grafana 里看到的是别人公司的名字</item>
/// <item><c>serviceVersion = "1.0"</c>——所有版本的数据混在一起，没法按版本区分问题，
///       而项目里明明有取真实版本号的办法</item>
/// <item>HttpClient 过滤按域名 <c>internal.example.com</c> 排除——客户环境不命中，
///       等于这条过滤不存在</item>
/// <item>SQL 过滤只认 <c>Microsoft.Data.SqlClient.SqlCommand</c>——切到 PostgreSQL 后
///       **整个失效**，Hangfire 的轮询 SQL 会灌满 trace</item>
/// </list>
/// </remarks>
public sealed class KObservabilityOptions
{
    /// <summary>服务名。留空取 <c>IHostEnvironment.ApplicationName</c>。</summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// 服务命名空间，用于在同一个遥测后端里区分不同产品线。留空则不上报该属性。
    /// </summary>
    public string? ServiceNamespace { get; set; }

    /// <summary>服务版本。留空取入口程序集的 informational version。</summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// 不上报链路的请求路径前缀。健康检查、静态资源这类噪声默认已排除。
    /// </summary>
    /// <remarks>
    /// ⚠️ 默认清单里**不含**任何业务路由。前身写死了 <c>/tasks/stats</c>、
    /// <c>/debug/time/status</c> 这些前身特有的路径，对新项目是纯噪声。
    /// </remarks>
    public List<string> ExcludedPathPrefixes { get; } =
    [
        "/health", "/metrics", "/favicon.ico", "/robots.txt", "/_framework", "/_content",
    ];

    /// <summary>
    /// 不上报链路的出站主机名（子串匹配）。用于排除配置中心、日志后端这类自身设施的调用。
    /// </summary>
    public List<string> ExcludedOutboundHosts { get; } = [];

    /// <summary>
    /// 不上报的 SQL 语句片段（大小写不敏感）。默认覆盖两个 provider 的连接握手与
    /// Hangfire 轮询噪声。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这里按**语句内容**过滤，不按 <c>DbCommand</c> 的具体类型分派。
    /// 前身写的是 <c>command is Microsoft.Data.SqlClient.SqlCommand</c>，
    /// 于是切到 PostgreSQL 后整个过滤器静默失效——症状不是报错，是 trace 被噪声淹没。
    /// </remarks>
    public List<string> ExcludedSqlFragments { get; } =
    [
        // SQL Server
        "sp_getapplock", "sp_releaseapplock", "sp_reset_connection", "@@trancount",
        "sysutcdatetime()", "object_id(n'[dbo]", "set xact_abort",
        // PostgreSQL
        "pg_try_advisory_lock", "pg_advisory_unlock", "current_database()",
        // 两个 provider 共有
        "__efmigrationshistory", "select 1", "hangfire",
    ];

    /// <summary>
    /// 是否把 HTTP 请求体记进链路。**默认关闭。**
    /// </summary>
    /// <remarks>
    /// ⚠️ 打开之前先读 <see cref="KSensitiveDataRedactor"/> 的说明：脱敏靠字段名匹配，
    /// 业务自定义的敏感字段（<c>userPin</c> 之类）会漏掉，且**不会有任何报错**。
    /// 请求体进遥测等于把凭据复制到一个防护更弱的地方。
    /// </remarks>
    public bool CaptureRequestBody { get; set; }

    /// <summary>记录请求体时的截断长度。</summary>
    public int RequestBodyLimit { get; set; } = 2048;

    /// <summary>业务自定义的敏感字段名，追加到脱敏器的默认清单之后。</summary>
    public List<string> AdditionalSensitiveKeys { get; } = [];

    // 【为什么没有 EnablePrometheusEndpoint】曾有过一个默认 true 的同名选项，但**没有任何实现**——
    // 消费方设了它不生效，且不报错。三个理由决定删掉而不是补实现：
    //   1. 唯一能提供它的 OpenTelemetry.Exporter.Prometheus.AspNetCore 至今仍是 beta，
    //      不该出现在一个要发版的包的依赖里
    //   2. 默认 true 意味着 /metrics 免认证外露：路由名、主机名、异常类型、请求量全在里面，
    //      与已修掉的 ExposeDetailedErrors 默认 true 是同一类「失败倒向更松」的缺陷
    //   3. 标准拓扑是应用只推 OTLP，由 Collector 负责再导出给 Prometheus——
    //      不需要每个应用自己开抓取端点
}
