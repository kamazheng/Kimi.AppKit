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
    /// <remarks>
    /// ⚠️ 内部双引号必须按 SQL 标准转义成两个，否则标识符提前闭合、
    /// 后半段被当成 SQL 语法。标识符通常来自模型元数据而非用户输入，
    /// 但「通常」不是「一定」——拼 SQL 的地方不留这种缺口。
    /// </remarks>
    public string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ 大小写看似无关紧要（SQL 关键字不区分大小写），但这个字符串会被拼进
    /// <c>HasFilter</c>，而 <c>HasFilter</c> 的内容**是模型的一部分**——
    /// 改动它会让 EF 判定模型已变，从而要求一次纯粹重建过滤索引的迁移。
    /// 对现役服务来说那是零收益的升级风险，所以取值一经确定就不要再动。
    /// </remarks>
    public string BooleanLiteral(bool value) => value ? "TRUE" : "FALSE";

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
