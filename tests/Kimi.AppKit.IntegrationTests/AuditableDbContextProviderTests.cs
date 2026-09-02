using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data.Auditing;
using Kimi.AppKit.Data.Conventions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kimi.AppKit.IntegrationTests;

/// <summary>
/// 双 provider 集成测试：同一套模型与同一套断言，分别在真实的 PostgreSQL 与
/// SQL Server 容器上跑一遍。
/// </summary>
/// <remarks>
/// 【为什么不能只测一个 provider】本包的核心承诺是「PostgreSQL 与 SQL Server 行为一致」。
/// SQLite 单元测试（见 <c>Kimi.AppKit.Tests</c>）验证的是逻辑正确性，跑得快、
/// 不需要容器，但它既不是 PostgreSQL 也不是 SQL Server，测不出两者的真实分歧——
/// 尤其是 <see cref="Kimi.AppKit.Data.Conventions.UtcDateTimeOffsetConverter"/> 要防的那个坑，
/// 必须连真正的 Npgsql 驱动才能验证。
///
/// 【需要 Docker/Podman】跑这个工程需要能连上容器运行时。本机用 podman，
/// 已通过 `docker.sock` 转发兼容 Testcontainers 默认的 Docker API 客户端。
/// </remarks>
public sealed class AuditableDbContextProviderTests
{
    public static TheoryData<string> Providers => new(["Npgsql", "SqlServer"]);

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task 带本地时区偏移的时间写入后读回是同一个瞬时值(string provider)
    {
        // ⚠️ 这是本类存在的**核心理由**：Npgsql 对 timestamptz 只接受 Offset == 0，
        // 传 +08:00 会直接抛 ArgumentException；SQL Server 的 datetimeoffset 照单全收。
        // 同一段代码在 SQL Server 上跑得好好的，换到 PostgreSQL 就在写入时崩，
        // 且通常要等生产环境第一次收到带本地偏移的输入才暴露。
        await using var fixture = await ProviderFixture.CreateAsync(provider);
        await using var db = fixture.CreateContext();
        await db.Database.EnsureCreatedAsync();

        var shanghai = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.FromHours(8));
        db.Widgets.Add(new Widget { Name = "W1", ExpiresAt = shanghai });
        await db.SaveChangesAsync();

        await using var verify = fixture.CreateContext();
        var stored = await verify.Widgets.SingleAsync();

        Assert.Equal(shanghai.ToUniversalTime(), stored.ExpiresAt.ToUniversalTime());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task 唯一名称约束在两个_provider_上行为一致(string provider)
    {
        await using var fixture = await ProviderFixture.CreateAsync(provider);
        await using var db = fixture.CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.Categories.Add(new Category { Name = "A" });
        await db.SaveChangesAsync();

        await using var duplicate = fixture.CreateContext();
        duplicate.Categories.Add(new Category { Name = "A" });

        // 两个 provider 对唯一索引中 NULL 的语义相反（SQL Server 视多 NULL 为相同，
        // PostgreSQL 视为不同），但本约束的列是 NOT NULL，不受这条分歧影响——
        // 这正是实体基类要求 Name 用空串而不是 null 的原因。这里验证两边表现一致：都拒绝。
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task 软删除后可以在两个_provider_上重建同名记录(string provider)
    {
        await using var fixture = await ProviderFixture.CreateAsync(provider);
        await using var db = fixture.CreateContext();
        await db.Database.EnsureCreatedAsync();

        var first = new Category { Name = "A" };
        db.Categories.Add(first);
        await db.SaveChangesAsync();

        db.Categories.Remove(first);
        await db.SaveChangesAsync();

        db.Categories.Add(new Category { Name = "A" });
        var affected = await db.SaveChangesAsync();

        Assert.Equal(1, affected);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task 审计轨迹在两个_provider_上都能正确回填自增主键(string provider)
    {
        // 覆盖 AuditableDbContext 的两阶段保存路径——这条路径只有实体带自增主键时才触发，
        // 且正是架构评审 R9 要修的「不包事务时静默丢审计」的那条路径。
        await using var fixture = await ProviderFixture.CreateAsync(provider);
        await using var db = fixture.CreateContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1" });
        await db.SaveChangesAsync();

        var trail = await db.AuditTrails.SingleAsync();
        Assert.Equal(TrailType.Create, trail.Type);
        Assert.Contains("\"Id\":1", trail.PrimaryKey);
    }

    // ── 测试基础设施 ──────────────────────────────────────────────

    private sealed class Widget : BaseAuditableEntity
    {
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
    }

    private sealed class Category : AuditableEntityWithName;

    private sealed class TestContext(DbContextOptions<TestContext> options)
        : AuditableDbContext(options, new FixedUser(), TimeProvider.System)
    {
        public DbSet<Widget> Widgets => Set<Widget>();
        public DbSet<Category> Categories => Set<Category>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Widget>().HasQueryFilter(w => w.Active);
            builder.Entity<Category>().HasQueryFilter(c => c.Active);
            builder.ApplyUniqueNameConstraint(Database);
            base.OnModelCreating(builder);
        }
    }

    private sealed class FixedUser : IKCurrentUser
    {
        public ValueTask<string> GetUserNameAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult("integration-test");

        public ValueTask<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class ProviderFixture : IAsyncDisposable
    {
        private readonly PostgreSqlContainer? _postgres;
        private readonly MsSqlContainer? _sqlServer;
        private readonly string _provider;
        private readonly string _connectionString;

        private ProviderFixture(string provider, string connectionString, PostgreSqlContainer? pg, MsSqlContainer? ms)
        {
            _provider = provider;
            _connectionString = connectionString;
            _postgres = pg;
            _sqlServer = ms;
        }

        public static async Task<ProviderFixture> CreateAsync(string provider)
        {
            if (provider == "Npgsql")
            {
                // ⚠️ Testcontainers 4.14 起要求显式传镜像——固定版本号也顺带避免了
                // 「本地缓存的 :latest 与 CI 拉到的 :latest 是不同镜像」这类不可复现的失败。
                var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
                await container.StartAsync();
                return new ProviderFixture(provider, container.GetConnectionString(), container, null);
            }

            var sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await sqlServer.StartAsync();
            return new ProviderFixture(provider, sqlServer.GetConnectionString(), null, sqlServer);
        }

        public TestContext CreateContext()
        {
            var builder = new DbContextOptionsBuilder<TestContext>();
            if (_provider == "Npgsql") builder.UseNpgsql(_connectionString);
            else builder.UseSqlServer(_connectionString);
            return new TestContext(builder.Options);
        }

        public async ValueTask DisposeAsync()
        {
            if (_postgres is not null) await _postgres.DisposeAsync();
            if (_sqlServer is not null) await _sqlServer.DisposeAsync();
        }
    }
}
