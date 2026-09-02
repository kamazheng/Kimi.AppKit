namespace Kimi.AppKit.Core.Entities;

/// <summary>
/// 软删除实体。数据层会为实现了本接口的实体自动挂上全局查询过滤器。
/// </summary>
/// <remarks>
/// 【⚠️ 删除一律走 Remove()，禁止手写 Active = false】
/// 框架在 SaveChanges 时拦截 Deleted 状态并转成软删除。手写 <c>Active = false</c> 会绕过
/// 这层拦截，导致关联的级联处理、审计轨迹的「删除」语义全部丢失——数据看着是删了，
/// 审计表里却记成了一次普通更新。
///
/// 【⚠️ 参与唯一索引的列一律 NOT NULL + 空串哨兵】
/// PostgreSQL 与 SQL Server 对唯一索引中的 NULL 语义**相反**：SQL Server 视多个 NULL 为相同
/// （只允许一行），PostgreSQL 视为不同（允许多行）。软删除后留在表里的行会参与唯一索引，
/// 这个分歧因此会实打实地影响「删掉再建同名记录」能不能成功。
/// </remarks>
public interface ISoftDeleteEntity
{
    /// <summary>是否有效。false 表示已软删除。</summary>
    bool Active { get; set; }
}
