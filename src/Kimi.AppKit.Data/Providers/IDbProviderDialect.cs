using Microsoft.EntityFrameworkCore;

namespace Kimi.AppKit.Data.Providers;

/// <summary>
/// PostgreSQL 与 SQL Server 之间**一切方言差异的唯一抽象点**。
/// </summary>
/// <remarks>
/// 【铁律】新增 provider 差异一律加在这里。
/// **禁止在 DbContext 或业务代码里散写 <c>if (provider == "Npgsql")</c>** ——
/// 一旦散开，「换 provider 要改哪些地方」就再也没人说得清，而漏改的表现全是静默的
/// （查询照常返回结果，只是结果不对）。
///
/// 【下游影响】迁移产物的表名、列类型、索引、约束全由此决定。
/// 改动本接口的任何实现都必须**重新生成两套迁移**并在两个 provider 上都跑测试。
/// </remarks>
public interface IDbProviderDialect
{
    /// <summary>provider 标识，与配置项 <c>Database:Provider</c> 的取值一致。</summary>
    string Name { get; }

    /// <summary>
    /// 把标识符包装成该 provider 的引用形式。
    /// </summary>
    /// <remarks>
    /// ⚠️ **PostgreSQL 会把裸标识符折叠成小写**，PascalCase 列名（如 <c>Status</c>）
    /// 写成裸 <c>Status</c> 会被解析成 <c>status</c> —— 建表当场失败。
    /// 一切手写 SQL 片段（<c>HasFilter</c> / <c>HasCheckConstraint</c> /
    /// <c>HasComputedColumnSql</c>）都必须经过本方法。
    /// </remarks>
    string Quote(string identifier);

    /// <summary>
    /// 把布尔值渲染成该 provider 在 SQL 片段里可用的字面量。
    /// </summary>
    /// <remarks>
    /// ⚠️ SQL Server 没有 bool 类型，过滤索引必须写 <c>[Active] = 1</c>；
    /// PostgreSQL 可以直接写 <c>"Active"</c>。
    /// 与 <see cref="Quote"/> 配合拼 <c>HasFilter</c>，是条件唯一索引能跨 provider 工作的前提。
    /// </remarks>
    string BooleanLiteral(bool value);

    /// <summary>
    /// 判断异常是否为唯一约束冲突。
    /// </summary>
    /// <remarks>
    /// ⚠️ 两个 provider 的错误标识**完全不同**：PostgreSQL 用 SqlState <c>23505</c>，
    /// SQL Server 用错误号 <c>2601</c>/<c>2627</c>，且异常类型分属各自的 ADO.NET 驱动。
    /// 「撞唯一键时退化为更新而不是报错」这类逻辑能否跨 provider 工作，全看这里。
    /// </remarks>
    bool IsUniqueConstraintViolation(Exception exception);

    /// <summary>provider 特有的模型配置（扩展启用、列类型覆盖等），在通用配置之后执行。</summary>
    /// <remarks>
    /// 【⚠️ 不要在这里配大小写不敏感的 collation】
    /// PostgreSQL 默认区分大小写、SQL Server 默认不区分，看似应该用 collation 抹平。
    /// 但正确做法是走**持久化的规范化列**（存一份 upper-case 的副本用于匹配），
    /// 两个 provider 行为天然一致，不依赖数据库排序规则。
    /// 而且 PostgreSQL 的非确定性 collation **不支持 LIKE**，配了它模糊搜索会直接失效。
    /// </remarks>
    void ApplyModelConventions(ModelBuilder builder);
}
