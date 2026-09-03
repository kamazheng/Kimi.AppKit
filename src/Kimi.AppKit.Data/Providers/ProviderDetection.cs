using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kimi.AppKit.Data.Providers;

/// <summary>
/// 按 <see cref="DatabaseFacade.ProviderName"/> 判定当前 provider。
/// </summary>
/// <remarks>
/// 【为什么是字符串判断，不是 <c>Database.IsNpgsql()</c> 之类的强类型扩展】
/// 那些扩展方法各自定义在对应的 provider 包里（<c>Npgsql.EntityFrameworkCore.PostgreSQL</c> /
/// <c>Microsoft.EntityFrameworkCore.SqlServer</c>）。本包的铁律是**不依赖任何具体 provider**——
/// 引用它们中的任何一个都会违反这条铁律，也会把不需要的 provider 拖进消费方的部署包。
/// 字符串判断虽然不如强类型优雅，但它是唯一不引入依赖的做法。
/// </remarks>
public static class ProviderDetection
{
    /// <summary>是否 SQL Server。</summary>
    public static bool IsSqlServer(this DatabaseFacade database) =>
        database.ProviderName?.Contains("SqlServer", StringComparison.Ordinal) ?? false;

    /// <summary>是否 PostgreSQL。</summary>
    public static bool IsPostgres(this DatabaseFacade database) =>
        database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) ?? false;

    /// <summary>是否 SQLite（仅用于测试，本产品不支持它作为生产 provider）。</summary>
    public static bool IsSqlite(this DatabaseFacade database) =>
        database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) ?? false;

    /// <summary>
    /// 取当前 provider 对应的方言。
    /// </summary>
    /// <remarks>
    /// 让 <see cref="Conventions.EnumStringConvention.ApplyEnumStringConstraints"/> 的调用方式
    /// 与另两个约定（<c>ApplyUniqueNameConstraint</c> / <c>ApplyConcurrencyTokens</c>，都直接收
    /// <see cref="DatabaseFacade"/>）保持一致，消费方不必自己 <c>new PostgresDialect()</c> ——
    /// 那样等于把「当前是哪个 provider」这个判断复制到每个消费方，而这正是
    /// <c>IDbProviderDialect</c> 想要收口掉的东西。
    ///
    /// ⚠️ SQLite 归到 PostgreSQL 方言：两者都用双引号做标识符引号、都认 <c>true/false</c>
    /// 布尔字面量。SQLite 只用于测试，这个近似足够，且好过在这里抛异常让测试跑不起来。
    /// </remarks>
    public static IDbProviderDialect Dialect(this DatabaseFacade database) =>
        database.IsSqlServer() ? new SqlServerDialect() : new PostgresDialect();
}
