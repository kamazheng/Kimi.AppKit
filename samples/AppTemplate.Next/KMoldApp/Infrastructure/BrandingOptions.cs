namespace KMoldApp.Infrastructure;

/// <summary>
/// 企业标识。显示在登录页与二维码卡片上。
/// </summary>
/// <remarks>
/// 【为什么是配置而不是落库】<c>Kimi.KMold.Auth</c> 那套品牌是 433 行：落库、管理页、
/// 上传校验、缓存失效。那是**身份服务**的需要——它一套部署服务多个客户，品牌必须运行期可改。
/// 本模板生成的是**某一个客户自己的应用**，品牌在 <c>dotnet new</c> 那一刻就定了，
/// 之后改一次配置重启即可。为这点需求背一套管理系统不划算。
/// 真出现「非技术人员要自助换 Logo」的需求，再照 Auth 那套加，届时本类型是它的读取口径。
///
/// ⚠️ Logo 走静态文件而不是数据库：一客户一组容器、无共享卷，
/// 但 Logo 是**构建期就确定**的资产，跟着镜像走即可，不像 Auth 那样需要运行期上传。
/// </remarks>
public sealed class BrandingOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Branding";

    /// <summary>企业名称。</summary>
    public string CompanyName { get; set; } = "Your Company Ltd.";

    /// <summary>产品名称。</summary>
    public string ProductName { get; set; } = "KMoldApp";

    /// <summary>
    /// Logo 的站内路径，如 <c>/images/logo.png</c>。
    /// </summary>
    /// <remarks>
    /// ⚠️ **不要配 SVG**。SVG 可内嵌脚本，直接访问时会在本站源上执行，
    /// 等于把一个站内 XSS 写入口交给了能改配置的人。用 PNG。
    /// ⚠️ 留空则回落到内置字标（企业名首字）。不要用三道横线做占位——
    /// 那和汉堡菜单图标一模一样，用户会去点它，点不动就以为界面坏了。
    /// </remarks>
    public string? LogoPath { get; set; }

    /// <summary>内置字标上的字符。</summary>
    public string Initial =>
        string.IsNullOrWhiteSpace(CompanyName) ? "?" : CompanyName.Trim()[..1].ToUpperInvariant();
}
