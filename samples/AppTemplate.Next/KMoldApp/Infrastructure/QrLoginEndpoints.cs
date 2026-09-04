using Kimi.AppKit.Web.Authentication;
using Kimi.AppKit.Web.Authorization;
using KMoldApp.Components.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KMoldApp.Infrastructure;

/// <summary>
/// 扫码登录：打印一张加密卡片，现场用扫码枪扫入登录。
/// </summary>
/// <remarks>
/// 【为什么需要它】产线现场的电脑常常不在域内、也没有键盘友好的输入条件，
/// 扫码枪是那里最顺手的输入设备。卡片在域内电脑上打印，带到现场使用。
///
/// ⚠️ **卡片等价于一张写着密码的便条**（密文里装着账号和密码）。
/// 这是 ROPC + 二维码方案的固有性质，缓解手段只有三条，缺一不可：
/// 有效期（<c>Auth:QrLogin:Lifetime</c>）、网络准入、卡片上写明勿外传。
/// </remarks>
public static class QrLoginEndpoints
{
    private static readonly string[] BlockedReturnPrefixes = ["/login", "/password-login", "/qr-print", "/authentication"];

    /// <summary>映射扫码登录与卡片打印端点。</summary>
    public static IEndpointRouteBuilder MapQrLogin(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/qr-print", RenderPrintPage).AllowAnonymous();
        endpoints.MapPost("/authentication/qr-print", PrintAsync).AllowAnonymous();
        endpoints.MapPost("/authentication/qr-login", LoginAsync).AllowAnonymous();

        return endpoints;
    }

    private static IResult RenderPrintPage(string? error) =>
        new RazorComponentResult<QrPrintPage>(new { Error = error });

    /// <summary>验一次密码（证明是本人），然后签发卡片。</summary>
    /// <remarks>
    /// ⚠️ 网络准入只对**匿名**入口生效。已登录用户可能正坐在域内的办公电脑前打印，
    /// 那台机器本来就不在车间网段。放行是安全的：打印不建立任何会话，
    /// 而卡片只能经同样受网络准入约束的 <c>/authentication/qr-login</c> 使用。
    /// ⚠️ 判「是否已登录」必须显式认证 Cookie 方案——默认方案是 OIDC，POST 请求不读 Cookie。
    /// </remarks>
    private static async Task<IResult> PrintAsync(
        HttpContext http,
        [FromForm] string? username,
        [FromForm] string? password,
        KNetworkGate networkGate,
        IKOidcTokenService tokenService,
        KQrLogin qrLogin,
        IOptionsMonitor<KQrLoginOptions> qrOptions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var cookieAuth = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)
            .ConfigureAwait(false);

        if (!cookieAuth.Succeeded && networkGate.Evaluate(http) != KNetworkGateResult.Allowed)
            return Results.Redirect("/qr-print?error=" + Uri.EscapeDataString("当前网络不允许打印登录二维码。"));

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Results.Redirect("/qr-print?error=" + Uri.EscapeDataString("请填写工号与密码。"));

        // ⚠️ 这一步换令牌**只为验密**，令牌本身随即丢弃、不建会话。
        //    少了它，任何人都能拿别人的工号打印出一张能登录的卡片。
        if (!await VerifyAsync(tokenService, username, password, loggerFactory, cancellationToken).ConfigureAwait(false))
            return Results.Redirect("/qr-print?error=" + Uri.EscapeDataString("工号或密码不正确。"));

        // ⚠️ 密文只出现在这一次响应体里：不落 Cookie、不进 URL、服务端不持久化密码。
        //    进 URL 就会被浏览器历史、代理日志与 Referer 头一路带走。
        var payload = qrLogin.Protect(username, password);

        return new RazorComponentResult<QrPrintPage>(new
        {
            Payload = payload,
            UserName = username,
            LifetimeHint = Describe(qrOptions.CurrentValue.Lifetime),
        });
    }

    /// <summary>扫码枪把密文键入后提交到这里。</summary>
    private static async Task<IResult> LoginAsync(
        HttpContext http,
        [FromForm] string? payload,
        [FromForm] string? returnUrl,
        KNetworkGate networkGate,
        IKOidcTokenService tokenService,
        KQrLogin qrLogin,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // 与密码登录同一道防线：卡片可能被带出车间，网络准入是它唯一的地理约束。
        if (networkGate.Evaluate(http) != KNetworkGateResult.Allowed)
            return Redirect(returnUrl, "当前网络不允许扫码登录。");

        // ⚠️ 密文非法、被篡改、已过期在这里得到**同一个**结果。
        //    区分开来等于告诉攻击者「这张卡是真的，只是过期了」。
        var credential = qrLogin.TryUnprotect(payload);
        if (credential is null)
            return Redirect(returnUrl, "二维码无效或已过期，请重新打印。");

        var token = await ExchangeAsync(
            tokenService, credential.Value.Username, credential.Value.Password,
            loggerFactory, cancellationToken).ConfigureAwait(false);

        if (token?.IdToken is null)
            return Redirect(returnUrl, "二维码对应的账号已无法登录，请重新打印。");

        await CookieSignIn.SignInAsync(http, token).ConfigureAwait(false);

        return Results.Redirect(KReturnUrl.Sanitize(returnUrl, BlockedReturnPrefixes));
    }

    private static async Task<bool> VerifyAsync(
        IKOidcTokenService tokenService, string username, string password,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        await ExchangeAsync(tokenService, username, password, loggerFactory, cancellationToken)
            .ConfigureAwait(false) is { IdToken: not null };

    /// <remarks>
    /// ⚠️ 令牌服务在 IdP 返回非 2xx 时**抛异常**，不是返回 null。
    /// 异常消息里带着 IdP 的 error_description，只进日志、绝不回显。
    /// </remarks>
    private static async Task<Kimi.AppKit.Core.Contracts.KTokenResponse?> ExchangeAsync(
        IKOidcTokenService tokenService, string username, string password,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        try
        {
            return await tokenService.PasswordLoginAsync(username, password, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            loggerFactory.CreateLogger(typeof(QrLoginEndpoints))
                .LogWarning(ex, "扫码登录换取令牌失败，IdP 拒绝了请求。");
            return null;
        }
    }

    private static IResult Redirect(string? returnUrl, string error)
    {
        var query = $"?error={Uri.EscapeDataString(error)}";
        if (!string.IsNullOrWhiteSpace(returnUrl))
            query += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";

        return Results.Redirect("/password-login" + query);
    }

    private static string Describe(TimeSpan lifetime) =>
        lifetime.TotalHours >= 1
            ? $"{lifetime.TotalHours:0.#} 小时"
            : $"{lifetime.TotalMinutes:0} 分钟";
}
