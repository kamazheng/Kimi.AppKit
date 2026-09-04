namespace Kimi.AppKit.Core.Contracts;

/// <summary>
/// 企业标识快照。服务端与 WASM 客户端共用。
/// </summary>
/// <remarks>
/// 【为什么向 Auth 要而不是各自配】一套部署里客户只该设一次企业名与 Logo。
/// 各服务各配一份的话，改名要改好几处，漏一处就出现**两个名字并存**——
/// 而这恰恰发生在客户最在意的地方：登录页和顶栏。
///
/// ⚠️ **产品名不在这里**，它恒取本地配置。产品名是「这个应用叫什么」，
/// 是本应用自己的属性，不该跟着身份服务的产品名走——
/// 同一套 Auth 下的文件服务、MES、看图工具本来就该各叫各的。
/// </remarks>
/// <param name="CompanyName">企业名称。</param>
/// <param name="LogoUrl">
/// Logo 的绝对地址。为 null 时消费方应回落到内置字标（企业名首字）。
/// ⚠️ 不要用三道横线之类的图形占位——那和汉堡菜单图标一模一样，
/// 用户会去点它，点不动就以为界面坏了。
/// </param>
public sealed record KBranding(string CompanyName, string? LogoUrl)
{
    /// <summary>Auth 不可达时的兜底值。</summary>
    /// <remarks>
    /// ⚠️ 兜底必须是**能看的**，不能是空串或「未知」。品牌只是装饰，
    /// 身份服务抖一下不该让每个页面的顶栏变成一片空白。
    /// </remarks>
    public static readonly KBranding Fallback = new("Your Company Ltd.", null);
}
