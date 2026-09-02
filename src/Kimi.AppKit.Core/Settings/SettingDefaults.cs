using System.Text.Json;

namespace Kimi.AppKit.Core.Settings;

/// <summary>
/// 设置项默认值的注册表。四层解析里的第三层——数据库和调用方都没给值时，从这里取，并回写数据库。
/// </summary>
/// <remarks>
/// 【与前身实现的差异】前身是一个静态类，默认值写死在类里的 <c>BuildDefaults()</c> 中。
/// 那样做的问题是：**默认值是应用的内容，不是框架的内容**。框架包里写死几个业务设置项，
/// 消费方既加不了自己的、也删不掉不需要的。
/// 现在框架只提供注册表本身，条目由消费方在启动时登记。
///
/// 【⚠️ 只能在启动期登记】<see cref="Register{T}"/> 只应在 DI 装配阶段调用。
/// 注册表内部用普通字典，**不是线程安全的**——运行期并发登记会撞
/// <c>InvalidOperationException</c> 或读到半个字典。
/// 这是刻意的取舍：为一个只在启动期写、之后全是读的结构付并发开销不划算。
/// </remarks>
public sealed class SettingDefaults
{
    private readonly Dictionary<string, SettingDefaultEntry> _entries = [];

    /// <summary>已登记的全部默认值。</summary>
    public IReadOnlyDictionary<string, SettingDefaultEntry> All => _entries;

    /// <summary>
    /// 登记一个设置项的默认值。<paramref name="defaultValue"/> 的实例即默认值，
    /// 它的 <see cref="ISetting.Description"/> 即界面上的说明。
    /// </summary>
    /// <param name="key">设置键。重复登记同一个键会覆盖前一次。</param>
    /// <param name="defaultValue">默认值实例。</param>
    /// <param name="isSystem">系统项。系统项不允许在界面上删除，只能改值。</param>
    /// <param name="jsonOptions">序列化选项。null 时用 <see cref="JsonSerializerOptions.Default"/>。</param>
    public SettingDefaults Register<T>(
        string key,
        T defaultValue,
        bool isSystem = true,
        JsonSerializerOptions? jsonOptions = null)
        where T : ISetting
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(defaultValue);

        _entries[key] = new SettingDefaultEntry(
            // ⚠️ 必须带程序集名：设置值以 JSON 落库，反序列化时靠它 Type.GetType 定位类型。
            // 只写类型全名的话，跨程序集的设置项在运行期会拿到 null 而**静默**回落到 default(T)。
            ValueTypeFullName: $"{typeof(T).FullName}, {typeof(T).Assembly.GetName().Name}",
            DefaultValueJson: JsonSerializer.Serialize(defaultValue, jsonOptions ?? JsonSerializerOptions.Default),
            Description: defaultValue.Description,
            IsSystem: isSystem);

        return this;
    }

    /// <summary>取某个键的默认值登记；未登记返回 null。</summary>
    public SettingDefaultEntry? Get(string key) => _entries.GetValueOrDefault(key);

    /// <summary>是否已登记。</summary>
    public bool Has(string key) => _entries.ContainsKey(key);
}
