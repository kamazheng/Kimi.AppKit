namespace Kimi.AppKit.Core.Settings;

using System.Text.Json.Serialization;

/// <summary>
/// 一个强类型设置项。实现类的属性即设置的字段，实例即默认值。
/// </summary>
/// <remarks>
/// 【为什么不用 IOptions】<c>IOptions&lt;T&gt;</c> 的值来自启动时的配置源，运行期改不了。
/// 本套设置要支持「管理员在界面上改、立刻生效、落库持久化」，是不同的需求，两者并存。
/// 简单说：**部署方在启动前定的走 IOptions，运维在运行期改的走 ISetting。**
///
/// 【四层解析顺序】数据库 → 调用方传入的 defaultValue → <see cref="SettingDefaults"/> 注册的默认值
/// （并回写数据库）→ <c>default(T)</c>。
/// </remarks>
public interface ISetting
{
    /// <summary>给管理界面看的说明。<c>[JsonIgnore]</c> 使其不落库——它是代码里的文档，不是数据。</summary>
    [JsonIgnore]
    string Description { get; }
}
