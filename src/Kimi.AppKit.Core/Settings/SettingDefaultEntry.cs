namespace Kimi.AppKit.Core.Settings;

/// <summary>
/// 设置项的默认值登记条目。
/// </summary>
/// <param name="ValueTypeFullName">
/// 值类型的「类型全名, 程序集名」。
/// ⚠️ 必须带程序集名：设置值以 JSON 落库，反序列化时要靠它定位类型。
/// 只写类型全名的话，跨程序集的设置项在运行期会 <c>Type.GetType</c> 返回 null 而静默失败。
/// </param>
/// <param name="DefaultValueJson">默认值的 JSON。</param>
/// <param name="Description">说明。</param>
/// <param name="IsSystem">系统项。系统项不允许在界面上删除，只能改值。</param>
public sealed record SettingDefaultEntry(
    string ValueTypeFullName,
    string DefaultValueJson,
    string Description,
    bool IsSystem = true);
