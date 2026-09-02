using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Kimi.AppKit.Web.ErrorHandling;

/// <summary>
/// 全局异常处理：把任意异常转换成 <see cref="ProblemDetails"/> 响应。
/// </summary>
/// <remarks>
/// 【与前身实现的差异】
/// 1. 是否暴露详情经 <see cref="IErrorDetailPolicy"/> 抽象，默认策略是 <c>false</c>（R6）。
/// 2. 删除了前身手写的「1 分钟进程内缓存」——那个缓存的默认值也是 <c>true</c>，
///    是问题的一部分而非解法。需要缓存的策略实现（比如查数据库）可以自己决定怎么缓存，
///    不应该由异常处理器代劳。
/// </remarks>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IErrorDetailPolicy errorDetailPolicy)
    : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problemDetails = new ProblemDetails { Instance = httpContext.Request.Path };

        if (exception is BaseException e)
        {
            httpContext.Response.StatusCode = (int)e.StatusCode;
            problemDetails.Detail = e.Message;
        }
        else
        {
            // ⚠️ 非 BaseException 的消息不直接当 Title 暴露——那可能是任意底层异常的
            // Message（含连接串、文件路径）。给客户端一个中性标题，详情由下面的策略决定要不要给。
            problemDetails.Title = "服务器处理请求时发生错误";
        }

        problemDetails.Status = httpContext.Response.StatusCode;
        logger.LogError(exception, "未处理的异常：{ExceptionMessage}", exception.Message);

        if (await errorDetailPolicy.ShouldExposeDetailsAsync(cancellationToken).ConfigureAwait(false))
        {
            problemDetails.Extensions["exception"] = exception.ToString();
        }

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
