using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Data;
using Kimi.AppKit.Data.Auditing;
using Kimi.AppKit.Data.Conventions;
using Kimi.AppKit.Data.Modeling;
using Kimi.AppKit.Data.Providers;
using KMoldApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KMoldApp.Data;

/// <summary>
/// 应用的数据上下文，继承 <see cref="AuditableDbContext"/>（审计轨迹 + 软删除）。
/// </summary>
/// <param name="options">上下文选项。</param>
/// <param name="currentUser">当前用户，审计字段记录到它返回的用户名。</param>
/// <param name="timeProvider">时间源，审计字段用它取时间。</param>
/// <remarks>
/// 【⚠️ 无参 <c>SaveChangesAsync</c> 就是正确用法】当前用户由 <see cref="IKCurrentUser"/> 注入，
/// **不要手传用户名**。前身的 <c>AuditTrailDbContext</c> 恰好相反——无参重载会抛
/// <c>NotSupportedException</c>，而手传那套正是审计表说谎的来源（取不到用户名时退化成 "System"）。
///
/// 【⚠️ 软删除】所有 <c>ISoftDeleteEntity</c> 由框架自动软删并全局过滤，
/// 业务代码只用 <c>Remove()</c>，**不要手写 <c>Active = false</c>**——那会绕过拦截，
/// 审计里记成一次普通更新而不是删除。
/// </remarks>
public class KMoldDbContext(
    DbContextOptions<KMoldDbContext> options,
    IKCurrentUser currentUser,
    TimeProvider timeProvider)
    : AuditableDbContext(options, currentUser, timeProvider)
{
    /// <summary>设置项。</summary>
    public DbSet<Setting> Settings => Set<Setting>();

    /// <summary>邮件模板。</summary>
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

    // 业务实体的 DbSet 加在此处。

    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ 必须调 <c>base</c>：基类在那里挂 <c>ApplyAppKitConventions()</c>，
    /// 也就是 <c>DateTimeOffset</c> 的 UTC 归一。漏掉它，带本地偏移的时间写进
    /// PostgreSQL 会抛 <c>ArgumentException: only offset 0 (UTC) is supported</c>，
    /// 而 SQL Server 照单全收——同一段代码换 provider 才崩。
    ///
    /// 【⚠️ 刻意**不设**全局 string 长度 / decimal 精度约定】
    /// 旧模板在这里写了 <c>Properties&lt;string&gt;().HaveMaxLength(100)</c> 一族约定。
    /// 那会连**框架自己的表**一起套住——审计表 <c>Trail</c> 的 <c>OldValues</c> /
    /// <c>NewValues</c> 存的是整行变更的 JSON，长度不可预估，被限制成
    /// <c>varchar(100)</c> 之后**每一次带审计的写操作都会失败**：
    /// PG 报 <c>22001: value too long</c>，SQL Server 报「数据将被截断」。
    /// 更糟的是错误指向审计表，而不是用户正在保存的那条业务数据。
    ///
    /// AppKit 生态里两个已投产的消费方（<c>Kimi.KMold.Auth</c> / <c>Kimi.KMold.Files</c>）
    /// 的 <c>ConfigureConventions</c> 里**都只有 <c>ApplyAppKitConventions()</c>**，
    /// 没有任何全局长度约定——字段长度由各实体自己用 <c>[StringLength]</c> 或
    /// Fluent 声明，那才是它该在的位置。
    ///
    /// ⚠️ 枚举转字符串也不在这里配：那件事连同 CHECK 约束一起由
    /// <c>ApplyEnumStringConstraints</c> 负责（见 <see cref="OnModelCreating"/>）。
    /// </remarks>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        base.ConfigureConventions(configurationBuilder);

    /// <inheritdoc />
    /// <remarks>
    /// ⚠️ 四个约定缺一不可，且都由 <c>Kimi.AppKit.Data</c> 提供，不要在这里重新实现：
    /// 并发令牌、字符串枚举 CHECK 约束、Name 条件唯一索引、软删除全局查询过滤器。
    ///
    /// ⚠️ **改枚举成员后必须生成迁移**：CHECK 约束把取值范围焊进了数据库层，
    /// 忘了迁移就会在写入新成员时撞约束，而那个错误完全指不到这里。
    ///
    /// ⚠️ <c>ApplySoftDeleteFilter</c> **必须放在最后**：它遍历模型里已发现的实体，
    /// 放在实体注册之前会漏掉后注册的实体，**且不报错**。
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Setting>().ToSchemaTable(DbSchema.Reference);
        modelBuilder.Entity<EmailTemplate>().ToSchemaTable(DbSchema.Reference);

        // 业务实体的 Fluent 映射加在此处。

        modelBuilder.ApplyConcurrencyTokens(Database);
        modelBuilder.ApplyEnumStringConstraints(Database.Dialect());
        modelBuilder.ApplyUniqueNameConstraint(Database);
        modelBuilder.ApplySoftDeleteFilter();
    }

}
