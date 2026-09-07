using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Kimi.AppKit.Components.Auth;
using Kimi.AppKit.Core.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Kimi.AppKit.Web.ErrorHandling;

/// <summary>未处理异常的落地页。</summary>
/// <remarks>
/// ⚠️ 做成端点而不是 <c>@page</c>：本站全局 InteractiveWebAssembly，
/// 服务端页面会先 SSR 出来、再被 WASM 接管清空（零报错）。
/// 详见 <c>Components/Account/AuthLayout.razor</c> 顶部。
/// </remarks>
public static class KErrorPageEndpoint
{
    /// <summary>映射错误页。路径与 <c>UseExceptionHandler("/Error")</c> 一致。</summary>
    public static IEndpointRouteBuilder MapAppKitErrorPage(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // ⚠️ 必须 AllowAnonymous：默认拒绝策略下，未登录用户遇到异常会被重定向到登录页，
        //    于是「出错了」这件事被掩盖成「你没登录」，报障时说不清到底发生了什么。
        endpoints.MapGet("/Error", Render).AllowAnonymous();

        return endpoints;
    }

    private static IResult Render(HttpContext http) =>
        new RazorComponentResult<KErrorPage>(new
        {
            // 追踪号优先取 Activity，退回 TraceIdentifier。⚠️ 只给号，不给异常内容。
            RequestId = System.Diagnostics.Activity.Current?.Id ?? http.TraceIdentifier,
        })
        {
            // ⚠️ 必须回 500。默认是 200，那会让浏览器、反代与探针都以为这次请求成功了，
            //    错误率指标上完全看不到——而用户看到的分明是一页错误。
            StatusCode = StatusCodes.Status500InternalServerError,
        };
}
