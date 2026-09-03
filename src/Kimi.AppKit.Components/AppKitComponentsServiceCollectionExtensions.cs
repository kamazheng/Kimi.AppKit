using Kimi.AppKit.Components.Dialogs;
using Kimi.AppKit.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Kimi.AppKit.Components;

/// <summary>Kimi.AppKit.Components 的服务注册。</summary>
public static class AppKitComponentsServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <see cref="IKConfirm"/>/<see cref="IKNotify"/> 的 MudBlazor 实现。
    /// </summary>
    /// <remarks>
    /// ⚠️ 消费方必须确保 <c>MudDialogProvider</c>/<c>MudSnackbarProvider</c>
    /// （见 <c>KMudProviders</c>）处于交互式渲染上下文——静态 SSR 下这两个服务
    /// 都能正常解析、调用也不报错，只是永远没有任何视觉效果。
    /// </remarks>
    public static IServiceCollection AddAppKitDialogs(this IServiceCollection services)
    {
        services.AddScoped<IKConfirm, MudConfirm>();
        services.AddScoped<IKNotify, MudNotify>();
        return services;
    }
}
