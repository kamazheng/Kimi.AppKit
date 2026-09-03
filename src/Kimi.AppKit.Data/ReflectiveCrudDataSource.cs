using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Core.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Kimi.AppKit.Data;

/// <summary>
/// <see cref="EfCrudDataSource{TContext,TEntity}"/> 的反射增强版：应用
/// <see cref="KQuery.Search"/>/<see cref="KQuery.Filters"/>。给不知道具体实体类型的通用场景用
/// （<c>KEntityTable</c>/<c>KEntityCrudPage&lt;T&gt;</c>）——登记了自己实体的调用方仍应优先
/// 直接覆写 <see cref="EfCrudDataSource{TContext,TEntity}.Filter"/>，那样能拿到强类型的属性访问，
/// 不必依赖反射。
/// </summary>
/// <remarks>
/// 【为什么是表达式树，不是 Dynamic LINQ】P2 已经量化过这个决策：MES 里
/// <c>DynamicQuery</c>/原始 SQL 拼接 0 使用，判定不抽，注入面直接消失。
/// 本类反射拿到的是 <see cref="PropertyInfo"/>，用它构造表达式树再交给 EF 翻译成 SQL，
/// 客户端传入的只有属性名（需先在白名单校验）与筛选值（转换失败静默忽略），
/// 任何时候都不会有原始字符串被当作查询语法解析。
///
/// 【⚠️ 大小写语义因 provider 而异，本类不做归一化】<c>string.Contains</c> 在 SQL Server
/// 默认不区分大小写（CI collation），PostgreSQL 默认区分大小写——同一句查询在两个 provider
/// 上可能给出不同结果。需要跨 provider 一致的不区分大小写搜索时，
/// 由具体实体的 <see cref="EfCrudDataSource{TContext,TEntity}.Filter"/> 覆写实现
/// （PG 走 <c>ILike</c>/<c>citext</c>，SQL Server 靠默认 CI collation），本类只服务
/// "不知道具体实体、按 provider 默认语义搜就够用"的通用场景。
/// </remarks>
public class ReflectiveCrudDataSource<TContext, TEntity>(IDbContextFactory<TContext> contextFactory)
    : EfCrudDataSource<TContext, TEntity>(contextFactory)
    where TContext : DbContext
    where TEntity : class
{
    private static readonly MethodInfo StringContains =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    /// <inheritdoc />
    protected override IQueryable<TEntity> Filter(IQueryable<TEntity> source, KQuery query)
    {
        source = ApplySearch(source, query.Search);
        source = ApplyFilters(source, query.Filters);
        return source;
    }

    private static IQueryable<TEntity> ApplySearch(IQueryable<TEntity> source, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return source;

        var stringProperties = EntityPropertyInspector.GetEditableProperties(typeof(TEntity))
            .Where(p => p.PropertyType == typeof(string))
            .ToArray();
        if (stringProperties.Length == 0) return source;

        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var searchConstant = Expression.Constant(search);

        Expression? predicate = null;
        foreach (var property in stringProperties)
        {
            var propertyAccess = Expression.Property(parameter, property);
            var notNull = Expression.NotEqual(propertyAccess, Expression.Constant(null, typeof(string)));
            var contains = Expression.Call(propertyAccess, StringContains, searchConstant);
            var clause = Expression.AndAlso(notNull, contains);
            predicate = predicate is null ? clause : Expression.OrElse(predicate, clause);
        }

        var lambda = Expression.Lambda<Func<TEntity, bool>>(predicate!, parameter);
        return source.Where(lambda);
    }

    private static IQueryable<TEntity> ApplyFilters(
        IQueryable<TEntity> source, IReadOnlyDictionary<string, string?> filters)
    {
        if (filters.Count == 0) return source;

        // 白名单：只接受"可编辑标量属性"里存在的字段名，其余静默忽略——
        // 这是通用查询入口天然缺少挂授权位置的补偿，客户端传入的字段名不能直接信任。
        var whitelisted = EntityPropertyInspector.GetEditableProperties(typeof(TEntity))
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, rawValue) in filters)
        {
            if (!whitelisted.TryGetValue(key, out var property)) continue;
            if (!TryConvertFilterValue(rawValue, property.PropertyType, out var value)) continue;

            var parameter = Expression.Parameter(typeof(TEntity), "x");
            var propertyAccess = Expression.Property(parameter, property);
            var equals = Expression.Equal(propertyAccess, Expression.Constant(value, property.PropertyType));
            var lambda = Expression.Lambda<Func<TEntity, bool>>(equals, parameter);
            source = source.Where(lambda);
        }

        return source;
    }

    /// <summary>
    /// 把筛选值（原始字符串）转换成属性的实际 CLR 类型。转换失败返回 <c>false</c>——
    /// 一条脏筛选值不该让整个查询报错，静默丢弃这一条筛选条件即可。
    /// </summary>
    private static bool TryConvertFilterValue(string? raw, Type propertyType, out object? converted)
    {
        converted = null;
        if (raw is null) return false; // 空值不构成筛选条件

        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        try
        {
            if (targetType == typeof(string)) { converted = raw; return true; }
            if (targetType.IsEnum) { converted = Enum.Parse(targetType, raw, ignoreCase: true); return true; }
            if (targetType == typeof(Guid)) { converted = Guid.Parse(raw); return true; }

            converted = Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return false;
        }
    }
}
