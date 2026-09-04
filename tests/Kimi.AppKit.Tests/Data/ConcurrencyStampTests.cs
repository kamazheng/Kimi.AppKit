using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data.Auditing;
using Kimi.AppKit.Data.Conventions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>
/// 真实并发令牌（<see cref="IConcurrencyStamped"/>）。
/// </summary>
/// <remarks>
/// 【为什么需要这套】默认的影子属性令牌**序列化不出来**，导致「经 HTTP 编辑」
/// 在结构上不可能成功：客户端拿不到令牌 → 回传默认值 → WHERE 匹配不到行 →
/// 报「已被他人修改」而实际无冲突，刷新多少次都一样。
/// </remarks>
public sealed class ConcurrencyStampTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public ConcurrencyStampTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task 令牌是真实列_能被查询读出来()
    {
        // ⚠️ 这条是整套机制的前提：影子属性读不出来，也就序列化不出去。
        await using (var db = NewContext())
        {
            db.Add(new Doc { Title = "初稿" });
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var doc = await read.Docs.AsNoTracking().SingleAsync();

        Assert.False(string.IsNullOrWhiteSpace(doc.ConcurrencyStamp));
    }

    [Fact]
    public async Task 每次保存都换发新令牌()
    {
        string first, second;

        await using (var db = NewContext())
        {
            var doc = new Doc { Title = "初稿" };
            db.Add(doc);
            await db.SaveChangesAsync();
            first = doc.ConcurrencyStamp!;
        }

        await using (var db = NewContext())
        {
            var doc = await db.Docs.SingleAsync();
            doc.Title = "二稿";
            await db.SaveChangesAsync();
            second = doc.ConcurrencyStamp!;
        }

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task 带着正确令牌回来的更新成功()
    {
        // 模拟 HTTP 往返：读出来 → 断开 → 带着令牌回来 → Update
        Doc detached;
        await using (var db = NewContext())
        {
            db.Add(new Doc { Title = "初稿" });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            detached = await db.Docs.AsNoTracking().SingleAsync();
        }

        detached.Title = "客户端改过的";

        await using (var db = NewContext())
        {
            db.Docs.Update(detached);
            await db.SaveChangesAsync();
        }

        await using var verify = NewContext();
        Assert.Equal("客户端改过的", (await verify.Docs.SingleAsync()).Title);
    }

    [Fact]
    public async Task 带着过期令牌回来的更新被拒绝()
    {
        // ⚠️ 这条证明并发保护**真的在生效**——不能为了让编辑能用就把检查废掉。
        Doc stale;
        await using (var db = NewContext())
        {
            db.Add(new Doc { Title = "初稿" });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            stale = await db.Docs.AsNoTracking().SingleAsync();
        }

        // 另一个人先改了（令牌因此换新）
        await using (var db = NewContext())
        {
            var doc = await db.Docs.SingleAsync();
            doc.Title = "别人改的";
            await db.SaveChangesAsync();
        }

        stale.Title = "我基于旧版本改的";

        await using var last = NewContext();
        last.Docs.Update(stale);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => last.SaveChangesAsync());
    }

    private TestContext NewContext() =>
        new(new DbContextOptionsBuilder<TestContext>().UseSqlite(_connection).Options,
            new StubUser(), TimeProvider.System);

    public void Dispose() => _connection.Dispose();

    private sealed class Doc : IConcurrencyStamped
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? ConcurrencyStamp { get; set; }
    }

    private sealed class TestContext(
        DbContextOptions<TestContext> options, IKCurrentUser user, TimeProvider time)
        : AuditableDbContext(options, user, time)
    {
        public DbSet<Doc> Docs => Set<Doc>();

        protected override bool AuditingEnabled => false;   // 只测并发令牌，不写审计表

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Doc>().ToTable("Doc");
            modelBuilder.ApplyConcurrencyTokens(Database);
        }
    }

    private sealed class StubUser : IKCurrentUser
    {
        public ValueTask<string> GetUserNameAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult("tester");

        public ValueTask<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }
}
