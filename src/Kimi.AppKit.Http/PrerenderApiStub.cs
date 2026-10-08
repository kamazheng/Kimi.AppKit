using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Kimi.AppKit.Http;

/// <summary>
/// 服务端**预渲染阶段**用的 Refit 接口空实现：任何方法被真的调用都会抛异常。
/// </summary>
/// <remarks>
/// 【为什么必须有】<see cref="RefitServiceCollectionExtensions.AddAppKitRefitClient{T}"/>
/// 只注册在客户端（WASM）容器里，但**服务端在预渲染引用它的页面时也要能解析这个类型**，
/// 即使页面本身设了 <c>prerender: false</c>。少了服务端这份注册，整页 500：
/// <c>Cannot provide a value for property 'Xxx' ... There is no registered service</c>，
/// 且错误指向组件的属性注入、不指向注册——与
/// <c>Kimi.AppKit.Crud.Http.KPrerenderCrudDataSource&lt;T&gt;</c> 要解决的是同一类问题。
///
/// 【为什么抛异常而不是像 CRUD 那样返回空结果】<c>ICrudDataSource&lt;T&gt;</c> 只有 4 个
/// 形状统一的方法，能定义一个通用的"空"语义（空页、null、失败 KResult）。
/// 而每个业务 Refit 接口的方法各不相同（返回 <c>decimal</c>、自定义 DTO、
/// <c>HttpResponseMessage</c> ……），没有统一的"空值"可给。业务页面本来就必须靠
/// <c>RendererInfo.IsInteractive</c> 守住"预渲染时不取数"，本类只兜底那道守卫万一
/// 被漏掉的情况——**明确失败**比返回一个编出来的假数据更安全。
/// </remarks>
public static class PrerenderApiStub
{
    /// <summary>
    /// 在**服务端**注册 <typeparamref name="T"/>（一个 Refit 接口）的预渲染空实现。
    /// </summary>
    /// <remarks>
    /// ⚠️ 与客户端的 <see cref="RefitServiceCollectionExtensions.AddAppKitRefitClient{T}"/>
    /// **成对使用**：客户端那份负责真正发请求，本份只保证预渲染阶段不因缺服务而整页 500。
    /// </remarks>
    public static IServiceCollection AddPrerenderRefitClient<T>(this IServiceCollection services)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped(_ => DispatchProxy.Create<T, ThrowingDispatchProxy>());
        return services;
    }

    private class ThrowingDispatchProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException(
                $"「{targetMethod?.DeclaringType?.Name}.{targetMethod?.Name}」在预渲染阶段被调用了——" +
                "业务页面必须先判 RendererInfo.IsInteractive 再取数，预渲染跑在服务端、" +
                "拿不到浏览器的认证 Cookie，这里不该被真的调用到。");
    }
}
