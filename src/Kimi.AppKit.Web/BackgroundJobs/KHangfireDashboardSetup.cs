using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Kimi.AppKit.Core.Contracts;
using Hangfire;
using Hangfire.Dashboard;

namespace Kimi.AppKit.Web.BackgroundJobs;

/// <summary>
/// Hangfire 后台任务面板（<c>/hangfire</c>）。
/// </summary>
/// <remarks>
/// ⚠️ **Hangfire 面板出厂对任何人开放，且只挡非本机请求。**
/// 一旦部署到服务器（请求都来自 nginx，对 Hangfire 而言就是「远程」），
/// 它会**拒绝所有人**——包括管理员；而如果照网上多数示例写一个
/// <c>return true</c> 的 filter，就变成**全公网可见并可操作**：
/// 面板能看到任务参数（常含业务标识）、能手动触发、能删队列。
/// 两种错法都很常见，所以这里必须显式接上应用自己的授权。
///
/// ⚠️ 授权走 调用方传入的授权策略 而不是「已登录即可」：
/// 触发和删除后台任务是运维动作，不是普通用户能力。
/// </remarks>
public static class KHangfireDashboardSetup
{
    /// <summary>面板路径。菜单项与此处必须同源。</summary>
    public const string Path = "/hangfire";

    /// <summary>映射后台任务面板。</summary>
    /// <param name="app">应用。</param>
    /// <param name="policy">
    /// 访问面板所需的授权策略名。⚠️ 由消费方传入——触发和删除后台任务是运维动作，
    /// 但「谁算运维」是业务决策，包里不该替客户定。
    /// </param>
    public static WebApplication MapAppKitHangfireDashboard(this WebApplication app, string policy)
    {
        app.MapHangfireDashboard(Path, new DashboardOptions
        {
            Authorization = [],   // 授权交给下面的端点策略，不用 Hangfire 自己那套
            DisplayStorageConnectionString = false,   // ⚠️ 别把连接串显示在页面上
            DashboardTitle = "后台任务",
        }).RequireAuthorization(policy);

        return app;
    }
}
