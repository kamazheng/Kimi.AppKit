using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Web.Branding;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Kimi.AppKit.Web.Branding;

/// <summary>
/// 服务端的企业标识：向身份服务拉取，并把结果随首屏送给 WASM 端。
/// </summary>
/// <remarks>
/// ⚠️ 服务端预渲染时必须拿到**真值**，否则顶栏会先显示兜底企业名、
/// 等 WASM 接管后再变成真名——用户看到的是名字闪一下，像页面出错了。
///
/// ⚠️ **持久化和取值必须是同一个类。** 早先拆成两个类时，
/// 那个只管持久化的类**没有任何组件依赖它**，于是 DI 里注册了却从未被解析，
/// <c>RegisterOnPersisting</c> 一次都没跑——表现是登录页品牌正确、顶栏永远是兜底值，
/// 而两边代码看起来都对、也没有任何报错。
/// 合并之后它由顶栏注入 <see cref="IKBrandingSource"/> 时一并激活。
/// </remarks>
public sealed class KServerBranding : IKBrandingSource, IDisposable
{
    private readonly KBrandingClient _client;
    private readonly PersistentComponentState _state;
    private readonly PersistingComponentStateSubscription _subscription;

    private KBranding? _value;

    public KServerBranding(KBrandingClient client, PersistentComponentState state)
    {
        _client = client;
        _state = state;
        _subscription = state.RegisterOnPersisting(PersistAsync, RenderMode.InteractiveWebAssembly);
    }

    public async ValueTask<KBranding> GetAsync(CancellationToken cancellationToken = default) =>
        _value ??= await _client.GetAsync(cancellationToken).ConfigureAwait(false);

    /// <remarks>
    /// ⚠️ 回调跑在渲染管线里，抛异常会让**整页渲染失败**——而这里要的只是一个企业名。
    /// <see cref="KBrandingClient"/> 本身永不抛，这里再兜一层是防它将来被改坏。
    /// </remarks>
    private async Task PersistAsync()
    {
        try
        {
            _state.PersistAsJson(nameof(KBranding), await GetAsync().ConfigureAwait(false));
        }
        catch
        {
            // 拿不到就不持久化，客户端自己回落兜底值。品牌是装饰，不能让页面挂掉。
        }
    }

    public void Dispose() => _subscription.Dispose();
}
