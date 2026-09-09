using Kimi.AppKit.Core.Contracts;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kimi.AppKit.Crud.Http;

/// <summary>
/// 把 <see cref="HttpResponseMessage"/> 翻译成 <see cref="KResult"/> / <see cref="KProblem"/>。
/// </summary>
/// <remarks>
/// 【为什么要有这个】<see cref="KResult"/> **从不在网线上传输**——它是只读结构、私有构造，
/// 服务端返回的是 HTTP 状态码 + <c>ProblemDetails</c>，客户端在本地重建 <see cref="KResult"/>。
/// 这份翻译原先私藏在 <see cref="KHttpCrudDataSource{T}"/> 里，于是每个调用**非 CRUD 端点**
/// 的应用都得自己抄一遍——抄出来的版本通常只剩「操作失败」四个字。
///
/// 【⚠️ 不要简化成「成功/失败」两分】401、403、409 三种失败对用户意味着完全不同的下一步
/// （重新登录 / 找管理员要权限 / 刷新后重试）。压成同一句提示，用户和排障的人都无从判断。
///
/// 【⚠️ 服务端正文不能原样倒给用户】早先的实现把整个响应正文当作失败原因，
/// 于是仓管在提示条里看到的是一整段 JSON：RFC 链接、状态码、traceId 全都在里面，
/// 而唯一有用的那句 <c>detail</c> 埋在中间。服务端的错误信息本来质量很高
/// （「块 X 可预留量只有 0（在库 1 − 已预留 1），要求 1」），只是没被取出来。
/// </remarks>
public static partial class KHttpResultExtensions
{
    /// <summary>按状态码与响应正文构造 <see cref="KResult"/>。</summary>
    public static async Task<KResult> ToKResultAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.IsSuccessStatusCode) return KResult.Ok();

        var problem = await ToKProblemAsync(response, cancellationToken).ConfigureAwait(false);
        return KResult.Fail(problem.ToMessages());
    }

    /// <summary>
    /// 解析服务端的 <c>ProblemDetails</c>，保留字段级校验错误。
    /// </summary>
    /// <remarks>
    /// ⚠️ 成功响应也会返回一个 <see cref="KProblem"/>（状态码 + 空消息由调用方自己判断）
    /// 是错的设计，所以这里**要求调用方先判成功**：成功时直接抛
    /// <see cref="InvalidOperationException"/>，避免「拿失败详情去描述一次成功」。
    /// </remarks>
    public static async Task<KProblem> ToKProblemAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.IsSuccessStatusCode)
            throw new InvalidOperationException("响应是成功的，没有失败详情可解析。");

        var status = (int)response.StatusCode;
        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        var (detail, fieldErrors) = Parse(body);

        return new KProblem(
            status,
            Clean(detail) ?? Canned(response.StatusCode) ?? $"操作失败（HTTP {status}）。",
            fieldErrors);
    }

    /// <summary>
    /// 从正文里取出 <c>detail</c>（回落 <c>title</c>）与 <c>errors</c> 字典。
    /// </summary>
    /// <remarks>
    /// ⚠️ 用 <see cref="JsonDocument"/> 而非反序列化到具体类型：WASM 会被裁剪，
    /// 反射式反序列化在裁剪后的产物上可能静默失败，而本地 <c>dotnet run</c> 看不出来。
    ///
    /// ⚠️ 正文不是 JSON 时（例如被状态码页重写成了一整页 HTML）**不能把它当消息**，
    /// 否则用户会收到一屏 HTML 源码。这种情况返回 null，交给状态码兜底文案。
    /// </remarks>
    private static (string? Detail, IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors)
        Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, KProblem.NoFieldErrors);

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return (null, KProblem.NoFieldErrors);

            var root = document.RootElement;

            var detail = Text(root, "detail") ?? Text(root, "title");

            return (detail, ReadFieldErrors(root));
        }
        catch (JsonException)
        {
            return (null, KProblem.NoFieldErrors);
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadFieldErrors(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors)
            || errors.ValueKind != JsonValueKind.Object)
            return KProblem.NoFieldErrors;

        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var field in errors.EnumerateObject())
        {
            // ⚠️ ValidationProblem 的值是数组，但自定义中间件常写成单个字符串。
            //    只认数组的话，那类响应会静默退化成「没有字段级错误」。
            string[] messages = field.Value.ValueKind switch
            {
                JsonValueKind.Array => [.. field.Value.EnumerateArray()
                    .Where(m => m.ValueKind == JsonValueKind.String)
                    .Select(m => Clean(m.GetString()))
                    .OfType<string>()],
                JsonValueKind.String => Clean(field.Value.GetString()) is { } single ? [single] : [],
                _ => [],
            };

            if (messages.Length > 0) map[field.Name] = messages;
        }

        return map.Count > 0 ? map : KProblem.NoFieldErrors;
    }

    /// <summary>
    /// 去掉 .NET 追加的 <c>(Parameter 'xxx')</c> 尾巴。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这不是「打磨文案」：服务层用 <see cref="ArgumentException"/> 表达业务拒绝
    /// 是本仓刻意的选择，于是**每一条**业务失败消息都会带上这个尾巴。
    /// 留着的话，用户看到的每句提示后面都跟着一个他不认识的英文参数名。
    /// </remarks>
    private static string? Clean(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;

        var cleaned = ParameterSuffix().Replace(message, string.Empty).Trim();
        return cleaned.Length > 0 ? cleaned : null;
    }

    /// <summary>状态码本身就说明了下一步该干什么的那几种。</summary>
    private static string? Canned(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "未登录或登录已过期。",
        HttpStatusCode.Forbidden => "没有执行该操作的权限。",
        HttpStatusCode.Conflict => "数据已被他人修改，请刷新后重试。",
        _ => null,
    };

    [GeneratedRegex(@"\s*\(Parameter '[^']*'\)\s*$")]
    private static partial Regex ParameterSuffix();
}
