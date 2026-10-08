using System.ComponentModel.DataAnnotations;
using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>
/// <c>EfCrudDataSource.UpsertAsync</c> 的保存前校验与唯一约束冲突翻译（A7）。
/// 端点手工读 body，minimal API 的自动校验不跑，所以校验必须落在数据源这一层；
/// Excel 导入逐行调同一个 UpsertAsync，自然一并覆盖。
/// </summary>
public sealed class EfCrudDataSourceValidationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<VContext> _factory;

    public EfCrudDataSourceValidationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _factory = new Factory(_connection);
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private EfCrudDataSource<VContext, Supplier> Suppliers() => new(_factory);

    private int Count() { using var db = _factory.CreateDbContext(); return db.Suppliers.Count(); }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task 必填名称留空时保存失败且不落库(string name)
    {
        var result = await Suppliers().UpsertAsync(new Supplier { Name = name });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Name"));
        Assert.Equal(0, Count());
    }

    [Fact]
    public async Task 合法实体保存成功()
    {
        Assert.True((await Suppliers().UpsertAsync(new Supplier { Name = "A" })).Succeeded);
        Assert.Equal(1, Count());
    }

    [Fact]
    public async Task 更新路径同样校验且库里保留旧值()
    {
        var source = Suppliers();
        await source.UpsertAsync(new Supplier { Name = "A" });
        var stored = (await source.LoadAsync(new KQuery())).Items.Single();

        stored.Name = "";
        var result = await source.UpsertAsync(stored);

        Assert.False(result.Succeeded);
        Assert.Equal("A", (await source.LoadAsync(new KQuery())).Items.Single().Name);
    }

    [Fact]
    public async Task 超长名称被拒绝()
    {
        var result = await Suppliers().UpsertAsync(new Supplier { Name = new string('x', 201) });
        Assert.False(result.Succeeded);
        Assert.Equal(0, Count());
    }

    [Fact]
    public async Task 审计字段上的_Required_不会因客户端未提供而拒绝保存()
    {
        // 审计字段由审计上下文在保存时填，校验时必为缺省值，不能算客户端的错。
        var source = new EfCrudDataSource<VContext, Doc>(_factory);
        var result = await source.UpsertAsync(new Doc { Title = "T" });
        Assert.True(result.Succeeded, string.Join(";", result.Errors));
    }

    [Fact]
    public async Task 非审计字段的_Required_仍然生效()
    {
        var source = new EfCrudDataSource<VContext, Doc>(_factory);
        Assert.False((await source.UpsertAsync(new Doc { Title = "" })).Succeeded);
    }

    [Fact]
    public async Task 重复名称返回可读失败而不是抛异常()
    {
        var source = Suppliers();
        Assert.True((await source.UpsertAsync(new Supplier { Name = "A" })).Succeeded);

        var result = await source.UpsertAsync(new Supplier { Name = "A" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("已存在"));
        Assert.Contains(result.Errors, e => e.Contains("Name"));
        Assert.DoesNotContain(result.Errors, e => e.Contains("Suppliers", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, Count());
    }

    // ── 测试替身 ──────────────────────────────────────────────

    private sealed class Supplier : AuditableEntityWithName;

    private sealed class Doc : IAuditableEntity
    {
        public int Id { get; set; }
        [Required] public string Title { get; set; } = string.Empty;
        public DateTimeOffset Updated { get; set; }
        [Required] public string? UpdatedBy { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        [Required] public string? CreatedBy { get; set; }
    }

    private sealed class VContext(DbContextOptions<VContext> options) : DbContext(options)
    {
        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<Doc> Docs => Set<Doc>();

        protected override void OnModelCreating(ModelBuilder builder) =>
            builder.Entity<Supplier>().HasIndex(s => s.Name).IsUnique();

        /// <summary>模拟审计上下文：保存时才填审计字段。</summary>
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var d in ChangeTracker.Entries<Doc>())
            {
                d.Entity.CreatedBy ??= "u";
                d.Entity.UpdatedBy ??= "u";
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class Factory(SqliteConnection connection) : IDbContextFactory<VContext>
    {
        public VContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<VContext>().UseSqlite(connection).Options);
    }
}
