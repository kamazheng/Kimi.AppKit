using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>
/// 反射驱动的通用搜索/筛选。核心风险点：白名单校验（客户端传入的字段名/值不可信）
/// 与"脏数据不搞挂整个查询"，这是通用查询入口天然缺少挂授权位置的补偿。
/// </summary>
public sealed class ReflectiveCrudDataSourceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<ShopContext> _factory;

    public ReflectiveCrudDataSourceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _factory = new PooledFactory(_connection);

        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private ReflectiveCrudDataSource<ShopContext, Product> NewSource() => new(_factory);

    [Fact]
    public async Task Search命中任一字符串属性即返回()
    {
        var source = NewSource();
        await source.UpsertAsync(new Product { Name = "螺栓", Category = "五金" });
        await source.UpsertAsync(new Product { Name = "垫片", Category = "橡胶" });

        var page = await source.LoadAsync(new KQuery { Search = "螺" });

        Assert.Single(page.Items);
        Assert.Equal("螺栓", page.Items[0].Name);
    }

    [Fact]
    public async Task Search匹配Category字段而不只是Name()
    {
        var source = NewSource();
        await source.UpsertAsync(new Product { Name = "螺栓", Category = "五金" });
        await source.UpsertAsync(new Product { Name = "垫片", Category = "橡胶" });

        var page = await source.LoadAsync(new KQuery { Search = "五金" });

        Assert.Single(page.Items);
        Assert.Equal("螺栓", page.Items[0].Name);
    }

    [Fact]
    public async Task Filters按属性名精确匹配()
    {
        var source = NewSource();
        await source.UpsertAsync(new Product { Name = "A", Category = "五金" });
        await source.UpsertAsync(new Product { Name = "B", Category = "橡胶" });

        var page = await source.LoadAsync(new KQuery { Filters = new Dictionary<string, string?> { ["Category"] = "五金" } });

        Assert.Single(page.Items);
        Assert.Equal("A", page.Items[0].Name);
    }

    [Fact]
    public async Task Filters里不存在的字段名被静默忽略而不是抛异常()
    {
        var source = NewSource();
        await source.UpsertAsync(new Product { Name = "A", Category = "五金" });

        var page = await source.LoadAsync(new KQuery
        {
            Filters = new Dictionary<string, string?> { ["NoSuchColumn"] = "随便"},
        });

        Assert.Single(page.Items); // 未知字段不参与过滤，不是"查不到任何结果"
    }

    [Fact]
    public async Task Filters里类型转换失败的值被静默忽略()
    {
        var source = NewSource();
        await source.UpsertAsync(new Product { Name = "A", Category = "五金", Stock = 10 });

        // Stock 是 int，传一个无法转换的字符串——这条筛选条件应该被丢弃，而不是让整个查询 500。
        var page = await source.LoadAsync(new KQuery
        {
            Filters = new Dictionary<string, string?> { ["Stock"] = "不是数字" },
        });

        Assert.Single(page.Items);
    }

    [Fact]
    public async Task 标记HideFromTable的属性不参与Search()
    {
        var source = NewSource();
        await source.UpsertAsync(new Product { Name = "A", Category = "五金", InternalNote = "机密关键字" });

        var page = await source.LoadAsync(new KQuery { Search = "机密关键字" });

        Assert.Empty(page.Items);
    }

    // ── 测试替身 ──────────────────────────────────────────────

    private sealed class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int Stock { get; set; }

        [HideFromTable]
        public string InternalNote { get; set; } = string.Empty;
    }

    private sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();
    }

    private sealed class PooledFactory(SqliteConnection connection) : IDbContextFactory<ShopContext>
    {
        public ShopContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<ShopContext>().UseSqlite(connection).Options);
    }
}
