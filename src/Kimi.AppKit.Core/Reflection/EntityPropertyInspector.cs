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
}
