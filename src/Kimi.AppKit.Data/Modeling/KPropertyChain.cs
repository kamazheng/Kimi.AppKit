using Kimi.AppKit.Data.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Linq.Expressions;
using System.Text.Json;

namespace Kimi.AppKit.Data.Modeling;

/// <summary>
/// 在同一个 <see cref="EntityTypeBuilder{TEntity}"/> 上连续配置多个属性的链式包装。
/// </summary>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <typeparam name="TProperty">当前属性类型。</typeparam>
/// <remarks>
/// 【解决什么】EF Core 原生写法每配一个属性都要重复 <c>builder.Property(x => x.Foo)</c>，
/// 实体一大就是几十行同样的前缀。本类让配置可以顺着链往下写。
///
/// ⚠️ <see cref="HasRange"/> 生成 CHECK 约束需要方言（标识符引号规则因 provider 而异），
/// 用它就必须在 <see cref="KModelingExtensions.KProperty"/> 时传入 <c>dialect</c>。
/// </remarks>
public sealed class KPropertyChain<TEntity, TProperty>
    where TEntity : class
{
    private readonly IDbProviderDialect? _dialect;

    internal KPropertyChain(
        EntityTypeBuilder<TEntity> entityBuilder,
        PropertyBuilder<TProperty> propertyBuilder,
        IDbProviderDialect? dialect)
    {
        EntityBuilder = entityBuilder;
        PropertyBuilder = propertyBuilder;
        _dialect = dialect;
    }

    /// <summary>当前实体的 builder（保持链上下文）。</summary>
    public EntityTypeBuilder<TEntity> EntityBuilder { get; }

    /// <summary>当前属性的 builder。</summary>
    public PropertyBuilder<TProperty> PropertyBuilder { get; }

    /// <summary>跳到下一个属性，返回新的链上下文。方言沿链传递。</summary>
    public KPropertyChain<TEntity, TNext> KProperty<TNext>(
        Expression<Func<TEntity, TNext>> nextProperty)
    {
        ArgumentNullException.ThrowIfNull(nextProperty);
        return new KPropertyChain<TEntity, TNext>(
            EntityBuilder, EntityBuilder.Property(nextProperty), _dialect);
    }

    /// <summary>结束链式，返回实体 builder。</summary>
    public EntityTypeBuilder<TEntity> End() => EntityBuilder;

    /// <inheritdoc cref="PropertyBuilder.HasMaxLength"/>
    public KPropertyChain<TEntity, TProperty> HasMaxLength(int maxLength)
    {
        PropertyBuilder.HasMaxLength(maxLength);
        return this;
    }

    /// <inheritdoc cref="PropertyBuilder.IsRequired"/>
    public KPropertyChain<TEntity, TProperty> IsRequired(bool required = true)
    {
        PropertyBuilder.IsRequired(required);
        return this;
    }

    /// <summary>指定列的数据库类型。⚠️ 写死类型名会绑定到具体 provider。</summary>
    public KPropertyChain<TEntity, TProperty> HasColumnType(string columnType)
    {
        PropertyBuilder.HasColumnType(columnType);
        return this;
    }

    /// <summary>设置列的数据库默认值（写入 DDL，不是 CLR 侧默认值）。</summary>
    public KPropertyChain<TEntity, TProperty> HasDefaultValue(object? value)
    {
        PropertyBuilder.HasDefaultValue(value);
        return this;
    }

    /// <inheritdoc cref="PropertyBuilder.HasPrecision(int, int)"/>
    public KPropertyChain<TEntity, TProperty> HasPrecision(int precision, int scale = 0)
    {
        PropertyBuilder.HasPrecision(precision, scale);
        return this;
    }

    /// <inheritdoc cref="KJsonConversionExtensions.HasJsonConversion"/>
    public KPropertyChain<TEntity, TProperty> HasJsonConversion(JsonSerializerOptions? options = null)
    {
        PropertyBuilder.HasJsonConversion(options);
        return this;
    }

    /// <summary>
    /// 给整数属性加范围检查，生成表级 CHECK 约束。
    /// </summary>
    /// <param name="minValue">最小值（含）。</param>
    /// <param name="maxValue">最大值（含）。</param>
    /// <param name="allowNull">属性可空时，是否允许 NULL 通过检查。</param>
    /// <param name="constraintName">约束名。默认 <c>CK_{表}_{列}_Range</c>。</param>
    /// <exception cref="InvalidOperationException">
    /// 未提供方言，或属性不是整数类型，或实体未映射到表。
    /// </exception>
    /// <remarks>
    /// ⚠️ **列名必须经 <see cref="IDbProviderDialect.Quote"/>**，不能裸插值。
    /// PostgreSQL 把未加引号的标识符**折叠成小写**，于是 PascalCase 列名
    /// （如 <c>Status</c>）在 CHECK 表达式里写成裸 <c>Status</c> 会被解析成
    /// <c>status</c>，**建表直接失败**。
    /// 前身实现正是裸插值，它没暴露只是因为那个应用跑在 SQL Server 上
    /// ——SQL Server 对裸标识符宽容。这是典型的「号称支持双 provider、
    /// 实际只在一个上验证过」。
    /// </remarks>
    public KPropertyChain<TEntity, TProperty> HasRange(
        long minValue, long maxValue, bool allowNull = false, string? constraintName = null)
    {
        if (minValue > maxValue)
            throw new ArgumentException("minValue 不能大于 maxValue。", nameof(minValue));

        if (_dialect is null)
        {
            throw new InvalidOperationException(
                $"{nameof(HasRange)} 需要方言来正确引用标识符，请在 KProperty(...) 时传入 dialect（" +
                $"用 {nameof(ProviderDetection)}.GetDialect(Database) 取得）。");
        }

        var underlying = Nullable.GetUnderlyingType(typeof(TProperty)) ?? typeof(TProperty);
        if (!IsIntegerType(underlying))
        {
            throw new InvalidOperationException(
                $"{nameof(HasRange)} 只适用于整数类型，当前属性类型为 {typeof(TProperty).Name}。");
        }

        var entityType = EntityBuilder.Metadata;
        var tableId = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
            ?? throw new InvalidOperationException(
                $"实体 {entityType.DisplayName()} 未映射到具体表，无法创建 CHECK 约束。");

        var columnName = PropertyBuilder.Metadata.GetColumnName(tableId) ?? PropertyBuilder.Metadata.Name;
        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"实体 {entityType.DisplayName()} 未解析到表名。");

        var column = _dialect.Quote(columnName);
        var name = constraintName ?? $"CK_{tableName}_{columnName}_Range";

        var inRange = $"{column} >= {minValue} AND {column} <= {maxValue}";
        var predicate = PropertyBuilder.Metadata.IsNullable && allowNull
            ? $"({column} IS NULL OR ({inRange}))"
            : inRange;

        EntityBuilder.ToTable(t => t.HasCheckConstraint(name, predicate));
        return this;
    }

    private static bool IsIntegerType(Type type) =>
        type == typeof(byte) || type == typeof(sbyte)
        || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(ulong);
}

