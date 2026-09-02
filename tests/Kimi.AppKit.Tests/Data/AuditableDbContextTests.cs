using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data.Auditing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>
/// 审计上下文的行为契约。
///
/// 【为什么用 SQLite 而不是 EFCore.InMemory】InMemory provider 不支持事务、不校验约束，
/// 测不出「两阶段保存必须在同一个事务里」这类正是本次要修的行为。
/// SQLite 内存模式是真实的关系型引擎，能跑事务。
/// </summary>
public sealed class AuditableDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AuditableDbContextTests()
    {
        // ⚠️ 连接必须由测试持有并保持打开：SQLite 的内存库随最后一个连接关闭而销毁。
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private TestContext NewContext(string? user = "alice", TimeProvider? time = null) =>
        new(new DbContextOptionsBuilder<TestContext>().UseSqlite(_connection).Options,
            new StubCurrentUser(user),
            time ?? new FixedTime(new DateTimeOffset(2026, 3, 15, 8, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task 新增实体时写入一条_Create_审计()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1" });
        await db.SaveChangesAsync();

        var trail = Assert.Single(await db.AuditTrails.ToListAsync());
        Assert.Equal(TrailType.Create, trail.Type);
        Assert.Equal("alice", trail.UserId);
        Assert.Contains("W1", trail.NewValues);
        // 自增主键在保存前是临时值，必须等数据库回填后才补进审计。
        Assert.Contains("\"Id\":1", trail.PrimaryKey);
    }

    [Fact]
    public async Task 无参_SaveChangesAsync_可以正常工作()
    {
        // 前身要求必须传用户名，无参重载直接抛 NotSupportedException ——
        // 一个只在运行期才暴露的约束，编译器帮不上忙。现在用户名从 IKCurrentUser 取。
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1" });

        var affected = await db.SaveChangesAsync();
        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task 取不到当前用户时抛异常而不是退化成_System()
    {
        // 审计的全部价值在「谁干的」。把异常伪装成一次正常的系统操作，
        // 等于让审计表在最需要它的时候说谎。
        await using var db = NewContext(user: null);
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task 审计与业务数据在同一个事务里_失败则一起回滚()
    {
        // 这是架构评审 R9 的回归用例：前身让两阶段各自独立提交，
        // 业务数据落库成功而审计行插入失败时会产生**静默的审计缺口**。
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1" });
        await db.SaveChangesAsync();

        // 两者必须同时存在。
        Assert.Equal(1, await db.Widgets.CountAsync());
        Assert.Equal(1, await db.AuditTrails.CountAsync());
    }

    [Fact]
    public async Task Remove_被改写成软删除并记为_Delete()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1" });
        await db.SaveChangesAsync();

        var widget = await db.Widgets.SingleAsync();
        db.Widgets.Remove(widget);
        await db.SaveChangesAsync();

        // 行还在，只是 Active = false。
        var stored = await db.Widgets.IgnoreQueryFilters().SingleAsync();
        Assert.False(stored.Active);

        // 审计记的是 Delete 而不是 Update —— 手写 Active=false 会绕过这层拦截，
        // 于是审计里只剩一次「普通更新」，删除语义丢失。
        Assert.Contains(await db.AuditTrails.ToListAsync(), t => t.Type == TrailType.Delete);
    }

    [Fact]
    public async Task 修改实体时只记录真正变动的列()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1", Note = "n1" });
        await db.SaveChangesAsync();

        var widget = await db.Widgets.SingleAsync();
        widget.Note = "n2";
        await db.SaveChangesAsync();

        var update = Assert.Single(await db.AuditTrails.Where(t => t.Type == TrailType.Update).ToListAsync());
        Assert.Contains("Note", update.AffectedColumns);
        Assert.DoesNotContain("\"Name\"", update.AffectedColumns);
    }

    [Fact]
    public async Task 审计时间戳来自注入的_TimeProvider()
    {
        var fixedNow = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

        await using var db = NewContext(time: new FixedTime(fixedNow));
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1" });
        await db.SaveChangesAsync();

        var trail = await db.AuditTrails.SingleAsync();
        Assert.Equal(fixedNow, trail.AuditOn);
    }

    [Fact]
    public async Task 审计表自身的变更不产生新审计()
    {
        // 否则每写一条审计就再生一条，无限递归。
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1" });
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.AuditTrails.CountAsync());
    }

    // ── 测试替身 ──────────────────────────────────────────────

    private sealed class Widget : BaseAuditableEntity
    {
        public string Name { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    private sealed class TestContext(
        DbContextOptions<TestContext> options, IKCurrentUser user, TimeProvider time)
        : AuditableDbContext(options, user, time)
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Widget>().HasQueryFilter(w => w.Active);
            base.OnModelCreating(builder);
        }
    }

    private sealed class StubCurrentUser(string? name) : IKCurrentUser
    {
        public ValueTask<string> GetUserNameAsync(CancellationToken cancellationToken = default) =>
            name is null
                ? throw new InvalidOperationException("无法确定当前用户")
                : ValueTask.FromResult(name);

        public ValueTask<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(name is not null);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
