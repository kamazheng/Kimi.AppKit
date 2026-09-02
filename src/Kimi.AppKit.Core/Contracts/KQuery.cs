namespace Kimi.AppKit.Core.Contracts;

/// <summary>
/// 列表查询请求：分页、排序、筛选。渲染模式无关——WASM 宿主把它序列化成 HTTP 请求，
/// Blazor Server 宿主直接交给数据源在进程内执行。
/// </summary>
/// <remarks>
/// 【⚠️ PageSize 必须有硬上限，且上限由服务端说了算】
/// 前身实现（`GetDbRecordsRequest`）默认 PageSize=1000 且服务端不做任何钳制，
/// 客户端传 `10000000` 就能拉爆内存——一个匿名可达的 DoS 面。
/// 所以本类型的 <see cref="PageSize"/> setter 自带钳制，且 <see cref="MaxPageSize"/>
/// 是**编译期常量而非可配置项**：让它可配置就等于把上限的决定权交回给不可信的一方。
/// 确实需要导出全部数据的场景走导出接口（流式），不要靠调大分页。
/// </remarks>
public sealed record KQuery
{
    /// <summary>分页大小的硬上限。</summary>
    public const int MaxPageSize = 500;

    /// <summary>默认分页大小。</summary>
    public const int DefaultPageSize = 50;

    private readonly int _pageSize = DefaultPageSize;
    private readonly int _page = 1;

    /// <summary>
    /// 页码，**从 1 开始**。
    /// ⚠️ 这个基准值一旦定下就不能改：MudBlazor 的 <c>MudTable</c> 用 0-based，
    /// <c>MudPagination</c> 用 1-based，混用时症状是「第一页显示第二页的数据」——
    /// 不报错，只是数据错位。组件层负责换算，契约层统一 1-based。
    /// </summary>
    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    /// <summary>每页条数。超出 <see cref="MaxPageSize"/> 会被静默钳制到上限。</summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    /// <summary>排序字段名。null 表示由数据源决定默认排序。</summary>
    public string? SortBy { get; init; }

    /// <summary>是否降序。</summary>
    public bool SortDescending { get; init; }

    /// <summary>
    /// 自由文本搜索词。
    /// ⚠️ 大小写语义由数据源实现负责显式声明——PostgreSQL 区分大小写、SQL Server 默认不区分，
    /// 同一句 LINQ 在两边给出不同结果且**编译和单测全过**。
    /// </summary>
    public string? Search { get; init; }

    /// <summary>结构化筛选条件：字段名 → 期望值。</summary>
    public IReadOnlyDictionary<string, string?> Filters { get; init; } =
        new Dictionary<string, string?>();

    /// <summary>跳过的记录数，由 <see cref="Page"/> 与 <see cref="PageSize"/> 推导。</summary>
    public int Skip => (Page - 1) * PageSize;
}
