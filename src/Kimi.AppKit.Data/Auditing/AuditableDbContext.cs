using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Data.Conventions;
using Microsoft.EntityFrameworkCore;

namespace Kimi.AppKit.Data.Auditing;

/// <summary>
/// 带审计轨迹与软删除的 <see cref="DbContext"/> 基类。
/// </summary>
/// <remarks>
/// 【它只是便利外壳】实现本体在 <see cref="AuditingBehavior"/>。
/// 本类的存在只为省掉「声明字段 + 重写 SaveChangesAsync + 挂约定」这三行样板。
///
/// ⚠️ **DbContext 的基类名额只有一个。**
/// 如果你的上下文必须继承别的基类（最常见的是 ASP.NET Core Identity 的
/// <c>IdentityDbContext</c>），**不要**试图绕开——直接改用组合：
/// <code>
/// public class MyDbContext : IdentityDbContext&lt;MyUser&gt;
/// {
///     private readonly AuditingBehavior _auditing;
///
///     protected override void ConfigureConventions(ModelConfigurationBuilder builder)
///     {
///         builder.ApplyAppKitConventions();
///         base.ConfigureConventions(builder);
///     }
///
///     public override Task&lt;int&gt; SaveChangesAsync(CancellationToken ct = default)
///         =&gt; _auditing.SaveChangesAsync(this, base.SaveChangesAsync, ct);
/// }
/// </code>
/// 两条路径的行为完全一致——本类自己走的就是下面这段。
///
/// 【与前身实现的三处关键差异】
///
/// **1. 两阶段保存并入同一个事务。** 前身让两次保存各自独立提交，
/// 于是业务数据落库成功而审计行插入失败时，得到一个静默的审计缺口。
///
/// **2. 当前用户经 <see cref="IKCurrentUser"/> 注入，而不是每次调用都手传。**
/// 前身要求 <c>SaveChangesAsync(userName)</c>，无参重载直接抛 <c>NotSupportedException</c>——
/// 一个只在**运行期**才暴露的约束。现在无参重载正常工作。
///
/// **3. 取用户名失败就抛，不退化成 "System"。**
/// 审计的全部价值在「谁干的」，把异常伪装成一次正常的系统操作，
/// 等于让审计表在最需要它的时候说谎。
/// </remarks>
public abstract class AuditableDbContext : DbContext
{
    private readonly AuditingBehavior _auditing;

    /// <summary>构造。</summary>
    protected AuditableDbContext(
        DbContextOptions options,
        IKCurrentUser currentUser,
        TimeProvider timeProvider)
        : base(options)
    {
        _auditing = new AuditingBehavior(currentUser, timeProvider);
    }

    /// <summary>审计轨迹。</summary>
    public DbSet<Trail> AuditTrails => Set<Trail>();

    /// <summary>
    /// 是否记录审计。派生类可覆盖以整体关闭（例如一次性的数据导入上下文）。
    /// </summary>
    /// <remarks>关掉的只是 <see cref="Trail"/> 行；软删除与审计字段照常应用。</remarks>
    protected virtual bool AuditingEnabled => true;

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.ApplyAppKitConventions();
        base.ConfigureConventions(builder);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _auditing.SaveChangesAsync(this, base.SaveChangesAsync, cancellationToken, AuditingEnabled);
}
