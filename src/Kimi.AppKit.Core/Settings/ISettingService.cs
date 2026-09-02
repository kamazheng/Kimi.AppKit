namespace Kimi.AppKit.Core.Settings;

/// <summary>
/// 设置项的读写。实现在 <c>Kimi.AppKit.Web</c>（需要数据库）。
/// </summary>
public interface ISettingService
{
    /// <summary>
    /// 按四层顺序解析设置值：数据库 → <paramref name="defaultValue"/> →
    /// <see cref="SettingDefaults"/> 注册的默认值（并回写数据库）→ <c>default(T)</c>。
    /// </summary>
    Task<T?> GetAsync<T>(string key, T? defaultValue = default, CancellationToken cancellationToken = default)
        where T : ISetting;

    /// <summary>写入并持久化。</summary>
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        where T : ISetting;

    /// <summary>是否已在数据库里存过值。</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>删除数据库里的值，下次读取回落到注册的默认值。</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>重置为 <see cref="SettingDefaults"/> 注册的默认值。</summary>
    Task ResetToDefaultAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>取原始 JSON，供管理界面直接编辑。</summary>
    Task<string?> GetRawJsonAsync(string key, CancellationToken cancellationToken = default);
}
