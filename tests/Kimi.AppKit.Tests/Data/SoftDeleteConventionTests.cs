using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data.Auditing;
using Kimi.AppKit.Data.Conventions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>
/// 软删除的**读**端。⚠️ 这批用例的关键在于：测试用的 DbContext 里
/// **没有任何一处手写 <c>HasQueryFilter</c>**，过滤器完全由
/// <see cref="SoftDeleteConvention.ApplySoftDeleteFilter"/> 自动挂上。
/// 早期这个包只实现了写端（Remove() → Active=false），读端要靠开发者为每个实体
/// 自己记得写过滤器；而当时的测试恰好都手写了过滤器，把缺口整个盖住——
/// 漏配一个实体不会报错，只会让那张表的软删除静默失效。
/// </summary>
public sealed class SoftDeleteConventionTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public SoftDeleteConventionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private ShopContext NewContext() =>
        new(new DbContextOptionsBuilder<ShopContext>().UseSqlite(_connection).Options,
            new FakeCurrentUser(), TimeProvider.System);

    [Fact]
    public async Task 软删除的行默认不出现在查询里()
    {
        await using (var db = NewContext())
        {
            db.Widgets.Add(new Widget { Name = "保留" });
            db.Widgets.Add(new Widget { Name = "待删" });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var target = await db.Widgets.SingleAsync(w => w.Name == "待删");
            db.Widgets.Remove(target);   // 被改写成软删除
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var names = await db.Widgets.Select(w => w.Name).ToListAsync();
            Assert.Equal(["保留"], names);
        }
    }

    [Fact]
    public async Task IgnoreQueryFilters_仍能查到软删除的行()
    {
        // 软删除的语义是「可追溯、可恢复」，运维/审计场景要能显式看到被删的行。
        await using (var db = NewContext())
        {
            db.Widgets.Add(new Widget { Name = "待删" });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.Widgets.Remove(await db.Widgets.SingleAsync());
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            Assert.Empty(await db.Widgets.ToListAsync());
            var all = await db.Widgets.IgnoreQueryFilters().ToListAsync();
            Assert.Single(all);
            Assert.False(all[0].Active);
        }
    }

    [Fact]
    public async Task 消费方自己加的具名过滤器不会顶掉软删除过滤器()
    {
        // ⚠️ 匿名 HasQueryFilter 是覆盖而非叠加：消费方为同一实体再加一条过滤器，
        // 软删除过滤器会被静默顶掉、被删的行全部重新可见。具名过滤器按 AND 组合，不会互相覆盖。
        await using (var db = NewContext())
        {
            db.Gadgets.Add(new Gadget { Name = "在售且有效", Discontinued = false });
            db.Gadgets.Add(new Gadget { Name = "停产", Discontinued = true });
            db.Gadgets.Add(new Gadget { Name = "待删", Discontinued = false });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.Gadgets.Remove(await db.Gadgets.SingleAsync(g => g.Name == "待删"));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            // 停产的被消费方自己的过滤器挡掉，软删除的被本包的过滤器挡掉——两条同时生效。
            var names = await db.Gadgets.Select(g => g.Name).ToListAsync();
            Assert.Equal(["在售且有效"], names);
        }
    }

    // ── 测试替身 ──────────────────────────────────────────────

    private sealed class Widget : ISoftDeleteEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Active { get; set; } = true;
    }

    private sealed class Gadget : ISoftDeleteEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Discontinued { get; set; }
        public bool Active { get; set; } = true;
    }

    private sealed class ShopContext(
        DbContextOptions<ShopContext> options, IKCurrentUser currentUser, TimeProvider timeProvider)
        : AuditableDbContext(options, currentUser, timeProvider)
    {
        public DbSet<Widget> Widgets => Set<Widget>();
        public DbSet<Gadget> Gadgets => Set<Gadget>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // 消费方自己的业务过滤器，同样用具名的。
            builder.Entity<Gadget>().HasQueryFilter("Shop:InStock", (Gadget g) => !g.Discontinued);

            // ⚠️ 注意：本测试**没有**为 Widget/Gadget 手写 Active 过滤器，全靠下面这一行。
            builder.ApplySoftDeleteFilter();
        }
    }

    private sealed class FakeCurrentUser : IKCurrentUser
    {
        public ValueTask<string> GetUserNameAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult("tester");

        public ValueTask<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }
}
