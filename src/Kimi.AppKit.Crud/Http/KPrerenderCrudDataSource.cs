using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Kimi.AppKit.Crud.Http;

/// <summary>
/// 服务端**预渲染阶段**用的空数据源。
/// </summary>
/// <typeparam name="T">实体类型。</typeparam>
/// <remarks>
/// 【为什么必须有】WASM 页面注入的 <see cref="ICrudDataSource{T}"/> 只注册在客户端容器里，
/// 但**服务端在预渲染这个页面时也要实例化它**——即使页面设了 <c>prerender: false</c>。
/// 少了服务端这份注册，整页 500：
/// <c>Cannot provide a value for property 'DataSource' ... There is no registered service</c>。
///
/// 【为什么返回空而不是真去取数】预渲染跑在服务端，**拿不到浏览器的认证 Cookie**，
/// 自己调自己的 API 只会得到 401；直连数据库又会绕过端点上的授权。
/// 与其产出一份「看起来有数据、其实来源可疑」的 HTML，不如明确空着——
/// WASM 接管后会立刻用真实身份重新加载。
///
/// ⚠️ 代价是首屏表格会短暂空白。若要消除这个闪烁，正确做法是给页面做骨架屏，
/// 而不是让预渲染去取数据。
/// </remarks>
public sealed class KPrerenderCrudDataSource<T> : ICrudDataSource<T>
{
    /// <inheritdoc />
    public Task<KPage<T>> LoadAsync(KQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Task.FromResult(KPage<T>.Empty(query));
    }

    /// <inheritdoc />
    public Task<T?> GetAsync(object id, CancellationToken cancellationToken = default) =>
        Task.FromResult<T?>(default);

    /// <inheritdoc />
    /// <remarks>⚠️ 预渲染阶段不应发生写操作，直接失败而不是假装成功。</remarks>
    public Task<KResult> UpsertAsync(T item, CancellationToken cancellationToken = default) =>
        Task.FromResult(KResult.Fail("预渲染阶段不支持写操作。"));

    /// <inheritdoc />
    /// <remarks>⚠️ 同上。</remarks>
    public Task<KResult> DeleteAsync(object id, CancellationToken cancellationToken = default) =>
        Task.FromResult(KResult.Fail("预渲染阶段不支持写操作。"));
}

/// <summary>预渲染数据源的注册入口。</summary>
public static class KPrerenderCrudServiceCollectionExtensions
{
    /// <summary>
    /// 在**服务端**注册 <typeparamref name="T"/> 的预渲染空数据源。
    /// </summary>
    /// <remarks>
    /// ⚠️ 与客户端的 <c>AddHttpCrudDataSource&lt;T&gt;()</c> **成对使用**：
    /// 客户端那份负责真正取数，本份只保证预渲染阶段不因缺服务而整页 500。
    /// 漏掉任何一份的表现都是 500，且错误信息指向组件的属性注入、不指向注册。
    /// </remarks>
    public static IServiceCollection AddPrerenderCrudDataSource<T>(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICrudDataSource<T>, KPrerenderCrudDataSource<T>>();
        return services;
    }
}
