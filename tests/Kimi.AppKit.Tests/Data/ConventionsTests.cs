using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data.Conventions;
using Kimi.AppKit.Data.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>模型配置约定的行为契约。</summary>
public sealed class ConventionsTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public ConventionsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task 唯一名称约束阻止重复但允许软删除后重建同名()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Categories.Add(new Category { Name = "A" });
        await db.SaveChangesAsync();

        // 同名且都有效 → 违反唯一约束。
        db.Categories.Add(new Category { Name = "A" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task 软删除后可以重建同名记录()
    {
        // 唯一索引带 Active = true 的过滤条件，软删除的行不再占用这个名字。
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        var first = new Category { Name = "A" };
        db.Categories.Add(first);
        await db.SaveChangesAsync();

        db.Categories.Remove(first);   // 转成软删除
        await db.SaveChangesAsync();

        db.Categories.Add(new Category { Name = "A" });
        var affected = await db.SaveChangesAsync();

        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task 实体带有并发令牌()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        var entry = db.Entry(new Category { Name = "A" });
        var property = entry.Property("RowVersion");

        Assert.True(property.Metadata.IsConcurrencyToken);
    }

    [Fact]
    public void PostgresDialect_引号用双引号_布尔用_true_false()
    {
        var dialect = new PostgresDialect();
        Assert.Equal("\"Status\"", dialect.Quote("Status"));
        Assert.Equal("true", dialect.BooleanLiteral(true));
        Assert.Equal("false", dialect.BooleanLiteral(false));
    }

    [Fact]
    public void SqlServerDialect_引号用方括号_布尔用_1_0()
    {
        // 这条差异是「未软删除的行里 Name 唯一」这类过滤索引能否跨 provider 工作的关键——
        // 写错任何一边，过滤条件会被数据库忽略，唯一索引静默退化成全表唯一，不报错。
        var dialect = new SqlServerDialect();
        Assert.Equal("[Status]", dialect.Quote("Status"));
        Assert.Equal("1", dialect.BooleanLiteral(true));
        Assert.Equal("0", dialect.BooleanLiteral(false));
    }

    [Fact]
    public void 两个方言都要转义标识符里的结束符()
    {
        // 不转义时标识符会**提前闭合**，后半段被数据库当成 SQL 语法。
        // 标识符通常来自模型元数据而非用户输入，但拼 SQL 的地方不留这种缺口。
        //
        // ⚠️ 这条用例是回灌 Kimi.KMold 时补的：包原本两个 Quote 都没转义，
        // 而两个现役服务的四份实现全都转义了——包在这里是**子集**而不是超集。
        // 上面那两条用例之所以没发现，是因为它们只传了不含结束符的标识符。
        Assert.Equal("\"we\"\"ird\"", new PostgresDialect().Quote("we\"ird"));
        Assert.Equal("[we]]ird]", new SqlServerDialect().Quote("we]ird"));
    }

    [Fact]
    public void DatabaseProviderSetup_无法识别的名称就地抛而不是静默兜底()
    {
        // 配错 provider 名的后果是整个迁移基线走错方向，越晚发现越贵，不能悄悄选一个默认值。
        Assert.Throws<ArgumentException>(() => DatabaseProviderSetup.Resolve("oracle"));
        Assert.Throws<ArgumentException>(() => DatabaseProviderSetup.Resolve(null));
    }

    [Theory]
    [InlineData("Npgsql", DatabaseProvider.Npgsql)]
    [InlineData("postgresql", DatabaseProvider.Npgsql)]
    [InlineData("SqlServer", DatabaseProvider.SqlServer)]
    [InlineData("mssql", DatabaseProvider.SqlServer)]
    public void DatabaseProviderSetup_大小写与别名都能识别(string input, DatabaseProvider expected) =>
        Assert.Equal(expected, DatabaseProviderSetup.Resolve(input));

    // ── 测试替身 ──────────────────────────────────────────────

    private sealed class Category : AuditableEntityWithName;

    private sealed class CatalogContext(DbContextOptions<CatalogContext> options) : DbContext(options)
    {
        public DbSet<Category> Categories => Set<Category>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Category>().HasQueryFilter(c => c.Active);
            builder.ApplyUniqueNameConstraint(Database);
            builder.ApplyConcurrencyTokens(Database);
            base.OnModelCreating(builder);
        }
    }

    private CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseSqlite(_connection).Options);
}
