using Kimi.AppKit.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Kimi.AppKit.Crud.Http;

/// <summary>WASM 端 CRUD 数据源的注册入口。</summary>
public static class KHttpCrudServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <typeparamref name="T"/> 的 HTTP CRUD 数据源，对接服务端的
    /// <c>MapCrudEndpoints&lt;T&gt;()</c>。
    /// </summary>
    /// <typeparam name="T">实体类型。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="prefix">
    /// 路由前缀。默认 <c>api/crud/{类型名小写}</c>，须与服务端 <c>MapCrudEndpoints</c> 一致。
    /// </param>
    /// <remarks>
    /// ⚠️ 依赖容器里已注册的 <see cref="HttpClient"/>，且它的 <c>BaseAddress</c>
    /// 必须指向应用根。WASM 端通常在 <c>Program.cs</c> 里注册成
    /// <c>new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) }</c>。
    ///
    /// ⚠️ **注册它不等于开放了数据。** 服务端的 <c>MapCrudEndpoints</c> 出厂即
    /// <c>RequireAuthorization()</c>，本类只是客户端的调用方；
    /// 忘了在服务端登记实体的话这里会得到 404，而不是「悄悄能读了」。
    /// </remarks>
    public static IServiceCollection AddHttpCrudDataSource<T>(
        this IServiceCollection services, string? prefix = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICrudDataSource<T>>(sp =>
            new KHttpCrudDataSource<T>(sp.GetRequiredService<HttpClient>(), prefix));

        return services;
    }
}
