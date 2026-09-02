namespace Kimi.AppKit.Data.Providers;

/// <summary>支持的数据库 provider。</summary>
public enum DatabaseProvider
{
    /// <summary>PostgreSQL。</summary>
    Npgsql,

    /// <summary>Microsoft SQL Server。</summary>
    SqlServer
}

/// <summary>
/// provider 选择的**单一真相源**。运行期与设计期共用同一套解析逻辑。
/// </summary>
/// <remarks>
/// 【为什么要抽出来】运行期（<c>Program.cs</c>）与设计期（<c>IDesignTimeDbContextFactory</c>）
/// 各写一份 <c>UseNpgsql</c>/<c>UseSqlServer</c> 的话，两处迟早漂移——
/// 表现是「应用跑得好好的，但 <c>dotnet ef migrations add</c> 生成的是另一个 provider 的迁移」。
///
/// 【⚠️ 无法识别时不要静默兜底到某个 provider】
/// 配错 provider 名的后果是整个迁移基线走错方向，越晚发现越贵。就地抛。
/// </remarks>
public static class DatabaseProviderSetup
{
    /// <summary>配置键。</summary>
    public const string ConfigKey = "Database:Provider";

    /// <summary>
    /// 解析 provider 名称。
    /// </summary>
    /// <exception cref="ArgumentException">名称为空或无法识别。</exception>
    public static DatabaseProvider Resolve(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "npgsql" or "postgresql" or "postgres" => DatabaseProvider.Npgsql,
        "sqlserver" or "mssql" => DatabaseProvider.SqlServer,
        _ => throw new ArgumentException(
            $"无法识别的数据库 provider '{name}'。可选值：Npgsql / SqlServer。" +
            $"配置键为 '{ConfigKey}'。",
            nameof(name))
    };
}
