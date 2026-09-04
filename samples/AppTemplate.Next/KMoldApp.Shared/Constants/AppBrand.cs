namespace KMoldApp.Shared.Constants;

/// <summary>
/// 企业标识的**唯一真相源**。服务端与 WASM 客户端共用。
/// </summary>
/// <remarks>
/// 【为什么只有产品名】**企业名与 Logo 不在这里**——它们向身份服务要
/// （见 <c>KBrandingClient</c>）。一套部署里客户只该设一次企业标识，
/// 各服务各配一份必然出现两个名字并存，而那恰恰发生在客户最在意的地方。
///
/// 产品名反过来：它是**本应用自己的名字**，同一套 Auth 下的文件服务、MES、
/// 看图工具本来就该各叫各的，跟着身份服务的产品名走才是错的。
/// 它在 <c>dotnet new</c> 那一刻由 <c>sourceName</c> 确定，因此是常量。
/// </remarks>
public static class AppBrand
{
    /// <summary>产品名称。显示在顶栏与认证页。</summary>
    public const string ProductName = "KMoldApp";
}
