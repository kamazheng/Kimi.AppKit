using Kimi.AppKit.Core.Contracts;
using System.Net;

namespace Kimi.AppKit.Crud.Http;

/// <summary>
/// 把 <see cref="HttpResponseMessage"/> 翻译成 <see cref="KResult"/>。
/// </summary>
/// <remarks>
/// 【为什么要有这个】<see cref="KResult"/> **从不在网线上传输**——它是只读结构、私有构造，
/// 服务端返回的是 HTTP 状态码 + <c>ProblemDetails</c>，客户端在本地重建 <see cref="KResult"/>。
/// 这份翻译原先私藏在 <see cref="KHttpCrudDataSource{T}"/> 里，于是每个调用**非 CRUD 端点**
/// 的应用都得自己抄一遍——抄出来的版本通常只剩「操作失败」四个字。
///
/// 【⚠️ 不要简化成「成功/失败」两分】401、403、409 三种失败对用户意味着完全不同的下一步
/// （重新登录 / 找管理员要权限 / 刷新后重试）。压成同一句提示，用户和排障的人都无从判断。
/// </remarks>
public static class KHttpResultExtensions
{
    /// <summary>按状态码与响应正文构造 <see cref="KResult"/>。</summary>
    /// <remarks>
    /// ⚠️ 失败时把服务端正文原样带回来当作失败原因——服务端的
    /// <c>ProblemDetails.detail</c> 才是唯一说得清「为什么不行」的地方。
    /// </remarks>
    public static async Task<KResult> ToKResultAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.IsSuccessStatusCode) return KResult.Ok();

        var detail = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => KResult.Fail("未登录或登录已过期。"),
            HttpStatusCode.Forbidden => KResult.Fail("没有执行该操作的权限。"),
            HttpStatusCode.Conflict => KResult.Fail("数据已被他人修改，请刷新后重试。"),
            _ => KResult.Fail(string.IsNullOrWhiteSpace(detail)
                ? $"操作失败（HTTP {(int)response.StatusCode}）。"
                : detail),
        };
    }
}
