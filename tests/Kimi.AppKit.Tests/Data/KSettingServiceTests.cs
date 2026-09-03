using System.Text.Json.Serialization;
using Kimi.AppKit.Core.Settings;
using Kimi.AppKit.Data.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>
/// 落库设置服务的行为契约。
/// </summary>
/// <remarks>
/// 【这组测试盯的两件事】
/// 1. **四层解析顺序**。顺序错了不会报错，只会让某个设置「莫名其妙不是我配的值」。
/// 2. **读操作会写库**这个副作用真的存在且可预期——它是刻意的（管理界面要看得见这一项），
///    但会让只读副本上的第一次读直接抛，所以必须在测试里写死，不能靠读实现才发现。
/// </remarks>
public sealed class KSettingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public KSettingServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class Ctx(DbContextOptions<Ctx> options) : DbContext(options)
    {
        public DbSet<SettingRow> Settings => Set<SettingRow>();
    }

    /// <summary>消费方自己声明的实体——**包里没有这个类**，这正是本设计的要点。</summary>
    private sealed class SettingRow : IKSettingEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string ValueTypeFullName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsSystem { get; set; }
    }

    private sealed class Greeting : ISetting
    {
        [JsonIgnore]
        public string Description => "打招呼用的文案";
        public string Text { get; set; } = "hello";
    }

    private const string Key = "Greeting";

    private (Ctx Db, KSettingService<Ctx, SettingRow> Service) Build(bool register = true)
    {
        var db = new Ctx(new DbContextOptionsBuilder<Ctx>().UseSqlite(_connection).Options);
        db.Database.EnsureCreated();

        var defaults = new SettingDefaults();
        if (register) defaults.Register(Key, new Greeting());

        return (db, new KSettingService<Ctx, SettingRow>(
            db, defaults, NullLogger<KSettingService<Ctx, SettingRow>>.Instance));
    }

    [Fact]
    public async Task 第一层_数据库有值时优先于一切()
    {
        var (db, service) = Build();
        await service.SetAsync(Key, new Greeting { Text = "库里的" });

        // 即使调用方传了默认值，数据库的值也要赢。
        var result = await service.GetAsync(Key, new Greeting { Text = "调用方的" });

        Assert.Equal("库里的", result!.Text);
        db.Dispose();
    }

    [Fact]
    public async Task 第二层_数据库没有时用调用方给的默认值()
    {
        var (db, service) = Build();

        var result = await service.GetAsync(Key, new Greeting { Text = "调用方的" });

        Assert.Equal("调用方的", result!.Text);
        db.Dispose();
    }

    [Fact]
    public async Task 第三层_两者都没有时回落到登记的默认值()
    {
        var (db, service) = Build();

        var result = await service.GetAsync<Greeting>(Key);

        Assert.Equal("hello", result!.Text);
        db.Dispose();
    }

    [Fact]
    public async Task 第四层_没登记也没默认值时返回default()
    {
        var (db, service) = Build(register: false);

        Assert.Null(await service.GetAsync<Greeting>("没人认识的键"));
        db.Dispose();
    }

    [Fact]
    public async Task 读操作会把默认值写进数据库()
    {
        // ⚠️ 这是一个**有副作用的读**。刻意为之——管理界面要能看到并编辑这一项。
        //    代价是：只读副本上第一次读一个未初始化的设置会抛；
        //    在大事务里读设置，这次插入会跟着回滚。写成测试就是为了不让它变成惊喜。
        var (db, service) = Build();

        Assert.False(await service.ExistsAsync(Key));
        _ = await service.GetAsync<Greeting>(Key);

        Assert.True(await service.ExistsAsync(Key));
        db.Dispose();
    }

    [Fact]
    public async Task GetRawJson是纯读不产生副作用()
    {
        // 不想要上面那个副作用时的出口。
        var (db, service) = Build();

        Assert.Null(await service.GetRawJsonAsync(Key));
        Assert.False(await service.ExistsAsync(Key));
        db.Dispose();
    }

    [Fact]
    public async Task 系统项不能删除只能恢复默认()
    {
        var (db, service) = Build();
        _ = await service.GetAsync<Greeting>(Key);          // 触发落库，IsSystem = true

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(Key));

        await service.SetAsync(Key, new Greeting { Text = "改过了" });
        await service.ResetToDefaultAsync(Key);

        Assert.Equal("hello", (await service.GetAsync<Greeting>(Key))!.Text);
        db.Dispose();
    }

    [Fact]
    public async Task 非系统项可以删除且删后回落到登记默认值()
    {
        var (db, service) = Build(register: false);
        await service.SetAsync(Key, new Greeting { Text = "临时的" });   // 未登记 → IsSystem = false

        await service.DeleteAsync(Key);

        Assert.False(await service.ExistsAsync(Key));
        db.Dispose();
    }

    [Fact]
    public async Task 恢复默认要求该键登记过()
    {
        var (db, service) = Build(register: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ResetToDefaultAsync("没人认识的键"));
        db.Dispose();
    }

    [Fact]
    public async Task 存储值损坏时按未设置处理而不是抛()
    {
        // ⚠️ 设置表是人能在界面上编辑的。一条手改坏的 JSON 不该让整个应用起不来——
        //    尤其当那条设置正好是异常处理器自己要读的那一条时，会变成死循环。
        var (db, service) = Build();
        db.Settings.Add(new SettingRow
        {
            Name = Key,
            Value = "{ 这不是 JSON",
            ValueTypeFullName = typeof(Greeting).FullName!,
        });
        await db.SaveChangesAsync();

        Assert.Null(await service.GetAsync<Greeting>(Key));
        db.Dispose();
    }

    [Fact]
    public async Task 类型名不带版本号()
    {
        // 带版本的话，程序集一升版这个字段就与实际类型对不上，管理界面渲染不出编辑器。
        var (db, service) = Build();
        await service.SetAsync(Key, new Greeting());

        var raw = await db.Settings.SingleAsync(s => s.Name == Key);

        Assert.DoesNotContain("Version=", raw.ValueTypeFullName, StringComparison.Ordinal);
        Assert.Contains(typeof(Greeting).FullName!, raw.ValueTypeFullName, StringComparison.Ordinal);
        db.Dispose();
    }
}
