using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data.Auditing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>
/// 审计的**失效边界**——这些测试断言的是「审计在这些情况下不生效」。
///
/// 【为什么要给缺陷写测试】这不是把 bug 固化成规格，而是因为这些边界
/// **无法被消除**，只能被知道。`ExecuteUpdate`/`ExecuteDelete` 与审计在语义上
/// 不可兼得：要审计就必须知道旧值，而批量操作的全部意义恰恰是不把行读进内存。
///
/// 真正的危害不是「批量操作没有审计」，而是**它不报错**。
/// 开发者以为接上审计就万事大吉，而每一处 `ExecuteUpdate` 都在悄悄绕过去。
/// 实测 Kimi.KMold.Files 里有 59 处这样的调用。
///
/// 这些用例的作用是把边界钉死：谁改了实现导致边界移动，这里会立刻变红。
/// </summary>
public sealed class AuditingBoundaryTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AuditingBoundaryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private BoundaryContext NewContext() =>
        new(new DbContextOptionsBuilder<BoundaryContext>().UseSqlite(_connection).Options,
            new FixedUser("alice"),
            new FixedTime(new DateTimeOffset(2026, 3, 15, 8, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task ExecuteUpdate_绕过审计_不产生任何轨迹()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1", Note = "before" });
        await db.SaveChangesAsync();

        var trailsAfterInsert = await db.AuditTrails.CountAsync();

        // EF Core 官方原话：ExecuteUpdate/ExecuteDelete
        // "completely unaware of EF's change tracker, and have no interaction with it whatsoever"。
        // 审计的全部实现都挂在 SaveChanges 上，追踪器里没有条目就采集不到任何东西。
        await db.Widgets.Where(w => w.Name == "W1")
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Note, "after"));

        Assert.Equal("after", (await db.Widgets.AsNoTracking().SingleAsync()).Note);
        Assert.Equal(trailsAfterInsert, await db.AuditTrails.CountAsync());
    }

    [Fact]
    public async Task ExecuteUpdate_不盖审计字段()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1", Note = "before" });
        await db.SaveChangesAsync();

        var before = await db.Widgets.AsNoTracking().SingleAsync();

        await db.Widgets.Where(w => w.Name == "W1")
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Note, "after"));

        // Updated / UpdatedBy 停留在上一次的值——数据变了，但「谁在什么时候改的」没变。
        var after = await db.Widgets.AsNoTracking().SingleAsync();
        Assert.Equal(before.Updated, after.Updated);
        Assert.Equal(before.UpdatedBy, after.UpdatedBy);
    }

    [Fact]
    public async Task ExecuteDelete_是物理删除_绕过软删除()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1" });
        await db.SaveChangesAsync();

        // 这是三条失效里最危险的一条：Remove() 会被改写成 Active=false，
        // 而 ExecuteDelete 直接发 DELETE 语句，行是真的没了、不可恢复。
        await db.Widgets.Where(w => w.Name == "W1").ExecuteDeleteAsync();

        Assert.Empty(await db.Widgets.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task SetValues_在值完全相同时不产生审计()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1", Note = "n1" });
        await db.SaveChangesAsync();
        var trailsAfterInsert = await db.AuditTrails.CountAsync();

        var widget = await db.Widgets.SingleAsync();

        // SetValues 只把**值真的变了**的属性标记为 Modified。
        // 全部相同时实体保持 Unchanged，采集器第一行就 continue 掉，一条审计都不产生。
        // 这本身是正确行为，但容易被误读成「我明明调了保存，为什么没记录」。
        db.Entry(widget).CurrentValues.SetValues(new { Name = "W1", Note = "n1" });
        await db.SaveChangesAsync();

        Assert.Equal(trailsAfterInsert, await db.AuditTrails.CountAsync());
    }

    [Fact]
    public async Task AsNoTracking_查出的实体改了也不会被保存更不会审计()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1", Note = "n1" });
        await db.SaveChangesAsync();
        var trailsAfterInsert = await db.AuditTrails.CountAsync();

        var detached = await db.Widgets.AsNoTracking().SingleAsync();
        detached.Note = "n2";
        await db.SaveChangesAsync();

        Assert.Equal("n1", (await db.Widgets.AsNoTracking().SingleAsync()).Note);
        Assert.Equal(trailsAfterInsert, await db.AuditTrails.CountAsync());
    }

    // ── 测试替身 ──────────────────────────────────────────────

    private sealed class Widget : BaseAuditableEntity
    {
        public string Name { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    private sealed class BoundaryContext(
        DbContextOptions<BoundaryContext> options, IKCurrentUser user, TimeProvider time)
        : AuditableDbContext(options, user, time)
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Widget>().HasQueryFilter(w => w.Active);
            base.OnModelCreating(builder);
        }
    }

    private sealed class FixedUser(string name) : IKCurrentUser
    {
        public ValueTask<string> GetUserNameAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(name);

        public ValueTask<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
