using Kimi.AppKit.Core.Http;
using Kimi.AppKit.Web.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Xunit;

namespace Kimi.AppKit.Tests.Web;

/// <summary>
/// 请求本地化的 provider 顺序与默认语言校验。
/// </summary>
public class KRequestLocalizationTests
{
    private static readonly string[] TwoCultures = ["en", "zh-CN"];

    [Fact]
    public void 自定义头排在_AcceptLanguage_之前()
    {
        // 顺序错了不会报错，只是应用内的语言切换器永远被浏览器偏好压过去。
        var options = KRequestLocalizationExtensions.CreateOptions("en", TwoCultures);

        Assert.Collection(
            options.RequestCultureProviders,
            p => Assert.IsType<KRequestLanguageHeaderCultureProvider>(p),
            p => Assert.IsType<AcceptLanguageHeaderRequestCultureProvider>(p));
    }

    [Fact]
    public void 查询串与_Cookie_两个默认_provider_被移除()
    {
        // QueryStringRequestCultureProvider 让任何人都能用 ?culture=xx 改语言。
        var options = KRequestLocalizationExtensions.CreateOptions("en", TwoCultures);

        Assert.DoesNotContain(options.RequestCultureProviders, p => p is QueryStringRequestCultureProvider);
        Assert.DoesNotContain(options.RequestCultureProviders, p => p is CookieRequestCultureProvider);
    }

    [Fact]
    public void 默认语言不在支持列表里时早失败()
    {
        // 中间件本身不会抛：DefaultRequestCulture 不受 SupportedCultures 约束，
        // 于是它照常生效但切换器里选不到，症状是「默认语言和可选语言对不上」且无报错。
        var ex = Assert.Throws<ArgumentException>(
            () => KRequestLocalizationExtensions.CreateOptions("fr", TwoCultures));

        Assert.Contains("fr", ex.Message);
    }

    [Fact]
    public void 支持列表为空时早失败()
    {
        Assert.Throws<ArgumentException>(
            () => KRequestLocalizationExtensions.CreateOptions("en", []));
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en")]
    public async Task 自定义头的值被采纳(string lang)
    {
        var provider = new KRequestLanguageHeaderCultureProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers[KLocalizationHeaders.RequestLanguage] = lang;

        var result = await provider.DetermineProviderCultureResult(context);

        Assert.NotNull(result);
        Assert.Equal(lang, result.Cultures[0].Value);
        Assert.Equal(lang, result.UICultures[0].Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task 头缺失或为空时让位给下一个_provider(string? headerValue)
    {
        // 返回 null 而不是默认语言——否则后面的 AcceptLanguage provider 永远轮不到。
        var provider = new KRequestLanguageHeaderCultureProvider();
        var context = new DefaultHttpContext();
        if (headerValue is not null)
            context.Request.Headers[KLocalizationHeaders.RequestLanguage] = headerValue;

        Assert.Null(await provider.DetermineProviderCultureResult(context));
    }
}
