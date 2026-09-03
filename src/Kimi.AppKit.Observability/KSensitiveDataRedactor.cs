using System.Text.RegularExpressions;

namespace Kimi.AppKit.Observability;

/// <summary>
/// 把日志与遥测里的凭据抹掉。
/// </summary>
/// <remarks>
/// 【为什么必须有】遥测后端与日志文件的访问控制通常远松于数据库。
/// 登录请求体里的明文密码、Authorization 头里的 Bearer 令牌，一旦随 span 属性
/// 或日志正文上报，就等于把凭据复制到了一个防护更弱的地方。
///
/// 【⚠️ 这是纵深防御，不是第一道防线】更可靠的做法是**一开始就不要把请求体放进遥测**。
/// 脱敏靠正则匹配字段名，遇到没列进 <see cref="DefaultSensitiveKeys"/> 的字段名
/// （比如业务自定义的 <c>userPin</c>）就会漏掉，而且**不会有任何报错**。
/// 请求体记录默认是关的，开之前先想清楚这一点。
///
/// 【⚠️ 正则必须源生成】前身用 <c>Regex.Replace(text, pattern, ...)</c> 内联模式，
/// 每调用一次就重新解析一次模式，而这是日志热路径；且运行时构造的 <c>Regex</c>
/// 没有超时，遇到恶意构造的输入是 ReDoS 面。<c>[GeneratedRegex]</c> 在编译期生成匹配器。
/// </remarks>
public static partial class KSensitiveDataRedactor
{
    /// <summary>替换后的占位符。</summary>
    public const string Mask = "****";

    /// <summary>默认视为敏感的字段名。大小写不敏感。</summary>
    public static IReadOnlyList<string> DefaultSensitiveKeys { get; } =
    [
        "password", "passwd", "pwd", "secret", "secretkey", "clientsecret",
        "token", "access_token", "refresh_token", "id_token",
        "apikey", "api_key", "auth", "authorization", "credential", "connectionstring",
    ];

    /// <summary>
    /// 抹掉文本里的敏感值。<paramref name="text"/> 为空时原样返回。
    /// </summary>
    /// <param name="text">待脱敏的文本（JSON、表单串、日志行皆可）。</param>
    /// <param name="additionalKeys">业务自定义的敏感字段名，追加到默认清单之后。</param>
    public static string? Redact(string? text, IEnumerable<string>? additionalKeys = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var result = text;

        foreach (var key in DefaultSensitiveKeys.Concat(additionalKeys ?? []))
        {
            // "key": "value" / 'key': 'value' —— JSON 与常见日志格式都覆盖。
            // ⚠️ 这个模式**按需构造**：字段名来自调用方，无法在编译期固化。
            //    但它有超时保护，且字段名先经 Regex.Escape 转义。
            var pattern = $"([\"']{Regex.Escape(key)}[\"']\\s*:\\s*[\"'])([^\"']*)([\"'])";
            result = Regex.Replace(
                result, pattern, $"$1{Mask}$3",
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromMilliseconds(200));
        }

        // ⚠️ 顺序不能反。头部正则若先跑，会把 "authorization: Bearer" 整体替换掉，
        //    于是后面的令牌本体失去 "Bearer" 前缀，Bearer 正则再也匹配不到——
        //    结果是令牌**原样留在日志里**。这条被测试抓到过一次。
        result = BearerTokenRegex().Replace(result, $"Bearer {Mask}");
        result = AuthorizationHeaderRegex().Replace(result, $"authorization: \"{Mask}\"");
        result = QueryStringSecretRegex().Replace(result, $"$1={Mask}");

        return result;
    }

    /// <summary>抹掉 <c>Authorization</c> 头的值，保留方案名。</summary>
    public static string RedactAuthorizationHeader(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue)) return string.Empty;

        var space = headerValue.IndexOf(' ');
        return space > 0 ? $"{headerValue[..space]} {Mask}" : Mask;
    }

    // 吃到行尾/引号/逗号/右花括号为止——只吃到第一个空格会把 "Bearer xxx" 的 xxx 漏在外面。
    [GeneratedRegex("""authorization\s*:\s*["']?[^"'\r\n,}]+["']?""",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex(@"bearer\s+[A-Za-z0-9._\-]+",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex BearerTokenRegex();

    // ⚠️ 查询串里的凭据是前身漏掉的一类：?access_token=xxx 会原样进 URL 属性，
    //    而 URL 几乎必然被记录（span 名、http.url 属性、反代日志）。
    [GeneratedRegex(@"\b(access_token|token|api_key|apikey|secret|password)=[^&\s""']+",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex QueryStringSecretRegex();
}
