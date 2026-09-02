namespace Kimi.AppKit.Core.Abstractions;

/// <summary>
/// 运行环境。服务端与 WASM 客户端各有一套互不兼容的宿主环境抽象
/// （<c>IWebHostEnvironment</c> vs <c>IWebAssemblyHostEnvironment</c>），这里收口成一个。
/// </summary>
public interface IKEnvironment
{
    /// <summary>环境名，如 Development / Staging / Production。</summary>
    string Name { get; }

    /// <summary>
    /// 是否生产环境。
    /// </summary>
    /// <remarks>
    /// ⚠️ **不要让安全行为只取决于这一个布尔值。** 前身模板的权限绕过开关是
    /// 「非生产默认全开、注入全部角色」，于是客户把 <c>ASPNETCORE_ENVIRONMENT</c>
    /// 设成 Staging/UAT 或者干脆没设对，任何已登录用户就自动拿到全部角色——
    /// 而且默认值生效时是**沉默的**，没有任何日志或界面提示。
    /// 安全开关应当默认关闭、显式打开，且处于打开状态时必须有持续可见的信号。
    /// </remarks>
    bool IsProduction { get; }
}
