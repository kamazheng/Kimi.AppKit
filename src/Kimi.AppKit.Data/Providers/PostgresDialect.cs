using Microsoft.EntityFrameworkCore;

namespace Kimi.AppKit.Data.Providers;

/// <summary>PostgreSQL 方言。</summary>
/// <remarks>
/// 【为什么这个类不依赖 Npgsql 包】判断唯一约束冲突只需要检查异常里的 SqlState 字符串，
/// 不需要 <c>PostgresException</c> 的强类型——那样就得引用 Npgsql 包，违反本包「不依赖具体
/// provider」的铁律。反射读 <c>SqlState</c> 属性，兼容性和解耦两者都要。
/// </remarks>
public sealed class PostgresDialect : IDbProviderDialect
{
    /// <inheritdoc />
    public string Name => "Npgsql";

    /// <inheritdoc />
    public string Quote(string identifier) => $"\"{identifier}\"";

    /// <inheritdoc />
    public string BooleanLiteral(bool value) => value ? "true" : "false";

    /// <inheritdoc />
    public bool IsUniqueConstraintViolation(Exception exception)
    {
        // PostgreSQL 的唯一约束冲突 SqlState 固定是 23505。
        var sqlState = exception.GetType().GetProperty("SqlState")?.GetValue(exception) as string;
        return sqlState == "23505"
            || (exception.InnerException is not null && IsUniqueConstraintViolation(exception.InnerException));
    }

    /// <inheritdoc />
    public void ApplyModelConventions(ModelBuilder builder)
    {
        // 目前无需 PostgreSQL 专属的额外配置；三处真实分歧（NULL 语义、自引用外键、
        // 唯一冲突标识）分别在 UniqueNameConvention / 实体外键配置 / 本类里处理。
    }
}
