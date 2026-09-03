namespace Kimi.AppKit.Core.Settings;

/// <summary>
/// 设置项在数据库里的存储形状。
/// </summary>
/// <remarks>
/// 【为什么是接口，而不是包里直接给一个实体类】
/// 实体一旦进包，消费方的 <c>DbContext</c> 就必须包含包里的实体类型，
/// **两套迁移都要重新生成**，而且客户再也不能给设置表加自己的列
/// （加个 <c>Category</c> 分组、加个 <c>UpdatedByDepartment</c> 都不行）。
///
/// 改成接口之后：实体由消费方自己声明、自己迁移、想加什么列加什么列，
/// 包只要求它能提供下面这几个字段。
///
/// 【<see cref="Name"/> 与 <see cref="Description"/> 通常来自
/// <c>AuditableEntityWithName</c>】消费方直接继承那个基类再实现本接口即可，
/// 不用重复声明这两个属性。
/// </remarks>
public interface IKSettingEntity
{
    /// <summary>设置键。参与唯一索引，因此不可为 null（没填用空串）。</summary>
    string Name { get; set; }

    /// <summary>设置值的 JSON。</summary>
    string Value { get; set; }

    /// <summary>
    /// 值类型的全名（<c>Namespace.TypeName, AssemblyName</c>）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这个字段是给**管理界面**用的：界面要按类型渲染编辑器，才需要知道值是什么形状。
    /// 服务端读设置走的是 <c>GetAsync&lt;T&gt;</c> 的泛型参数，**不按这个字符串反射查类型**——
    /// 「按字符串找类型再实例化」是一条反射攻击面，前身的通用端点族正因此被整体删除。
    /// </remarks>
    string ValueTypeFullName { get; set; }

    /// <summary>给管理界面看的说明。</summary>
    string? Description { get; set; }

    /// <summary>系统项。不允许在界面上删除，只能改值。</summary>
    bool IsSystem { get; set; }
}
