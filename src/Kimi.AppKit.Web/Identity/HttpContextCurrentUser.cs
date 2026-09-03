using System.Security.Claims;
using Kimi.AppKit.Core.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Kimi.AppKit.Web.Identity;

/// <summary>
/// 从 <see cref="HttpContext.User"/> 取当前操作者的 <see cref="IKCurrentUser"/> 实现。
/// 服务端（含 Blazor Server / 静态 SSR / Web API）用这一个。
/// </summary>
/// <remarks>
/// 【⚠️ 取不到身份返回哨兵值，不返回 "System"】
/// 审计的全部价值在「谁干的」。把「取不到」伪装成一次正常的系统操作，
/// 等于让审计表在最需要它的时候说谎。这里返回带 <c>anonymous:</c> 前缀的哨兵值，
/// 事后翻审计表能一眼看出「这条记录的操作者没取到」，而不是误以为是后台任务写的。
///
/// 【⚠️ 后台任务不要用这个实现】Hangfire/托管服务里没有 <see cref="HttpContext"/>，
/// 拿到的永远是哨兵值。后台任务应注册一个固定系统身份的实现，
/// 让审计表里的「谁干的」如实写成那个任务的名字。
/// </remarks>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : IKCurrentUser
{
    /// <summary>未认证时写入审计的哨兵值。</summary>
    public const string Anonymous = "anonymous:unauthenticated";

    /// <summary>有身份但取不到任何可用名称时的哨兵值。</summary>
    public const string Unresolved = "anonymous:unresolved";

    /// <inheritdoc />
    public ValueTask<string> GetUserNameAsync(CancellationToken cancellationToken = default)
    {
        var user = accessor.HttpContext?.User;

        if (user?.Identity is not { IsAuthenticated: true })
            return ValueTask.FromResult(Anonymous);

        // ⚠️ 按优先级找一个稳定标识：Name 可能没配 nameType 而为空，
        // 此时退到 preferred_username / sub，而不是直接给哨兵值。
        var name = user.Identity.Name
            ?? user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("sub")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return ValueTask.FromResult(string.IsNullOrWhiteSpace(name) ? Unresolved : name);
    }

    /// <inheritdoc />
    public ValueTask<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false);
}
