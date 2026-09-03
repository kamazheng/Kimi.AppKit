namespace Kimi.AppKit.Web.Authentication;

/// <summary>
/// 把用户可控的 <c>returnUrl</c> 收敛成一个安全的站内相对路径。
/// </summary>
/// <remarks>
/// 【防的是开放重定向】登录成功后跳到 <c>returnUrl</c> 是标准做法，
/// 而这个值来自查询串或表单，攻击者可以任意构造。
/// 不校验的话，一条 <c>/login?returnUrl=https://evil.example</c> 就能把
/// **刚刚成功登录的用户**送到钓鱼站，而地址栏上一跳还是你的域名——
/// 这正是钓鱼最想要的那种信任转移。
///
/// 【⚠️ 为什么这段代码值得从应用里抽出来】它原本是登录端点里的一个
/// <c>private static</c> 方法，逻辑本身写得不错（协议相对 URL、反斜杠都挡了），
/// 但**没有任何测试，而且以那个可见性根本写不了测试**。
/// 一段安全关键、边界条件多、又没法测的代码，等于每次改动都在赌。
/// </remarks>
public static class KReturnUrl
{
    /// <summary>校验失败时的回落路径。</summary>
    public const string Fallback = "/";

    /// <summary>
    /// 收敛 <paramref name="returnUrl"/>。任何可疑输入一律回落到 <see cref="Fallback"/>。
    /// </summary>
    /// <param name="returnUrl">用户提交的回跳地址。</param>
    /// <param name="blockedPrefixes">
    /// 禁止回跳的路径前缀（大小写不敏感）。通常是登录相关端点——
    /// 跳回登录页会让用户看起来「登录成功了却又回到登录页」，像是登录失败。
    /// </param>
    public static string Sanitize(string? returnUrl, IReadOnlyList<string>? blockedPrefixes = null)
    {
        // 绝对 URL、空值、畸形输入统统在这里被挡掉。
        // Uri.IsWellFormedUriString(..., Relative) 对 "https://evil.example" 返回 false。
        if (string.IsNullOrWhiteSpace(returnUrl)
            || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
        {
            return Fallback;
        }

        var path = returnUrl[0] == '/' ? returnUrl : "/" + returnUrl;

        // ⚠️ 这两条是真正的绕过点，缺一不可：
        //    「//evil.example」是**协议相对 URL**——它是合法的相对 URI，首字符也是 '/'，
        //    浏览器却会把它当成绝对地址跳到 evil.example。
        //    反斜杠则会被浏览器归一成斜杠，于是 "/\evil.example" 等价于 "//evil.example"。
        if (path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\'))
        {
            return Fallback;
        }

        if (blockedPrefixes is not null)
        {
            foreach (var prefix in blockedPrefixes)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return Fallback;
            }
        }

        return path;
    }
}
