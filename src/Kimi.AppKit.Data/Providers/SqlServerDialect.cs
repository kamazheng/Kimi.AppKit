using Microsoft.EntityFrameworkCore;

namespace Kimi.AppKit.Data.Providers;

/// <summary>SQL Server 方言。</summary>
public sealed class SqlServerDialect : IDbProviderDialect
{
    /// <inheritdoc />
    public string Name => "SqlServer";

    /// <inheritdoc />
    public string Quote(string identifier) => $"[{identifier}]";

    /// <inheritdoc />
    public string BooleanLiteral(bool value) => value ? "1" : "0";

    /// <inheritdoc />
    public bool IsUniqueConstraintViolation(Exception exception)
    {
        // SQL Server 的唯一约束/索引冲突错误号是 2601（唯一索引）与 2627（唯一约束）。
        var number = exception.GetType().GetProperty("Number")?.GetValue(exception) as int?;
        if (number is 2601 or 2627) return true;

        return exception.InnerException is not null && IsUniqueConstraintViolation(exception.InnerException);
    }

    /// <inheritdoc />
    public void ApplyModelConventions(ModelBuilder builder)
    {
        // 目前无需 SQL Server 专属的额外配置。
    }
}
