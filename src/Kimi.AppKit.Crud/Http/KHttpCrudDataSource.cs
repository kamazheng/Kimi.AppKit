using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kimi.AppKit.Crud.Http;

/// <summary>
/// <see cref="ICrudDataSource{T}"/> 的 HTTP 实现，对接 <c>MapCrudEndpoints&lt;T&gt;()</c>。
/// </summary>
/// <typeparam name="T">实体类型。</typeparam>
/// <remarks>
/// 【为什么需要它】<see cref="ICrudDataSource{T}"/> 的接口注释把三种宿主都列了出来，
/// 但包里长期**只有服务端的 EF 实现**。于是 Blazor WebAssembly 页面拿不到数据源，
/// 只能把本该跑在浏览器里的页面改成服务端渲染——而那会连带引入混合渲染的一整套
/// 复杂性（MudBlazor 的 Provider 在两个渲染模式下抢同一个 section ID 直接抛异常）。
///
/// 【路由约定】必须与 <c>MapCrudEndpoints</c> 一致，默认 <c>api/crud/{类型名小写}</c>：
/// <list type="bullet">
/// <item><c>GET  {root}/</c> —— 分页查询</item>
/// <item><c>GET  {root}/{id}</c> —— 取单条</item>
/// <item><c>POST {root}/</c> —— 新增或更新</item>
/// <item><c>DELETE {root}/{id}</c> —— 删除</item>
/// </list>
/// ⚠️ 改了服务端的 <c>prefix</c> 就要同步改这里的 <paramref name="prefix"/>——
/// 两处是同一份约定的两个副本，不一致时表现为 404，看不出是路由约定错位。
///
/// 【⚠️ 授权不在这一层】见接口注释：鉴权由服务端端点负责。
/// 本类只负责把 401/403 转成可读的 <see cref="KResult"/>，不做任何本地权限判断——
/// 客户端的权限判断只能用于显示/隐藏，绝不能当作访问控制。
/// </remarks>
public sealed class KHttpCrudDataSource<T>(HttpClient httpClient, string? prefix = null)
    : ICrudDataSource<T>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _root = prefix ?? $"api/crud/{typeof(T).Name.ToLowerInvariant()}";

    /// <inheritdoc />
    public async Task<KPage<T>> LoadAsync(KQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var url = $"{_root}/?page={query.Page}&pageSize={query.PageSize}"
                  + (string.IsNullOrWhiteSpace(query.Search) ? "" : $"&search={Uri.EscapeDataString(query.Search)}")
                  + (string.IsNullOrWhiteSpace(query.SortBy) ? "" : $"&sortBy={Uri.EscapeDataString(query.SortBy)}")
                  + (query.SortDescending ? "&sortDescending=true" : "");

        var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

        // ⚠️ 未授权时返回空页而不是抛。列表页在用户没权限时应显示「无数据」而不是
        //    整页崩掉——真正的拦截已经由服务端完成，这里只决定怎么呈现。
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return KPage<T>.Empty(query);

        response.EnsureSuccessStatusCode();

        return await response.Content
                   .ReadFromJsonAsync<KPage<T>>(JsonOptions, cancellationToken)
                   .ConfigureAwait(false)
               ?? KPage<T>.Empty(query);
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync(object id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        var response = await httpClient
            .GetAsync($"{_root}/{Uri.EscapeDataString(id.ToString()!)}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound
            or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return default;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<KResult> UpsertAsync(T item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var response = await httpClient
            .PostAsJsonAsync($"{_root}/", item, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return await ToResultAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<KResult> DeleteAsync(object id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        var response = await httpClient
            .DeleteAsync($"{_root}/{Uri.EscapeDataString(id.ToString()!)}", cancellationToken)
            .ConfigureAwait(false);

        return await ToResultAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <remarks>
    /// ⚠️ 失败时把服务端的 <c>ProblemDetails</c> 正文带回来。
    /// 只返回「操作失败」会让并发冲突、校验不通过、权限不足三种完全不同的情况
    /// 在界面上长得一模一样，用户和排障的人都无从判断。
    /// </remarks>
    private static async Task<KResult> ToResultAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return KResult.Ok();

        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

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
