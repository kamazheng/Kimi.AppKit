using System.Net;
using Kimi.AppKit.Core.Abstractions;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Kimi.AppKit.Components.Buttons;

/// <summary>
/// 集中式异常 → UI 提示分流器。<see cref="ErrorCatchButton"/> 与应用级 <c>ErrorBoundary</c>
/// 都应该调它，保证同一种异常在任何触发路径下都得到同一种提示。
/// </summary>
/// <remarks>
/// 【为什么要收口】前身没有这层收口时，每个按钮各自写 <c>catch</c> + 各自决定弹什么，
/// 同一个 403 在这个页面弹一条纯文本吐司、在那个页面弹一个对话框，用户经验不一致，
/// 还容易漏处理某个状态码（尤其 401——忘了跳登录，用户以为系统卡死了）。
///
/// 【与前身实现的差异】不再依赖自定义的 <c>AccessDeniedDialog</c>/<c>MyErrorContent</c>
/// 对话框组件——那两个组件本身有价值，但把它们也搬进来会让本次改动的范围失控。
/// 现在 403/其它错误改用 MudBlazor 自带的 <c>IDialogService.ShowMessageBoxAsync</c>
/// 展示，交互没有前身精致，但异常分流的核心行为（哪种异常弹什么、要不要跳登录）保留。
/// 更精致的错误展示对话框留给 P6。
/// </remarks>
public static class ApiErrorPresenter
{
    /// <summary>
    /// 展示异常。
    /// </summary>
    /// <param name="exception">捕获到的异常。</param>
    /// <param name="notify">吐司通道。</param>
    /// <param name="dialogService">需要展示详情时使用；为 null 时降级为吐司。</param>
    /// <param name="navigation">401 时跳转登录页；为 null 时跳过跳转。</param>
    /// <param name="loginPath">登录页相对路径。</param>
    public static async Task PresentAsync(
        Exception exception,
        IKNotify notify,
        IDialogService? dialogService,
        NavigationManager? navigation,
        string loginPath = "authentication/login")
    {
        if (exception is not HttpRequestException http)
        {
            await ShowErrorDialogOrToastAsync(exception, dialogService, notify);
            return;
        }

        switch (http.StatusCode)
        {
            case HttpStatusCode.Forbidden:
                notify.Fail(BuildForbiddenMessage(http));
                return;

            case HttpStatusCode.Unauthorized:
                notify.Warn("登录已失效，请重新登录。");
                if (navigation is not null)
                {
                    var returnUrl = Uri.EscapeDataString(navigation.ToBaseRelativePath(navigation.Uri));
                    navigation.NavigateTo($"{loginPath}?returnUrl={returnUrl}", forceLoad: true);
                }
                return;

            case null:
                // 无状态码：网络/连接失败，没有服务器响应可看。
                notify.Fail("网络连接失败，请检查网络后重试。");
                return;

            default:
                // 5xx 及其它服务器错误：展示详情，方便定位问题。
                await ShowErrorDialogOrToastAsync(http, dialogService, notify);
                return;
        }
    }

    private static async Task ShowErrorDialogOrToastAsync(Exception exception, IDialogService? dialogService, IKNotify notify)
    {
        if (dialogService is null)
        {
            notify.Fail(exception.Message);
            return;
        }

        await dialogService.ShowMessageBoxAsync("发生错误", exception.Message, yesText: "关闭");
    }

    /// <summary>
    /// 403 的提示消息。优先取服务端在 <see cref="Exception.Data"/> 里塞的缺失角色列表——
    /// 那是 <c>EnsureSuccessCode</c> 一类的服务端契约，跨端约定的键名不能随便改。
    /// </summary>
    private static string BuildForbiddenMessage(HttpRequestException http)
    {
        if (http.Data["missingRoles"] is string[] { Length: > 0 } roles)
        {
            return $"您没有执行此操作的权限，缺少角色：{string.Join("、", roles)}";
        }

        return "您没有执行此操作的权限。";
    }
}
