namespace Kimi.AppKit.Core.Hosting;

/// <summary>
/// 按当前页面的协议（http / https）自适应选择下游服务地址。
/// </summary>
/// <remarks>
/// 【它解决的真问题】同一份前端部署要同时服务两种访问方式：
/// 车间现场的机器脱离域环境、不信任企业根证书，只能走 http；办公网在域内走 https。
/// 二者共用同一份配置，下游地址（文件服务、看图服务）就必须按页面协议选。
///
/// 【为什么不能只配 https】https 页面加载 http 资源会被浏览器按 mixed content 拦掉；
/// 而 http 页面去访问 https 下游，在不信任根证书的机器上直接报证书错误。两个方向都不通。
///
/// ⚠️ 资源型下游地址一律走这个函数，**禁止在代码里硬编码 `https://`**。
/// </remarks>
public static class SchemeUrlResolver
{
    /// <summary>
    /// 按 <paramref name="appBaseAddress"/> 的协议选地址；缺失一端时回退另一端，
    /// 两端皆空返回空串（保证至少不抛）。
    /// </summary>
    public static string Resolve(string? appBaseAddress, string? httpsUrl, string? httpUrl)
    {
        var isHttps = appBaseAddress?.StartsWith("https", StringComparison.OrdinalIgnoreCase) ?? false;
        return isHttps
            ? httpsUrl ?? httpUrl ?? string.Empty
            : httpUrl ?? httpsUrl ?? string.Empty;
    }
}
