using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using System.Globalization;

namespace Kimi.AppKit.Web.Localization;

/// <summary>请求本地化的装配。</summary>
public static class KRequestLocalizationExtensions
{
    /// <summary>
    /// 启用请求本地化：自定义头优先，其次浏览器的 <c>Accept-Language</c>，最后默认语言。
    /// </summary>
    /// <param name="app">应用管线。</param>
    /// <param name="defaultCulture">取不到任何语言偏好时使用的 BCP-47 语言标签，如 <c>"en"</c>。</param>
    /// <param name="supportedCultures">支持的语言标签，必须包含 <paramref name="defaultCulture"/>。</param>
    /// <remarks>
    /// 【⚠️ 管线位置】官方约束是「必须在**任何可能读取请求 culture 的中间件**之前」，
    /// 并**点名 <c>UseStaticFiles</c> 作为例子**。它不是「必须在 UseRouting 之前」——
    /// 这两条常被混为一谈，而且方向可能相反：用
    /// <c>RouteDataRequestCultureProvider</c>（从路由段取语言）时，本中间件反而**必须在
    /// UseRouting 之后**，否则路由数据还不存在。
    ///
    /// 对 Blazor Web App，微软的具体建议是放在 <c>MapRazorComponents</c> 紧前。
    /// 本方法用的两个 provider 都只读请求头，与路由无关，因此**尽量靠前**即可——
    /// 放在所有静态文件中间件之前。
    ///
    /// ⚠️ 这个错放是静默的：管线照常工作，只是本地化对排在它前面的中间件不生效。
    /// 参见 <see href="https://learn.microsoft.com/aspnet/core/fundamentals/middleware/#middleware-order"/>。
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="supportedCultures"/> 为空，或不包含 <paramref name="defaultCulture"/>。
    /// </exception>
    public static IApplicationBuilder UseAppKitRequestLocalization(
        this IApplicationBuilder app,
        string defaultCulture,
        IReadOnlyList<string> supportedCultures)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseRequestLocalization(CreateOptions(defaultCulture, supportedCultures));
    }

    /// <summary>
    /// 构造本地化选项。单独公开是为了可测——中间件注册本身没法回读它挂了哪些 provider。
    /// </summary>
    /// <param name="defaultCulture">取不到任何语言偏好时使用的 BCP-47 语言标签。</param>
    /// <param name="supportedCultures">支持的语言标签，必须包含 <paramref name="defaultCulture"/>。</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="supportedCultures"/> 为空，或不包含 <paramref name="defaultCulture"/>。
    /// </exception>
    public static RequestLocalizationOptions CreateOptions(
        string defaultCulture, IReadOnlyList<string> supportedCultures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCulture);
        ArgumentNullException.ThrowIfNull(supportedCultures);

        if (supportedCultures.Count == 0)
            throw new ArgumentException("至少要给一个支持的语言。", nameof(supportedCultures));

        // ⚠️ 默认语言不在支持列表里时，中间件**不会抛**——DefaultRequestCulture 不受
        //    SupportedCultures 约束，于是它照常生效，但语言切换器里又选不到这个语言。
        //    症状是「默认语言和可选语言对不上」，没有任何报错。在这里早失败。
        if (!supportedCultures.Contains(defaultCulture, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"默认语言 '{defaultCulture}' 不在支持列表 [{string.Join(", ", supportedCultures)}] 中。",
                nameof(defaultCulture));
        }

        var cultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();

        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(defaultCulture),
            SupportedCultures = cultures,
            SupportedUICultures = cultures,
        };

        // ⚠️ 这里是 Clear() 而不是把自定义 provider 插到最前（官方为后者提供了
        //    AddInitialRequestCultureProvider）——因为要的不只是调顺序，而是**去掉**
        //    默认三个里的前两个：QueryStringRequestCultureProvider 让任何人都能用
        //    ?culture=xx 覆盖语言，CookieRequestCultureProvider 则与「语言由前端经请求头
        //    显式声明」的模型冲突。Add 是追加，不 Clear 的话自定义 provider 排到第四，
        //    要等前三个全部返回 null 才轮得到。
        options.RequestCultureProviders.Clear();
        options.RequestCultureProviders.Add(new KRequestLanguageHeaderCultureProvider());
        options.RequestCultureProviders.Add(new AcceptLanguageHeaderRequestCultureProvider());

        return options;
    }
}
