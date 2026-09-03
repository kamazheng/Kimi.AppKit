using Kimi.AppKit.Components.Components;
using Kimi.AppKit.Core.Contracts;
using MudBlazor;
using Xunit;

namespace Kimi.AppKit.Components.Tests;

/// <summary>
/// 服务端分页表格。⚠️ 核心风险点是基准换算：<c>TableState.Page</c> 是 0-based，
/// <see cref="KQuery.Page"/> 是 1-based，这个仓库已经因为混用这两个基准出过
/// "第一页显示第二页数据"的事故。只测 <see cref="KDataTable{T}.BuildQuery"/> 这个纯函数——
/// 完整渲染 MudTable 需要拉起 IScrollManager 等一整套 MudBlazor 服务，
/// 而基准换算这个风险点与渲染管线无关，纯函数测试已经把它锁死。
/// </summary>
public class KDataTableTests
{
    private static TableState State(int page, int pageSize, string? sortLabel = null,
        SortDirection sortDirection = SortDirection.None) =>
        new() { Page = page, PageSize = pageSize, SortLabel = sortLabel, SortDirection = sortDirection };

    [Fact]
    public void 首次加载对应0based的第0页时产出1based的第一页()
    {
        var query = KDataTable<object>.BuildQuery(filter: null, State(page: 0, pageSize: 20));

        Assert.Equal(1, query.Page);
    }

    [Fact]
    public void 翻到0based第二页时产出1based的第三页()
    {
        var query = KDataTable<object>.BuildQuery(filter: null, State(page: 2, pageSize: 20));

        Assert.Equal(3, query.Page);
    }

    [Fact]
    public void 外部Filter的Search被保留到合并后的查询里()
    {
        var filter = new KQuery { Search = "关键字" };

        var query = KDataTable<object>.BuildQuery(filter, State(page: 0, pageSize: 20));

        Assert.Equal("关键字", query.Search);
    }

    [Fact]
    public void 排序标签与方向被正确映射()
    {
        var query = KDataTable<object>.BuildQuery(
            filter: null, State(page: 0, pageSize: 20, sortLabel: "Name", sortDirection: SortDirection.Descending));

        Assert.Equal("Name", query.SortBy);
        Assert.True(query.SortDescending);
    }

    [Fact]
    public void 空排序标签时SortBy为null不是空字符串()
    {
        var query = KDataTable<object>.BuildQuery(filter: null, State(page: 0, pageSize: 20, sortLabel: ""));

        Assert.Null(query.SortBy);
    }
}
