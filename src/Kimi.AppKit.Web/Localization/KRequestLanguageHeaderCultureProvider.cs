using Kimi.AppKit.Core.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace Kimi.AppKit.Web.Localization;

/// <summary>
/// 从自定义请求头取语言，让前端能显式指定语种而不受浏览器 <c>Accept-Language</c> 摆布。
/// </summary>
/// <remarks>
/// 【为什么需要它】<c>Accept-Language</c> 反映的是**浏览器**的偏好，而应用内的语言切换器
/// 反映的是**用户在本应用里**的选择。两者不一致时应当以后者为准，
/// 但 <c>Accept-Language</c> 改不了——它由浏览器设置决定。
///
/// ⚠️ 本 provider 不校验语言标签是否受支持。<c>RequestLocalizationMiddleware</c> 会拿返回值
/// 与 <c>SupportedCultures</c> 求交集，取不到就继续问下一个 provider，
/// 全都取不到才用 <c>DefaultRequestCulture</c>。所以这里返回一个不受支持的值是安全的
/// ——它只是被忽略，不会抛。
/// </remarks>
public sealed class KRequestLanguageHeaderCultureProvider : RequestCultureProvider
{
    /// <inheritdoc />
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // 头名走 Core 的常量：客户端的委托处理器写的是同一个来源，不能两端各写一份字面量。
        if (!httpContext.Request.Headers.TryGetValue(KLocalizationHeaders.RequestLanguage, out var values))
            return Task.FromResult<ProviderCultureResult?>(null);

        var lang = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(lang))
            return Task.FromResult<ProviderCultureResult?>(null);

        // Culture 与 UICulture 取同一个值：本应用没有「按 A 地格式化数字、按 B 语言显示文案」
        // 这种需求，分开只会多一个能配错的地方。
        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(lang, lang));
    }
}
