namespace Kimi.AppKit.Core.Abstractions;

/// <summary>
/// 用户偏好的持久化（列宽、折叠状态、上次选中的筛选项之类）。
/// </summary>
/// <remarks>
/// 【为什么要抽象】直接依赖浏览器 localStorage 会把组件绑死在「有浏览器」这个前提上，
/// 静态 SSR 期间没有 JS 运行时，调用会抛。实现方需要自己处理「现在还不能访问存储」的阶段。
///
/// ⚠️ 这里存的是**偏好，不是状态**。任何影响业务正确性的东西都不能放这儿——
/// 用户换台机器、换个浏览器、清一次缓存就没了。
/// </remarks>
public interface IKPreferenceStore
{
    /// <summary>读取。键不存在或反序列化失败时返回 default，**不抛**。</summary>
    ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>写入。</summary>
    ValueTask SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    /// <summary>删除。键不存在不算错。</summary>
    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);
}
