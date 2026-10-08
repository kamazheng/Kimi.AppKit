using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Data;
using Kimi.AppKit.Data.Auditing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>
/// A6 回归：经 <see cref="EfCrudDataSource{TContext,TEntity}"/> 的更新路径（Attach + Modified，
/// 即 UI 表单回传 DTO 的形态）不得改写 CreatedOn/CreatedBy；新建须盖上真实时间与当前用户。
/// </summary>
public sealed class CrudAuditCreatedFieldsTests : IDisposable
{
    private static readonly DateTimeOffset T1 = new(2026, 3, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 3, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private TimeProvider _time = new FixedTime(T1);
    private string _user = "alice";

    public CrudAuditCreatedFieldsTests()
    {
        _connection.Open();
        using var db = Create();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private Ctx Create() => new(
        new DbContextOptionsBuilder<Ctx>().UseSqlite(_connection).Options,
        new StubUser(() => _user), _time);

    private EfCrudDataSource<Ctx, Item> Source() => new(new Factory(this));

    [Fact]
    public async Task 经Crud新建时CreatedOn为真实时间且CreatedBy为当前用户()
    {
        Assert.True((await Source().UpsertAsync(new Item { Name = "A" })).Succeeded);

        await using var db = Create();
        var row = await db.Items.AsNoTracking().SingleAsync();
        Assert.Equal(T1, row.CreatedOn);
        Assert.Equal("alice", row.CreatedBy);
    }

    [Fact]
    public async Task 经Crud更新时不改写CreatedOn与CreatedBy_只改Updated()
    {
        await Source().UpsertAsync(new Item { Name = "A" });

        // 模拟 UI 回传的 DTO：只带 Id 与可编辑字段，审计字段全是缺省值。
        _time = new FixedTime(T2);
        _user = "bob";
        var dto = new Item { Id = 1, Name = "B" };
        Assert.True((await Source().UpsertAsync(dto)).Succeeded);

        await using var db = Create();
        var row = await db.Items.AsNoTracking().SingleAsync();
        Assert.Equal("B", row.Name);
        Assert.Equal(T1, row.CreatedOn);
        Assert.Equal("alice", row.CreatedBy);
        Assert.Equal(T2, row.Updated);
        Assert.Equal("bob", row.UpdatedBy);
    }

    private sealed class Item : BaseAuditableEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Ctx(DbContextOptions<Ctx> options, IKCurrentUser user, TimeProvider time)
        : AuditableDbContext(options, user, time)
    {
        public DbSet<Item> Items => Set<Item>();
    }

    private sealed class Factory(CrudAuditCreatedFieldsTests t) : IDbContextFactory<Ctx>
    {
        public Ctx CreateDbContext() => t.Create();
    }

    private sealed class StubUser(Func<string> name) : IKCurrentUser
    {
        public ValueTask<string> GetUserNameAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(name());

        public ValueTask<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
