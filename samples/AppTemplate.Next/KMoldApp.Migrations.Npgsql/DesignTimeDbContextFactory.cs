using Kimi.AppKit.Data;
using KMoldApp.Data;
using Microsoft.EntityFrameworkCore;

namespace KMoldApp.Migrations.Npgsql;

/// <summary>
/// PostgreSQL 迁移的设计时工厂，供 <c>dotnet ef</c> 使用。
/// </summary>
/// <remarks>
/// 【为什么每个迁移程序集各有一份】各自只认自己那个 provider，
/// 生成迁移时不需要「当前该用哪个 provider」这种运行期判断——
/// 执行的是哪个工程的命令，就是在为哪个 provider 生成迁移，不会搞混。
///
/// 【为什么必须有】没有它时 EF 会去构建 Web 主机的 host，那会读真实配置、连真实库；
/// 「生成一份迁移代码」这种纯离线操作不该要求能连上数据库。
/// </remarks>
public sealed class DesignTimeDbContextFactory : DesignTimeDbContextFactoryBase<KMoldDbContext>
{
    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ 指向不存在的实例：误跑 <c>database update</c> 会连不上而快速失败，
    /// 而不是悄悄改错库。要对真实库执行迁移，用
    /// <c>dotnet ef database update --connection "真实连接串"</c> 显式指定。
    /// </remarks>
    protected override string GetConnectionString() =>
        "Host=design-time-only;Database=DesignTimeOnly;Username=none;Password=none";

    /// <inheritdoc />
    protected override void ConfigureProvider(
        DbContextOptionsBuilder<KMoldDbContext> builder, string connectionString) =>
        builder.UseNpgsql(connectionString,
            o => o.MigrationsAssembly(ProviderSetup.NpgsqlMigrationsAssembly));

    /// <inheritdoc />
    protected override KMoldDbContext CreateContext(DbContextOptions<KMoldDbContext> options) =>
        new(options, DesignTimeCurrentUser.Instance, TimeProvider.System);
}
