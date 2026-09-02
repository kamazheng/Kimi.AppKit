using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kimi.AppKit.Data;

/// <summary>
/// <c>dotnet ef migrations add</c> 设计时工厂的基类。
/// </summary>
/// <typeparam name="TContext">数据上下文类型。</typeparam>
/// <remarks>
/// 【为什么需要它，而不是让 EF 直接从 Program.cs 构建 host】
/// 默认情况下 <c>dotnet ef</c> 会尝试构建应用的 <c>Program.cs</c> 里的 host 来找 DbContext——
/// 这意味着执行一条迁移命令会连带触发配置中心连接、外部服务探活等一整套启动逻辑。
/// 显式提供设计时工厂可以完全绕开这些，只为了生成迁移而已。
///
/// 【消费方的职责】派生类必须提供连接串与 provider 选择——这两样只有消费方知道，
/// 包本身不持有任何客户的连接信息。典型用法：
/// <code>
/// public sealed class MyDesignTimeFactory : DesignTimeDbContextFactoryBase&lt;MyDbContext&gt;
/// {
///     protected override MyDbContext CreateContext(DbContextOptions&lt;MyDbContext&gt; options)
///         => new(options, new DesignTimeCurrentUser(), TimeProvider.System);
///
///     protected override void ConfigureProvider(DbContextOptionsBuilder builder, string connectionString)
///         => builder.UseNpgsql(connectionString);
/// }
/// </code>
/// </remarks>
public abstract class DesignTimeDbContextFactoryBase<TContext> : IDesignTimeDbContextFactory<TContext>
    where TContext : DbContext
{
    /// <summary>
    /// 设计时使用的连接串。
    /// </summary>
    /// <remarks>
    /// ⚠️ 不要在这里读真实的生产连接串——设计时经常在开发者本机运行，
    /// 一个指向不存在实例的占位值（如 <c>Host=design-time-only</c>）比连上生产库更安全。
    /// 需要针对真实库生成迁移时，用环境变量临时覆盖。
    /// </remarks>
    protected abstract string GetConnectionString();

    /// <summary>把 <paramref name="builder"/> 配置到目标 provider。</summary>
    protected abstract void ConfigureProvider(DbContextOptionsBuilder<TContext> builder, string connectionString);

    /// <summary>用给定的 <see cref="DbContextOptions{TContext}"/> 构造上下文实例。</summary>
    protected abstract TContext CreateContext(DbContextOptions<TContext> options);

    /// <inheritdoc />
    public TContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        ConfigureProvider(builder, GetConnectionString());
        return CreateContext(builder.Options);
    }
}
