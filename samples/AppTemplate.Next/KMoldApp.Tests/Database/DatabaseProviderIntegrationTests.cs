using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Data;
using Kimi.AppKit.Data.Auditing;
using Kimi.AppKit.Data.Providers;
using KMoldApp.Data;
using KMoldApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KMoldApp.Tests.Database;

/// <summary>
/// 双 provider 回归网：同一份模型在 PostgreSQL 与 SQL Server 下都能建表、CRUD、并写出审计记录。
/// </summary>
/// <remarks>
/// 【为什么必须连真库跑】SQLite 上的单元测试验证得了逻辑，验证不了两个 provider 的真实分歧。
/// 尤其是 UTC 归一：Npgsql 的 <c>timestamptz</c> **只接受 Offset == 0**，
/// 传 +08:00 直接抛；SQL Server 的 <c>datetimeoffset</c> 照单全收。
/// 同一段代码在 SQL Server 上跑得好好的，换到 PG 就在写入时崩，
/// 而且通常要等生产环境第一次收到带本地偏移的输入才暴露。
///
/// 【审计静默失效是查不出来的那类故障】功能全对，只是没有痕迹。这组测试把它钉死。
///
/// 【怎么跑】按需设环境变量，未设的 provider **跳过而不是失败**：
///   TEST_NPGSQL_CONNECTION="Host=localhost;Port=5433;Database=xxx_test;Username=..;Password=.."
///   TEST_SQLSERVER_CONNECTION="Server=...;Database=...;"
///
/// ⚠️ 用 <c>EnsureCreated</c> 而非 <c>Migrate</c>：直接按模型建表，与迁移方言无关。
/// ⚠️ 测试会 <c>EnsureDeleted</c> 清库，连接串务必指向**专用测试库**。
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class DatabaseProviderIntegrationTests
{
    private const string SqlServerEnv = "TEST_SQLSERVER_CONNECTION";
    private const string NpgsqlEnv = "TEST_NPGSQL_CONNECTION";
    private const string TestUser = "integration-test";

    [EnvFact(NpgsqlEnv)]
    public Task PostgreSQL_建表_读写_并写出审计() =>
        RunProviderContractAsync(
            DatabaseProvider.Npgsql, Suffix(Environment.GetEnvironmentVariable(NpgsqlEnv)!));

    [EnvFact(SqlServerEnv)]
    public Task SqlServer_建表_读写_并写出审计() =>
        RunProviderContractAsync(
            DatabaseProvider.SqlServer, Environment.GetEnvironmentVariable(SqlServerEnv)!);

    private static async Task RunProviderContractAsync(DatabaseProvider provider, string connectionString)
    {
        await using var context = CreateContext(provider, connectionString);

        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        var setting = new Setting
        {
            Name = "ProviderContract",
            Description = "双 provider 契约验证",
            ValueTypeFullName = typeof(string).FullName!,
            Value = "created",
        };

        context.Settings.Add(setting);
        await context.SaveChangesAsync();

        // ⚠️ 审计字段的「谁干的」由 IKCurrentUser 提供，不是每次 SaveChanges 手传。
        //    手传那套正是审计表说谎的来源——取不到用户名时会退化成 "System"。
        Assert.Equal(TestUser, setting.CreatedBy);

        setting.Value = "updated";
        await context.SaveChangesAsync();
        Assert.Equal(TestUser, setting.UpdatedBy);

        // 软删除：Remove 由框架底层拦成更新，行仍在、只是被过滤掉。
        // ⚠️ 手写 Active = false 会绕过拦截，审计里记成一次普通更新而不是删除。
        context.Settings.Remove(setting);
        await context.SaveChangesAsync();

        Assert.Empty(await context.Settings.Where(s => s.Name == "ProviderContract").ToListAsync());

        // 审计轨迹必须真的落盘。⚠️ 这是本组测试的核心断言：
        //    审计静默失效时，上面每一条都照样通过。
        var trails = await context.Set<Trail>().AsNoTracking().ToListAsync();
        Assert.NotEmpty(trails);
        Assert.Contains(trails, t => t.UserId == TestUser);
    }

    private static KMoldDbContext CreateContext(DatabaseProvider provider, string connectionString)
    {
        var builder = new DbContextOptionsBuilder<KMoldDbContext>();
        builder.Apply(provider, connectionString);

        return new KMoldDbContext(builder.Options, new FixedUser(TestUser), TimeProvider.System);
    }

    /// <summary>把库名加个后缀，避免与端点集成测试抢同一个库。</summary>
    private static string Suffix(string connectionString) =>
        connectionString.Replace("Database=", "Database=dbcontract_", StringComparison.OrdinalIgnoreCase);

    /// <summary>固定身份，用于断言审计字段确实记下了「谁干的」。</summary>
    private sealed class FixedUser(string name) : IKCurrentUser
    {
        public ValueTask<string> GetUserNameAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(name);

        public ValueTask<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }
}
