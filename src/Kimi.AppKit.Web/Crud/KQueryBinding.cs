using System.Reflection;
using Kimi.AppKit.Core.Contracts;
using Microsoft.AspNetCore.Http;

namespace Kimi.AppKit.Web.Crud;

/// <summary>
/// 从查询串绑定出一个 <see cref="KQuery"/>。
/// </summary>
/// <remarks>
/// 【为什么要包一层，而不是直接给 <see cref="KQuery"/> 加 <c>BindAsync</c>】
/// <see cref="KQuery"/> 住在 <c>Kimi.AppKit.Core</c>，而 Core 是**零依赖**的——
/// 它不引 ASP.NET，也就不可能有 <c>HttpContext</c> 参数。把绑定放在 Web 包是这条约束的必然结果。
///
/// 【绑定规则】保留字段（<c>page</c>/<c>pageSize</c>/<c>sortBy</c>/<c>sortDescending</c>/<c>search</c>）
/// 走强类型解析；**其余全部查询参数一律进 <see cref="KQuery.Filters"/>**。
/// 于是 <c>?page=2&amp;Status=Active&amp;Category=五金</c> 天然就是「第 2 页、按状态与分类筛选」。
///
/// ⚠️ 这不构成注入面：字段名会在
/// <c>ReflectiveCrudDataSource</c> 里过实体属性白名单，未知字段静默丢弃；
/// 值按属性的 CLR 类型转换，转换失败也是丢弃这一条而不是让整个查询失败。
///
/// ⚠️ <c>pageSize</c> 的上限由 <see cref="KQuery"/> 自身钳制（<see cref="KQuery.MaxPageSize"/>），
/// **不要在这里再放一个可配置的上限**——可配置就等于把上限的决定权交回给不可信的一方。
/// </remarks>
public sealed class KQueryRequest
{
    /// <summary>解析结果。</summary>
    public KQuery Query { get; private init; } = new();

    /// <summary>保留字段名。出现在这个集合里的查询参数不进 <see cref="KQuery.Filters"/>。</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "page", "pageSize", "sortBy", "sortDescending", "search",
    };

    /// <summary>Minimal API 的自定义绑定约定：签名固定，由框架反射查找。</summary>
    public static ValueTask<KQueryRequest?> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        ArgumentNullException.ThrowIfNull(context);
        var q = context.Request.Query;

        var filters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in q)
        {
            if (Reserved.Contains(pair.Key)) continue;
            filters[pair.Key] = pair.Value.ToString();
        }

        var query = new KQuery
        {
            Page = TryInt(q["page"]) ?? 1,
            PageSize = TryInt(q["pageSize"]) ?? KQuery.DefaultPageSize,
            SortBy = Trimmed(q["sortBy"]),
            SortDescending = TryBool(q["sortDescending"]) ?? false,
            Search = Trimmed(q["search"]),
            Filters = filters,
        };

        return ValueTask.FromResult<KQueryRequest?>(new KQueryRequest { Query = query });
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // 脏值一律回退到默认，不 400——分页参数不该成为一个能把请求打挂的输入。
    private static int? TryInt(string? value) =>
        int.TryParse(value, out var parsed) ? parsed : null;

    private static bool? TryBool(string? value) =>
        bool.TryParse(value, out var parsed) ? parsed : null;
}
