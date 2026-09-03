using System.Text.Json;

namespace Kimi.AppKit.Core.Http;

/// <summary>
/// 把失败的 HTTP 响应变成一个**信息完整**的异常。
/// </summary>
/// <remarks>
/// 【为什么不用 <c>HttpResponseMessage.EnsureSuccessStatusCode()</c>】
/// 内置那个方法丢掉响应体，异常消息只有一句
/// <c>Response status code does not indicate success: 400 (Bad Request)</c>。
/// 而服务端明明在 <c>ProblemDetails</c> 里写清了原因（「这条记录已被其他人修改，请刷新后重试」），
/// 用户却只能看到状态码。
///
/// 本方法把 <c>detail</c>/<c>message</c>/<c>title</c> 提出来作为异常 Message
/// （于是未做特殊处理的调用方直接 <c>catch (Exception ex)</c> 展示 <c>ex.Message</c> 就是友好的），
/// 把结构化信息（缺哪个角色、缺哪个权限、服务端异常链、原始 body）放进
/// <see cref="Exception.Data"/>，供集中的错误对话框读取。
///
/// 【⚠️ 为什么在 Core 而不是客户端包】服务端也要用它——授权特性
/// （<c>AppRoleRequired</c>/<c>PermissionRequired</c>）与 <c>TokenService</c> 调用外部 IdP
/// 时走的是同一条路径。放客户端包会让服务端拿不到，放两份则是又一处手工同步。
/// 它只依赖 <c>HttpResponseMessage</c> 与 <c>System.Text.Json</c>，不违反 Core 的零依赖约束。
/// </remarks>
public static class KHttpResponseExtensions
{
    /// <summary>Data 字典里存放原始响应体的键。</summary>
    public const string BodyKey = "body";

    /// <summary>Data 字典里存放「缺少的角色」的键。</summary>
    public const string MissingRolesKey = "missingRoles";

    /// <summary>Data 字典里存放「缺少的权限」的键。</summary>
    public const string MissingPermissionsKey = "missingPermissions";

    /// <summary>Data 字典里存放服务端异常链的键。</summary>
    public const string ServerExceptionKey = "serverException";

    /// <summary>
    /// 响应不成功时抛出携带完整信息的 <see cref="HttpRequestException"/>；成功则原样返回。
    /// </summary>
    public static async Task EnsureSuccessOrThrowAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.IsSuccessStatusCode) return;

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw BuildException(response.StatusCode, content);
    }

    /// <summary>
    /// 从状态码与响应体构造异常。供 Refit 的 <c>ExceptionFactory</c> 之类的场景直接调用。
    /// </summary>
    public static HttpRequestException BuildException(
        System.Net.HttpStatusCode statusCode, string? content)
    {
        var message = content ?? string.Empty;
        string[]? missingRoles = null;
        string[]? missingPermissions = null;
        string? serverException = null;

        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var root = doc.RootElement;
                    message = ReadString(root, "detail")
                              ?? ReadString(root, "message")
                              ?? ReadString(root, "title")
                              ?? content;
                    missingRoles = ReadStringArray(root, MissingRolesKey);
                    missingPermissions = ReadStringArray(root, MissingPermissionsKey);
                    serverException = ReadString(root, "exception");
                }
            }
            catch (JsonException)
            {
                // 非 JSON（例如反代返回的 HTML 错误页）：原文就是最有用的信息，保留它。
            }
        }

        var ex = new HttpRequestException(message, inner: null, statusCode: statusCode);
        ex.Data[BodyKey] = content;
        if (missingRoles is not null) ex.Data[MissingRolesKey] = missingRoles;
        if (missingPermissions is not null) ex.Data[MissingPermissionsKey] = missingPermissions;
        if (serverException is not null) ex.Data[ServerExceptionKey] = serverException;
        return ex;
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string[]? ReadStringArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return null;

        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString()!);
        }
        return list.Count > 0 ? [.. list] : null;
    }
}
