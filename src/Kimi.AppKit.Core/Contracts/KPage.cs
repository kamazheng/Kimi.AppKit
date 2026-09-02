namespace Kimi.AppKit.Core.Contracts;

/// <summary>
/// 分页查询的结果。
/// </summary>
/// <typeparam name="T">行类型。</typeparam>
/// <param name="Items">当前页的数据。</param>
/// <param name="TotalCount">
/// 满足筛选条件的总条数（不是当前页条数）。
/// ⚠️ 表格组件靠它算总页数；返回当前页条数会让分页器永远只显示一页。
/// </param>
/// <param name="Page">当前页码，**从 1 开始**，与 <see cref="KQuery.Page"/> 同基准。</param>
/// <param name="PageSize">每页条数。</param>
public sealed record KPage<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    /// <summary>空结果。用于「查询条件不成立、直接短路」的场景，避免各处各写一份空对象。</summary>
    public static KPage<T> Empty(KQuery query) => new([], 0, query.Page, query.PageSize);

    /// <summary>总页数。<see cref="TotalCount"/> 为 0 时返回 0（不是 1）。</summary>
    public int TotalPages => TotalCount <= 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;

    /// <summary>
    /// 当前页首行在整个结果集中的绝对序号（从 1 开始）。
    /// 服务端分页的表格要显示「真实行号」而不是「本页第几行」时用它——
    /// 否则每翻一页行号都从 1 重新开始，用户会以为数据错了。
    /// </summary>
    public int FirstRowNumber => TotalCount <= 0 ? 0 : (Page - 1) * PageSize + 1;
}
