using System.Net;

namespace Kimi.AppKit.Web.ErrorHandling;

/// <summary>
/// 携带 HTTP 状态码的业务异常。<see cref="GlobalExceptionHandler"/> 会把
/// <see cref="StatusCode"/> 与 <see cref="Exception.Message"/> 映射成对应的 HTTP 响应。
/// </summary>
/// <remarks>
/// 【何时该抛这个，何时该用 <see cref="Kimi.AppKit.Core.Contracts.KResult"/>】
/// 业务级失败若发生在 <c>ICrudDataSource</c>/服务方法这类**有返回值**的地方，
/// 用 <see cref="Kimi.AppKit.Core.Contracts.KResult"/> 表达，不要抛异常——
/// 调用方不该被迫 try/catch 才能拿到「校验不通过」这种预期内的结果。
/// 本类型用于**没有自然返回值**的地方（中间件、后台任务、Minimal API 端点内部）。
/// </remarks>
public class BaseException(string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
    : Exception(message)
{
    /// <summary>响应状态码。</summary>
    public HttpStatusCode StatusCode { get; } = statusCode;
}
