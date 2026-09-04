using Kimi.AppKit.Data.Providers;
using Microsoft.EntityFrameworkCore;

namespace KMoldApp.Data;

/// <summary>
/// 把 provider 选择接到具体的 EF Core 驱动上。
/// </summary>
/// <remarks>
/// 【为什么这部分在应用而不在包】<c>UseNpgsql</c> / <c>UseSqlServer</c> 来自各自的
/// provider 包。<c>Kimi.AppKit.Data</c> 刻意不引用它们中的任何一个——那会把不需要的
/// 驱动强加给消费方（只用 SQL Server 的部署也得拖着 Npgsql）。
/// 所以「是哪个 provider」由包的 <see cref="DatabaseProviderSetup"/> 判定，
/// 「怎么接线」由应用这一层完成。
///
/// ⚠️ 判定逻辑**不要在这里重写一份**。包里那份在无法识别时会抛异常，
/// 而旧模板自己那份是**静默回退 SqlServer**——配错 provider 名的后果是整个迁移基线
/// 走错方向，越晚发现越贵，静默兜底恰恰让它晚发现。
/// </remarks>
public static class ProviderSetup
{
    /// <summary>PostgreSQL 的迁移程序集名。</summary>
    /// <remarks>
    /// ⚠️ **一个 provider 一套迁移，不能共用。** 迁移文件里的列类型是**生成那一刻**
    /// 按当时的 provider 烤进去的字符串：PG 生成 <c>character varying(100)</c> /
    /// <c>timestamp with time zone</c>，SQL Server 生成 <c>nvarchar(100)</c> /
    /// <c>datetimeoffset</c>。共用一套时，<c>dotnet ef migrations script</c> 换个 provider
    /// 只会换掉标识符引号（<c>"X"</c> ↔ <c>[X]</c>），**列类型仍是另一个库的写法**——
    /// 脚本照常生成、能通过 code review，拿到目标库上执行才炸。
    ///
    /// 用字符串而非 <c>typeof</c>：业务工程不能反过来依赖迁移工程（会形成循环引用）。
    /// ⚠️ 改工程名要同步改这里，编译器帮不上忙——写错只在运行迁移时报「找不到迁移」。
    /// </remarks>
    public const string NpgsqlMigrationsAssembly = "KMoldApp.Migrations.Npgsql";

    /// <inheritdoc cref="NpgsqlMigrationsAssembly" />
    public const string SqlServerMigrationsAssembly = "KMoldApp.Migrations.SqlServer";

    /// <summary>按 provider 配置 <see cref="DbContextOptionsBuilder"/>（含各自独立的迁移程序集）。</summary>
    public static DbContextOptionsBuilder Apply(
        this DbContextOptionsBuilder options,
        DatabaseProvider provider,
        string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return provider switch
        {
            DatabaseProvider.Npgsql => options.UseNpgsql(
                connectionString, o => o.MigrationsAssembly(NpgsqlMigrationsAssembly)),
            DatabaseProvider.SqlServer => options.UseSqlServer(
                connectionString, o => o.MigrationsAssembly(SqlServerMigrationsAssembly)),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "未支持的 provider。"),
        };
    }
}
