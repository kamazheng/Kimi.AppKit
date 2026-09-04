using Kimi.AppKit.Data;
using Kimi.AppKit.Data.Modeling;
using Kimi.AppKit.Data.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Kimi.AppKit.Tests.Data;

/// <summary>
/// 属性链式配置——重点是 CHECK 约束的标识符引用（铁律 4）。
/// </summary>
public class KPropertyChainTests
{
    [Fact]
    public void PostgreSQL_的CHECK约束把列名用双引号包起来()
    {
        // ⚠️ 这条是本类存在的理由。PG 把未加引号的标识符**折叠成小写**，
        //    PascalCase 列名写成裸 Status 会被解析成 status，**建表直接失败**。
        //    前身实现是裸插值，没暴露只因为那个应用跑在 SQL Server 上。
        var sql = GetRangeConstraintSql(new PostgresDialect());

        Assert.Contains("\"Level\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Level >=", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlServer_的CHECK约束用方括号()
    {
        var sql = GetRangeConstraintSql(new SqlServerDialect());

        Assert.Contains("[Level]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void 范围上下界都写进约束()
    {
        var sql = GetRangeConstraintSql(new PostgresDialect());

        Assert.Contains(">= 1", sql, StringComparison.Ordinal);
        Assert.Contains("<= 5", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void 未提供方言时明确报错而不是悄悄拼出错误SQL()
    {
        // 早失败好过在 PG 上建表时才炸——那时错误信息指向的是 SQL 语法，不是这里。
        var ex = Assert.Throws<InvalidOperationException>(() => BuildModel(dialect: null));

        Assert.Contains("dialect", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 非整数属性用HasRange时报错()
    {
        using var context = new BadRangeContext();

        var ex = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("整数", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 链式可以连续配置多个属性()
    {
        using var context = new ChainContext();
        var entity = context.Model.FindEntityType(typeof(Gadget))!;

        Assert.Equal(32, entity.FindProperty(nameof(Gadget.Name))!.GetMaxLength());
        Assert.False(entity.FindProperty(nameof(Gadget.Name))!.IsNullable);
        Assert.Equal(64, entity.FindProperty(nameof(Gadget.Code))!.GetMaxLength());
    }

    [Fact]
    public void ToSchemaTable_默认用实体类型名做表名()
    {
        using var context = new ChainContext();
        var entity = context.Model.FindEntityType(typeof(Gadget))!;

        Assert.Equal(nameof(Gadget), entity.GetTableName());
        Assert.Equal(DbSchema.Reference, entity.GetSchema());
    }

    private static string GetRangeConstraintSql(IDbProviderDialect dialect)
    {
        using var context = BuildModel(dialect);

        // ⚠️ 不去读模型元数据里的 CheckConstraint——那东西不在运行时模型里
        //    （read-optimized model 会剥掉它）。直接看**生成的建表 DDL**，
        //    这也更接近真实行为：我们要保证的正是「PG 上这段 SQL 能建表成功」。
        return context.Database.GenerateCreateScript();
    }

    private static ChainContext BuildModel(IDbProviderDialect? dialect)
    {
        var context = new ChainContext(dialect, withRange: true);
        _ = context.Model; // 触发 OnModelCreating
        return context;
    }

    /// <summary>
    /// ⚠️ EF Core 默认按 <c>context 类型 + designTime</c> 缓存模型，**构造参数不参与缓存键**。
    /// 本测试类用同一个 context 类型配不同方言，不换缓存键的话第二个测试会静默拿到
    /// 第一个构建的模型——断言照样通过或失败，但测的根本不是它以为的那个模型。
    /// </summary>
    private sealed class PerDialectModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            (context.GetType(), ((ChainContext)context).CacheKey, designTime);
    }

    private sealed class Gadget
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public int Level { get; set; }
    }

    private sealed class ChainContext(IDbProviderDialect? dialect = null, bool withRange = false) : DbContext
    {
        internal string CacheKey => $"{dialect?.GetType().Name ?? "none"}:{withRange}";

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder
                .UseSqlite("DataSource=:memory:")
                .ReplaceService<IModelCacheKeyFactory, PerDialectModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var builder = modelBuilder.Entity<Gadget>();
            builder.ToSchemaTable(DbSchema.Reference);

            builder.KProperty(x => x.Name, dialect)
                .HasMaxLength(32)
                .IsRequired()
                .KProperty(x => x.Code)
                .HasMaxLength(64);

            if (withRange)
                builder.KProperty(x => x.Level, dialect).HasRange(1, 5);
        }
    }

    private sealed class BadRangeContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlite("DataSource=:memory:");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var builder = modelBuilder.Entity<Gadget>();
            builder.ToTable("Gadget");
            builder.KProperty(x => x.Name, new PostgresDialect()).HasRange(1, 5);
        }
    }
}
