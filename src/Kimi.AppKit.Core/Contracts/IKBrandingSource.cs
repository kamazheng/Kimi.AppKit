namespace Kimi.AppKit.Core.Contracts;

/// <summary>
/// 企业标识的读取口径。**两端各有一个实现**。
/// </summary>
/// <remarks>
/// ⚠️ 必须是接口而不是具体类：顶栏组件在 Client 程序集里，但它会被
/// **服务端预渲染**一次、再由 WASM 接管一次，两次用的是**两个不同的容器**。
/// 只在客户端注册的话，服务端预渲染时直接抛
/// 「Cannot provide a value for property … There is no registered service」——
/// 整页 500，而错误信息指向组件属性注入、不指向注册缺失。
///
/// - 服务端实现：直接向身份服务拉（KBrandingClient），并把值持久化给客户端
/// - 客户端实现：读服务端随首屏送来的持久化状态，不再发一次跨域请求
/// </remarks>
public interface IKBrandingSource
{
    /// <summary>取企业标识。永不抛，拿不到就回兜底值。</summary>
    ValueTask<KBranding> GetAsync(CancellationToken cancellationToken = default);
}
