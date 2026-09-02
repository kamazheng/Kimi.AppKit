namespace Kimi.AppKit.Core.Abstractions;

/// <summary>
/// 当前操作者。审计、权限判定与「谁在编辑」之类的功能都从这里取身份。
/// </summary>
/// <remarks>
/// 【为什么必须是接口而不是静态访问器】前身模板有一个 <c>AppServicesHelper.Services</c>
/// 静态服务定位器，用来在任意位置取当前用户。实测它的 setter **全仓从未被赋值**，
/// 于是依赖它的接口永远返回 <c>"UserId:Anonymous"</c>——编译通过、运行不报错、值是错的。
/// 静态定位器把初始化顺序依赖藏了起来，出错时不是崩溃而是「看起来正常但值不对」。
///
/// 【实现方】服务端从 <c>HttpContext.User</c> 取；WASM 客户端从认证状态取；
/// 后台任务用一个固定的系统身份。三者语义不同，这正是它该是接口的理由。
/// </remarks>
public interface IKCurrentUser
{
    /// <summary>用于审计落库的用户标识。未认证时返回一个**可识别的哨兵值**而不是 null 或空串。</summary>
    /// <remarks>
    /// ⚠️ 取不到用户名时不要静默退化成 "System"。审计的全部价值就在「谁干的」，
    /// 把异常伪装成一次正常的系统操作，等于让审计表在最需要它的时候说谎。
    /// 取不到就让它抛，或者返回一个一眼能看出「这里出问题了」的值。
    /// </remarks>
    ValueTask<string> GetUserNameAsync(CancellationToken cancellationToken = default);

    /// <summary>是否已认证。</summary>
    ValueTask<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default);
}
