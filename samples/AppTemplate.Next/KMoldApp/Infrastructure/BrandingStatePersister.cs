using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Web.Branding;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KMoldApp.Infrastructure;

/// <summary>
/// 把从身份服务拉到的企业标识经 <see cref="PersistentComponentState"/> 送给 WASM 端。
/// </summary>
/// <remarks>
/// 【为什么需要这一层】顶栏跑在 **WASM 客户端**——那是另一个进程，
/// 既没有服务端的 <c>HttpClient</c>，也不能直接打身份服务（跨域）。
/// 服务端在预渲染时把值取好、随首屏一起送过去，客户端接管后直接读，
/// 既不多一次网络往返，也不闪一下默认值再变成真值。
///
/// ⚠️ 持久化回调**必须能容错**。它跑在渲染管线里，抛异常会让整页渲染失败——
/// 而这里要的只是一个企业名。<see cref="KBrandingClient"/> 本身永不抛，
/// 这里再兜一层是防它将来被改坏。
/// </remarks>
public sealed class BrandingStatePersister : IDisposable
{
    /// <summary>持久化键。⚠️ 与客户端读取处必须一致，写错不会报错，只是永远读不到。</summary>
    public const string StateKey = nameof(KBranding);

    private readonly PersistentComponentState _state;
    private readonly KBrandingClient _client;
    private readonly PersistingComponentStateSubscription _subscription;

    public BrandingStatePersister(PersistentComponentState state, KBrandingClient client)
    {
        _state = state;
        _client = client;
        _subscription = state.RegisterOnPersisting(PersistAsync, RenderMode.InteractiveWebAssembly);
    }

    private async Task PersistAsync()
    {
        try
        {
            _state.PersistAsJson(StateKey, await _client.GetAsync().ConfigureAwait(false));
        }
        catch
        {
            // 拿不到就不持久化，客户端自己回落兜底值。
            // 品牌是装饰，绝不能因为它让整页渲染失败。
        }
    }

    public void Dispose() => _subscription.Dispose();
}
