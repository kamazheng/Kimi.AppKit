using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kimi.AppKit.Web.ErrorHandling;

/// <summary>全局异常处理的注册入口。</summary>
public static class KErrorHandlingServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <see cref="GlobalExceptionHandler"/> 与 <c>ProblemDetails</c>，
    /// 并在消费方未提供 <see cref="IErrorDetailPolicy"/> 时兜底为「不暴露详情」。
    /// </summary>
    /// <remarks>
    /// ⚠️ 三样必须成套：<c>AddExceptionHandler</c> 注册的处理器只有在
    /// <c>AddProblemDetails</c> 也在时才拿得到默认的 ProblemDetails 写出能力，
    /// 而处理器构造注入 <see cref="IErrorDetailPolicy"/>——少了它是**启动期**
    /// DI 解析失败，不是运行期降级。
    ///
    /// 【为什么用 TryAdd 兜底】<see cref="FixedErrorDetailPolicy.Disabled"/> 的文档
    /// 一直写着它是「消费方尚未接入运行期设置系统时的兜底」，但在此之前没有任何一处
    /// 真的把它注册成默认值——那个兜底名存实亡。
    /// 用 <c>TryAdd</c> 而非 <c>Add</c>：消费方**先**注册自己的策略实现（如读数据库设置的
    /// <c>SettingBackedErrorDetailPolicy</c>）时不会被覆盖，顺序无关。
    ///
    /// ⚠️ 兜底方向只能是「不暴露」。读不到策略就倒向暴露，等于让配置缺失自动放松防护——
    /// 完整异常链（含内部类型名、文件路径、EF 异常里的 SQL 片段）会从任意能触发 500 的
    /// 匿名接口漏出去。这正是前身的失败形态。
    /// </remarks>
    public static IServiceCollection AddAppKitErrorHandling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IErrorDetailPolicy>(FixedErrorDetailPolicy.Disabled);
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        return services;
    }
}
