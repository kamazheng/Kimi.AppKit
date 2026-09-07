namespace Kimi.AppKit.Components.Auth;

/// <summary>
/// 认证页（登录 / 二维码打印 / 错误页）的宿主参数。
/// </summary>
/// <remarks>
/// 【为什么用 options 而不是组件参数】这些页面的外壳是 Blazor **布局**，
/// 由 <c>@layout</c> 指定，拿不到调用方传的参数。而外壳里要引消费方自己的
/// 样式包与图标——那是每个应用都不同的东西。options 是唯一能在布局里取到它们的口径。
///
/// ⚠️ 消费方**必须**配 <see cref="AppStylesheet"/>。Blazor 的组件 scoped 样式包
/// （<c>{程序集名}.styles.css</c>）名字随程序集走，包里猜不出来；
/// 漏配时页面照常渲染，只是所有 <c>*.razor.css</c> 里的样式**全部丢失且不报错**。
/// </remarks>
public sealed class KAuthPageOptions
{
    /// <summary>
    /// 消费方的组件 scoped 样式包路径，形如 <c>MyApp.styles.css</c>。
    /// </summary>
    public string? AppStylesheet { get; set; }

    /// <summary>消费方的应用级样式表，形如 <c>app.css</c>。</summary>
    public string? AppCss { get; set; } = "app.css";

    /// <summary>站点图标路径。</summary>
    public string FaviconPath { get; set; } = "favicon.png";

    /// <summary>
    /// 产品名称。显示在认证页品牌区的第二行。
    /// </summary>
    /// <remarks>
    /// ⚠️ 与企业名不同：企业名向身份服务要（一套部署只设一次），
    /// 产品名是**本应用自己的名字**——同一套 IdP 下的各个应用本就该各叫各的。
    /// </remarks>
    public string ProductName { get; set; } = "Application";

    /// <summary>页面语言，写进 <c>&lt;html lang&gt;</c>。</summary>
    public string Language { get; set; } = "zh-CN";
}
