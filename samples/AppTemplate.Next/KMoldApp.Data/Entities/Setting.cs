using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Core.Settings;

namespace KMoldApp.Data.Entities;

/// <summary>
/// 设置项的存储实体。
/// </summary>
/// <remarks>
/// ⚠️ 这个实体**刻意留在应用里**，没有跟着 <c>ISettingService</c> 一起进包。
/// 实体进包意味着：两套迁移都要跟着包版本重新生成，而且再也不能给设置表加自己的列
/// （分组、部门、生效时间……）。包只要求它实现 <see cref="IKSettingEntity"/>。
/// 要加列，直接在这里加，再生成两套迁移即可。
/// </remarks>
public class Setting : AuditableEntityWithName, IKSettingEntity
{
    /// <summary>系统内置设置。系统设置不可删除，编辑也受限。</summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// 设置值的类型全名，格式为 <c>Namespace.TypeName, AssemblyName</c>。
    /// </summary>
    /// <remarks>
    /// ⚠️ 该类型必须定义在 <c>KMoldApp.Shared</c> 或客户端工程里——服务端类型在前端引用不到。
    /// 且必须能被 <c>System.Text.Json</c> 序列化与反序列化。
    /// </remarks>
    public string ValueTypeFullName { get; set; } = null!;

    /// <summary>设置值，以 JSON 字符串存储。</summary>
    public string Value { get; set; } = default!;
}
