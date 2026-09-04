namespace Kimi.AppKit.Core.Http;

/// <summary>前后端约定的本地化请求头。</summary>
/// <remarks>
/// 【为什么在 Core 而不是 Web】这个头名是**两端**的契约：服务端的
/// <c>KRequestLanguageHeaderCultureProvider</c> 读它，浏览器端的委托处理器写它。
/// Web 包是服务端专用的，WASM 客户端引用不到——常量放那里的结果必然是客户端再写一份
/// 字面量，于是「改一处漏一处」变成静默失配：请求头照发，服务端不认，语言默默退回默认值。
/// </remarks>
public static class KLocalizationHeaders
{
    /// <summary>
    /// 客户端用它声明当前 UI 语言，优先级高于浏览器的 <c>Accept-Language</c>。
    /// </summary>
    public const string RequestLanguage = "X-Request-Language";
}