/// <summary>建模辅助扩展。</summary>
public static class KModelingExtensions
{
    /// <summary>
    /// 把实体映射到指定 schema 下的表，表名默认取实体类型名。
    /// </summary>
    /// <typeparam name="TEntity">实体类型。</typeparam>
    /// <param name="builder">实体 builder。</param>
    /// <param name="schema">schema 名，用 <see cref="DbSchema"/> 里的常量。</param>
    /// <param name="tableName">表名。默认 <c>typeof(TEntity).Name</c>。</param>
    /// <remarks>
    /// 【为什么值得有】EF Core 的 <c>ToTable(name, schema)</c> 要求显式给表名，
    /// 于是每个实体都要重复写一遍自己的类名。而「表名等于实体名」正是 EF Core
    /// 不调 <c>ToTable</c> 时的默认约定——只是一旦要指定 schema 就得把表名也一起写死，
    /// 类改名时那个字符串不会有编译错误。
    ///
    /// ⚠️ 显式传 <paramref name="tableName"/> 时就恢复了「改名不报错」的风险，
    /// 只在表名确实不等于类名时才传。
    /// </remarks>
    public static EntityTypeBuilder<TEntity> ToSchemaTable<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string schema,
        string? tableName = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        return builder.ToTable(tableName ?? typeof(TEntity).Name, schema);
    }

    /// <summary>
    /// 开始链式配置属性。
    /// </summary>
    /// <param name="builder">实体 builder。</param>
    /// <param name="property">属性表达式。</param>
    /// <param name="dialect">
    /// 方言。只有用到 <see cref="KPropertyChain{TEntity,TProperty}.HasRange"/>
    /// （生成 CHECK 约束）时才必需，其余配置不需要。
    /// </param>
    public static KPropertyChain<TEntity, TProperty> KProperty<TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TProperty>> property,
        IDbProviderDialect? dialect = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(property);

        return new KPropertyChain<TEntity, TProperty>(builder, builder.Property(property), dialect);
    }
}
