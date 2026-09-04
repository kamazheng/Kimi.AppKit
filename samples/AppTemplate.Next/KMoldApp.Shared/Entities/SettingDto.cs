using Kimi.AppKit.Core.Entities;
using System.ComponentModel.DataAnnotations;

namespace KMoldApp.Shared.Entities;

/// <summary>
/// 设置项的客户端契约。
/// </summary>
/// <remarks>
/// ⚠️ **必须实现 <see cref="IConcurrencyStamped"/>**，不能只声明一个同名属性。
/// 反射驱动的表格与表单靠这个接口把并发令牌从界面上排除掉——
/// 只有同名属性的话，那串 GUID 会原样显示给用户，还能被用户编辑
/// （改了就等于伪造「我基于哪个版本修改」，并发保护随之失效）。
/// </remarks>
/// <remarks>
/// 【为什么不直接把实体给 WASM】实体在 <c>KMoldApp.Data</c>，那个工程引用 EF Core
/// 与两个数据库驱动——把它暴露给客户端会把整条数据访问链拖进浏览器下载包。
/// 这里只放界面真正要显示与编辑的字段。
///
/// ⚠️ 字段名必须与实体一致：CRUD 端点按属性名做 JSON 绑定，
/// 改名会让该字段静默地不参与保存（既不报错也不生效）。
/// </remarks>
public sealed class SettingDto : IConcurrencyStamped
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>设置名。</summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>说明。</summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>系统内置设置，不可删除。</summary>
    public bool IsSystem { get; set; }

    /// <summary>设置值的类型全名。</summary>
    [StringLength(500)]
    public string ValueTypeFullName { get; set; } = string.Empty;

    /// <summary>设置值（JSON 字符串）。</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 并发令牌。⚠️ **编辑时必须原样带回**——它是「我基于哪个版本改的」这一事实的
    /// 唯一载体，丢掉它等于放弃并发保护（后写无感覆盖前写）。
    /// 由服务端换发，客户端不要生成也不要清空。
    /// </summary>
    public string? ConcurrencyStamp { get; set; }
}
