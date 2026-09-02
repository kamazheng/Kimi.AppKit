using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>
/// EF 数据源的行为契约。重点是并发覆盖（lost update）——架构评审 R10。
/// </summary>
public sealed class EfCrudDataSourceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<ShopContext> _factory;

    public EfCrudDataSourceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _factory = new PooledFactory(_connection);

        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private EfCrudDataSource<ShopContext, Product> NewSource() => new(_factory);

    [Fact]
    public async Task 新实体走_Insert_已有实体走_Update()
    {
        var source = NewSource();

        Assert.True((await source.UpsertAsync(new Product { Name = "A", Price = 1 })).Succeeded);

        var stored = Assert.Single((await source.LoadAsync(new KQuery())).Items);
        stored.Price = 2;
        Assert.True((await source.UpsertAsync(stored)).Succeeded);

        var page = await source.LoadAsync(new KQuery());
        Assert.Equal(1, page.TotalCount);          // 是更新不是又插了一条
        Assert.Equal(2, page.Items[0].Price);
    }

    [Fact]
    public async Task 并发编辑时后写者被拒绝而不是无感覆盖前者()
    {
        // 【架构评审 R10 的回归用例】
        // 前身用 db.Entry(existing).CurrentValues.SetValues(dto) —— 先按主键查出实体、
        // 再用传入对象覆盖同名 CLR 属性。并发令牌是影子属性，DTO 上没有，SetValues 覆盖不到，
        // 于是 OriginalValue 永远是「刚查出来那一刻」的值，并发检查必然通过。
        // 结果：两个用户先后编辑，后者永远无感覆盖前者，而模型里明明配了并发令牌。
        var source = NewSource();
        await source.UpsertAsync(new Product { Name = "A", Price = 1 });

        // 两个用户各自读到同一版本
        var alice = Assert.Single((await source.LoadAsync(new KQuery())).Items);
        var bob = Assert.Single((await source.LoadAsync(new KQuery())).Items);

        alice.Price = 10;
        Assert.True((await source.UpsertAsync(alice)).Succeeded);

        bob.Price = 20;
        var bobResult = await source.UpsertAsync(bob);

        Assert.False(bobResult.Succeeded);
        Assert.Contains(bobResult.Errors, e => e.Contains("其他人修改"));

        // Alice 的值必须还在——这正是「无感覆盖」要防的。
        Assert.Equal(10, (await source.LoadAsync(new KQuery())).Items[0].Price);
    }

    [Fact]
    public async Task 分页返回总条数而不是当前页条数()
    {
        var source = NewSource();
        for (var i = 0; i < 25; i++) await source.UpsertAsync(new Product { Name = $"P{i}", Price = i });

        var page = await source.LoadAsync(new KQuery { Page = 2, PageSize = 10 });

        Assert.Equal(25, page.TotalCount);     // 返回当前页条数会让分页器永远只显示一页
        Assert.Equal(10, page.Items.Count);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(11, page.FirstRowNumber);
    }

    [Fact]
    public async Task 未知排序字段被静默忽略而不是让查询失败()
    {
        // 排序字段名来自客户端。不认识就按默认顺序返回，
        // 不能因为一个拼错的字段名让整个列表页 500。
        var source = NewSource();
        await source.UpsertAsync(new Product { Name = "A", Price = 1 });

        var page = await source.LoadAsync(new KQuery { SortBy = "NoSuchColumn" });

        Assert.Single(page.Items);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public async Task 按字段升降序排序(bool descending, int expectedFirst)
    {
        var source = NewSource();
        foreach (var p in (int[])[2, 1, 3])
            await source.UpsertAsync(new Product { Name = $"P{p}", Price = p });

        var page = await source.LoadAsync(new KQuery { SortBy = "Price", SortDescending = descending });

        Assert.Equal(expectedFirst, page.Items[0].Price);
    }

    [Fact]
    public async Task 删除不存在的记录返回可读提示而不是抛异常()
    {
        var result = await NewSource().DeleteAsync(9999);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("不存在"));
    }

    [Fact]
    public async Task 空结果集不触发额外查询也不返回_null()
    {
        var page = await NewSource().LoadAsync(new KQuery());

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalPages);
    }

    // ── 测试替身 ──────────────────────────────────────────────

    private sealed class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Price { get; set; }

        /// <summary>并发令牌。SQLite 没有原生 rowversion，用手动维护的版本列模拟。</summary>
        public Guid Version { get; set; } = Guid.NewGuid();
    }

    private sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            // IsConcurrencyToken + 保存时更新，等价于 SQL Server 的 rowversion /
            // PostgreSQL 的 xmin 在并发检查上的效果。
            builder.Entity<Product>().Property(p => p.Version).IsConcurrencyToken();
            base.OnModelCreating(builder);
        }

        public override int SaveChanges()
        {
            BumpVersions();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            BumpVersions();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void BumpVersions()
        {
            foreach (var entry in ChangeTracker.Entries<Product>()
                         .Where(e => e.State is EntityState.Modified))
            {
                entry.CurrentValues[nameof(Product.Version)] = Guid.NewGuid();
            }
        }
    }

    private sealed class PooledFactory(SqliteConnection connection) : IDbContextFactory<ShopContext>
    {
        public ShopContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<ShopContext>().UseSqlite(connection).Options);
    }
}
