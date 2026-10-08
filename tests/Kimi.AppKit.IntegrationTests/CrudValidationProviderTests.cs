using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kimi.AppKit.IntegrationTests;

/// <summary>
/// A7：保存前校验与唯一约束冲突翻译在真实 PostgreSQL / SQL Server 上的行为。
/// 唯一冲突的错误码在两个 provider 上不同（23505 / 2601,2627），只有真驱动才测得到。
/// </summary>
public sealed class CrudValidationProviderTests
{
    public static TheoryData<string> Providers => new(["Npgsql", "SqlServer"]);

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task 留空与重名都返回失败结果而不是抛异常(string provider)
    {
        await using var host = await Host.CreateAsync(provider);
        await using (var db = host.CreateDbContext()) await db.Database.EnsureCreatedAsync();
        var source = new EfCrudDataSource<VContext, Item>(host);

        var blank = await source.UpsertAsync(new Item { Name = "" });
        Assert.False(blank.Succeeded);

        Assert.True((await source.UpsertAsync(new Item { Name = "A" })).Succeeded);
        var dup = await source.UpsertAsync(new Item { Name = "A" });

        Assert.False(dup.Succeeded);
        Assert.Contains(dup.Errors, e => e.Contains("已存在") && e.Contains("Name"));
        Assert.DoesNotContain(dup.Errors, e => e.Contains("IX_", StringComparison.OrdinalIgnoreCase));

        await using var verify = host.CreateDbContext();
        Assert.Equal(1, await verify.Items.CountAsync());
    }

    private sealed class Item : AuditableEntityWithName;

    private sealed class VContext(DbContextOptions<VContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();

        protected override void OnModelCreating(ModelBuilder builder) =>
            builder.Entity<Item>().HasIndex(i => i.Name).IsUnique();
    }

    private sealed class Host : IDbContextFactory<VContext>, IAsyncDisposable
    {
        private readonly PostgreSqlContainer? _pg;
        private readonly MsSqlContainer? _ms;
        private readonly string _provider;
        private readonly string _cs;

        private Host(string provider, string cs, PostgreSqlContainer? pg, MsSqlContainer? ms)
        { _provider = provider; _cs = cs; _pg = pg; _ms = ms; }

        public static async Task<Host> CreateAsync(string provider)
        {
            if (provider == "Npgsql")
            {
                var pg = new PostgreSqlBuilder("postgres:16-alpine").Build();
                await pg.StartAsync();
                return new Host(provider, pg.GetConnectionString(), pg, null);
            }
            var ms = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await ms.StartAsync();
            return new Host(provider, ms.GetConnectionString(), null, ms);
        }

        public VContext CreateDbContext()
        {
            var b = new DbContextOptionsBuilder<VContext>();
            if (_provider == "Npgsql") b.UseNpgsql(_cs); else b.UseSqlServer(_cs);
            return new VContext(b.Options);
        }

        public async ValueTask DisposeAsync()
        {
            if (_pg is not null) await _pg.DisposeAsync();
            if (_ms is not null) await _ms.DisposeAsync();
        }
    }
}
