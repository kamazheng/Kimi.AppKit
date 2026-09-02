using Kimi.AppKit.Core.Contracts;
using Xunit;

namespace Kimi.AppKit.Tests.Core;

/// <summary>
/// 分页契约的行为。
///
/// 【为什么值得写】前身实现的分页请求默认 PageSize=1000 且服务端不做任何钳制，
/// 客户端传 10000000 就能拉爆内存——一个匿名可达的 DoS 面。上限必须由契约本身保证，
/// 而不是指望每个数据源实现都记得自己钳一次。
/// </summary>
public class KQueryTests
{
    [Theory]
    [InlineData(10_000_000)]
    [InlineData(int.MaxValue)]
    [InlineData(KQuery.MaxPageSize + 1)]
    public void 超大_PageSize_被钳制到上限(int requested)
    {
        Assert.Equal(KQuery.MaxPageSize, new KQuery { PageSize = requested }.PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void 非法_PageSize_退回默认值(int requested)
    {
        Assert.Equal(KQuery.DefaultPageSize, new KQuery { PageSize = requested }.PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void 页码下限是_1_不是_0(int requested)
    {
        // 契约层统一 1-based。混用 0-based 的症状是「第一页显示第二页的数据」——
        // 不报错，只是数据错位。
        Assert.Equal(1, new KQuery { Page = requested }.Page);
    }

    [Fact]
    public void Skip_由页码与页大小推导()
    {
        Assert.Equal(0, new KQuery { Page = 1, PageSize = 20 }.Skip);
        Assert.Equal(40, new KQuery { Page = 3, PageSize = 20 }.Skip);
    }

    [Fact]
    public void Filters_默认非空可直接遍历()
    {
        Assert.NotNull(new KQuery().Filters);
        Assert.Empty(new KQuery().Filters);
    }

    [Fact]
    public void 空结果的总页数是_0_不是_1()
    {
        var page = KPage<string>.Empty(new KQuery());

        Assert.Equal(0, page.TotalPages);
        Assert.Equal(0, page.FirstRowNumber);
        Assert.Empty(page.Items);
    }

    [Fact]
    public void 总页数按总条数向上取整()
    {
        Assert.Equal(3, new KPage<string>([], 41, 1, 20).TotalPages);
        Assert.Equal(2, new KPage<string>([], 40, 1, 20).TotalPages);
    }

    [Fact]
    public void 首行绝对序号用于服务端分页的真实行号()
    {
        // 不用它的话每翻一页行号都从 1 重新开始，用户会以为数据错了。
        Assert.Equal(1, new KPage<string>([], 100, 1, 20).FirstRowNumber);
        Assert.Equal(41, new KPage<string>([], 100, 3, 20).FirstRowNumber);
    }
}

/// <summary>操作结果契约。</summary>
public class KResultTests
{
    [Fact]
    public void 成功时_Errors_是空列表而不是_null()
    {
        // 调用方可以直接遍历，不必先判空。
        Assert.True(KResult.Ok().Succeeded);
        Assert.Empty(KResult.Ok().Errors);
    }

    [Fact]
    public void 失败但没给原因时兜底一条()
    {
        // 「失败了但界面上什么都不显示」比失败本身更让人困惑。
        Assert.False(KResult.Fail().Succeeded);
        Assert.NotEmpty(KResult.Fail().Errors);
    }

    [Fact]
    public void Combine_全部成功才算成功且累加原因()
    {
        Assert.True(KResult.Combine([KResult.Ok(), KResult.Ok()]).Succeeded);

        var combined = KResult.Combine([KResult.Ok(), KResult.Fail("A"), KResult.Fail("B")]);
        Assert.False(combined.Succeeded);
        Assert.Equal(["A", "B"], combined.Errors);
    }
}
