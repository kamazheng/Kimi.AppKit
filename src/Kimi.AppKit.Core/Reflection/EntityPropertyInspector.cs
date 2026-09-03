using System.Globalization;
using System.Reflection;
using Kimi.AppKit.Core.Entities;

namespace Kimi.AppKit.Core.Reflection;

/// <summary>
/// 从实体类型反射出"可编辑标量属性"——反射驱动的表格列/表单字段（<c>KEntityTable</c>/
/// <c>KEntityForm</c>）与通用搜索/筛选（<c>ReflectiveCrudDataSource</c>）共用同一份规则，
/// 避免两处各自维护一套"哪些属性算数据库字段"的判断。
/// </summary>
public static class EntityPropertyInspector
{
    /// <summary>
    /// 获取可编辑的标量属性：公开读写、非索引器、非集合、未贴 <see cref="HideFromTableAttribute"/>，
    /// 排除指定接口自带的属性（如 <c>ISoftDeleteEntity.Active</c>、审计字段）。
    /// </summary>
    /// <remarks>
    /// 排序固定为 <c>Id</c> → <c>Name</c> → <c>Description</c> → 其余按声明顺序——
    /// 这三个字段几乎是所有实体的"看一眼就知道是哪一条"的关键信息，理应排在表格/表单最前面。
    /// </remarks>
    public static IReadOnlyList<PropertyInfo> GetEditableProperties(
        Type entityType, IEnumerable<Type>? excludeInterfaces = null)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        var excluded = BuildExcludedPropertyNames(entityType, excludeInterfaces);

        return entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !excluded.Contains(p.Name))
            .Where(IsEditableScalar)
            .Where(p => p.GetCustomAttribute<HideFromTableAttribute>() is null)
            .OrderBy(p => PriorityOf(p.Name))
            .ToArray();
    }

    private static bool IsEditableScalar(PropertyInfo property)
    {
        if (property.GetIndexParameters().Length > 0) return false;
        if (property.GetGetMethod(nonPublic: false) is null) return false;
        if (property.GetSetMethod(nonPublic: false) is null) return false;

        var type = property.PropertyType;
        if (type == typeof(string)) return true;
        if (!type.IsClass) return true; // 值类型/枚举/可空值类型

        return false; // 引用类型（string 除外）视为导航属性/集合，不进表格/表单
    }

    private static HashSet<string> BuildExcludedPropertyNames(Type entityType, IEnumerable<Type>? excludeInterfaces)
    {
        var names = new HashSet<string>();
        foreach (var iface in excludeInterfaces ?? [])
        {
            if (!iface.IsInterface || !iface.IsAssignableFrom(entityType)) continue;
            foreach (var p in iface.GetProperties()) names.Add(p.Name);
        }
        return names;
    }

    private static int PriorityOf(string propertyName) => propertyName switch
    {
        "Id" => 0,
        "Name" => 1,
        "Description" => 2,
        _ => 100,
    };

    /// <summary>
    /// 构造一个「按此属性取可排序值」的委托，供反射驱动的表格做客户端排序。
    /// </summary>
    /// <remarks>
    /// ⚠️ **必须按类型归一到可比较的值**，不能一律 <c>ToString()</c>：
    /// 数字按字符串排会得到 1 &lt; 10 &lt; 2，日期按字符串排在非 ISO 格式下同样错乱。
    /// 这类排序错误不会报错，只是顺序不对——用户往往以为是数据问题而不是排序问题。
    /// </remarks>
    public static Func<object, object> BuildSortSelector(this PropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(property);

        var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        return Type.GetTypeCode(underlying) switch
        {
            TypeCode.DateTime => x => Convert.ToDateTime(property.GetValue(x) ?? DateTime.MinValue, CultureInfo.InvariantCulture),

            TypeCode.Decimal or TypeCode.Double or TypeCode.Single
                or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
                or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
                or TypeCode.Byte or TypeCode.SByte =>
                x => Convert.ToDouble(property.GetValue(x) ?? 0, CultureInfo.InvariantCulture),

            // 字符串、枚举、Guid 等按字符串排；枚举按名称排是有意为之——
            // 按底层数值排对用户没有意义（他看到的是名称）。
            _ => x => property.GetValue(x)?.ToString() ?? string.Empty,
        };
    }
}
