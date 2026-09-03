using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data.Auditing;
using Kimi.AppKit.Data.Conventions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>
/// 组合式接入审计的行为契约：**基类名额已被占用**的上下文也要能拿到完整审计。
///
/// 【这个测试为什么存在】审计原本只能通过继承 <c>AuditableDbContext</c> 获得，
/// 而 DbContext 的基类名额只有一个。于是任何必须继承别的基类的上下文
/// （最典型的是 ASP.NET Core Identity 的 <see cref="IdentityDbContext{TUser}"/>）
/// 与审计**二选一**，且没有绕法。
///
/// 这个冲突在自己写的样例上永远不会暴露——样例的 DbContext 天生没有基类之争。
/// 它是在接入真实服务（Kimi.KMold.Auth）时才撞上的。
/// **这条测试如果早写，这个设计缺陷在 P2 就该暴露，而不是拖到 P8。**
///
/// 所以这里刻意用**真的** <see cref="IdentityDbContext{TUser}"/> 而不是自造的基类替身：
/// 替身等于「自己写的消费方」，测不出真实约束。
/// </summary>
public sealed class AuditingBehaviorTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AuditingBehaviorTests()
    {
        // ⚠️ 连接必须由测试持有并保持打开：SQLite 的内存库随最后一个连接关闭而销毁。
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private IdentityStyleContext NewContext(string user = "alice") =>
        new(new DbContextOptionsBuilder<IdentityStyleContext>().UseSqlite(_connection).Options,
            new FixedUser(user),
            new FixedTime(new DateTimeOffset(2026, 3, 15, 8, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task 继承_IdentityDbContext_的上下文也能拿到审计轨迹()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1" });
        await db.SaveChangesAsync();

        var trail = Assert.Single(await db.AuditTrails.ToListAsync());
        Assert.Equal(TrailType.Create, trail.Type);
        Assert.Equal("alice", trail.UserId);
        Assert.Contains("W1", trail.NewValues);
        // 自增主键在保存前是临时值，必须等数据库回填后才补进审计——
        // 这条路径正是需要「两阶段保存并入同一事务」的那条。
        Assert.Contains("\"Id\":1", trail.PrimaryKey);
    }

    [Fact]
    public async Task 组合方式下软删除同样被改写并记为_Delete()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Widgets.Add(new Widget { Name = "W1" });
        await db.SaveChangesAsync();

        var widget = await db.Widgets.SingleAsync();
        db.Widgets.Remove(widget);
        await db.SaveChangesAsync();

        var stored = await db.Widgets.IgnoreQueryFilters().SingleAsync();
        Assert.False(stored.Active);
        Assert.Contains(await db.AuditTrails.ToListAsync(), t => t.Type == TrailType.Delete);
    }

    [Fact]
    public async Task 组合方式不影响宿主基类自己的表()
    {
        // Identity 的表要照常可用——接入审计不能把宿主框架的能力挤掉。
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new IdentityUser("bob") { Id = "u1" });
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.Users.CountAsync());
    }

    // ── 测试替身 ──────────────────────────────────────────────

    private sealed class Widget : BaseAuditableEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// 模拟 Kimi.KMold.Auth 的形态：基类名额已经给了 Identity，审计只能靠组合接入。
    /// </summary>
    private sealed class IdentityStyleContext : IdentityDbContext<IdentityUser>
    {
        private readonly AuditingBehavior _auditing;

        public IdentityStyleContext(
            DbContextOptions<IdentityStyleContext> options, IKCurrentUser user, TimeProvider time)
            : base(options)
        {
            _auditing = new AuditingBehavior(user, time);
        }

        public DbSet<Widget> Widgets => Set<Widget>();

        // ⚠️ 组合方式下必须自己把 Trail 纳入模型，否则 context.Set<Trail>() 抛
        //    「Cannot create a DbSet for 'Trail' because this type is not included in the model」。
        public DbSet<Trail> AuditTrails => Set<Trail>();

        protected override void ConfigureConventions(ModelConfigurationBuilder builder)
        {
            builder.ApplyAppKitConventions();
            base.ConfigureConventions(builder);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Widget>().HasQueryFilter(w => w.Active);
        }

        // ⚠️ 必须传 base.SaveChangesAsync，传 this.SaveChangesAsync 会无限递归。
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _auditing.SaveChangesAsync(this, base.SaveChangesAsync, cancellationToken);
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
