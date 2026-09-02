namespace Kimi.AppKit.Core.Abstractions;

using Kimi.AppKit.Core.Contracts;

/// <summary>
/// 给用户的即时反馈（吐司/横幅）。
/// </summary>
/// <remarks>
/// 【为什么收口】前身模板里散着 61 处裸调 <c>Snackbar.Add(...)</c> 与 8 处
/// 「成功就绿、失败就红」的三元表达式，文案、颜色、停留时长各写各的。
/// 收成一个接口之后，「操作反馈长什么样」变成一处决定。
///
/// 【⚠️ 每个动作都必须有可见反馈】幂等情形（「他已经是管理员了」）不要走 <see cref="Ok"/>：
/// 界面会照常提示「已完成」并清空输入框，而列表一行没变，用户看到的就是「点了没反应」。
/// </remarks>
public interface IKNotify
{
    /// <summary>成功。</summary>
    void Ok(string message);

    /// <summary>警告：操作完成了，但有需要注意的地方。</summary>
    void Warn(string message);

    /// <summary>失败。</summary>
    void Fail(string message);

    /// <summary>
    /// 按 <see cref="KResult"/> 自动分流：成功报 <paramref name="successText"/>，
    /// 失败把 <see cref="KResult.Errors"/> 展示出来。收敛掉散落各处的三元表达式。
    /// </summary>
    void Result(KResult result, string successText);
}
