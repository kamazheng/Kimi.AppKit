using Kimi.AppKit.Core.Contracts;
using Microsoft.AspNetCore.Components;

namespace Kimi.AppKit.Components.Auth;

/// <summary>WASM 端的企业标识：读服务端随首屏送来的持久化状态。</summary>
/// <remarks>
/// ⚠️ 客户端**不去打身份服务**：那是跨域请求，且会让每个浏览器多一次网络往返。
/// ⚠️ 读不到时回落兜底值而不是空白——读不到是正常情况
/// （身份服务当时不可达，或本页是客户端路由跳转过来的、没有新的持久化状态）。
/// </remarks>
public sealed class KClientBranding(PersistentComponentState state) : IKBrandingSource
{
    private KBranding? _value;

    public ValueTask<KBranding> GetAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_value ??=
            state.TryTakeFromJson<KBranding>(nameof(KBranding), out var restored) && restored is not null
                ? restored
                : KBranding.Fallback);
}
