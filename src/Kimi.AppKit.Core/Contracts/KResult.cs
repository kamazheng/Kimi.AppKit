namespace Kimi.AppKit.Core.Contracts;

/// <summary>
/// 操作结果。用于「这次动作成功了没、失败原因是什么」这类需要在界面上给出可见反馈的场景。
/// </summary>
/// <remarks>
/// 【为什么不用抛异常表达失败】业务级失败（校验不通过、状态机不允许、并发冲突）是预期内的控制流，
/// 用异常表达会让调用方被迫 try/catch，且异常栈的开销与噪音都不划算。
/// 真正的意外（数据库连不上、空引用）仍然抛异常，由全局异常处理接管。
///
/// 【⚠️ 幂等操作不要返回 Ok】「他已经是管理员了」这类情形若返回成功，界面会照常提示「已完成」
/// 并清空输入框，而列表一行没变——用户看到的就是「点了没反应」。这类情形应带上说明性的 Errors，
/// 或由调用方显式区分。
/// </remarks>
public readonly record struct KResult
{
    private static readonly IReadOnlyList<string> NoErrors = [];

    private KResult(bool succeeded, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Errors = errors;
    }

    /// <summary>操作是否成功。</summary>
    public bool Succeeded { get; }

    /// <summary>失败原因。成功时为空列表，**永不为 null**，调用方可直接遍历。</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>成功。</summary>
    public static KResult Ok() => new(true, NoErrors);

    /// <summary>失败，带一条或多条原因。</summary>
    public static KResult Fail(params string[] errors) =>
        new(false, errors is { Length: > 0 } ? errors : ["操作失败"]);

    /// <summary>失败，带一组原因。</summary>
    public static KResult Fail(IEnumerable<string> errors)
    {
        var list = errors as IReadOnlyList<string> ?? errors.ToArray();
        return new(false, list.Count > 0 ? list : ["操作失败"]);
    }

    /// <summary>把多个结果合并成一个：全部成功才算成功，失败原因累加。</summary>
    public static KResult Combine(IEnumerable<KResult> results)
    {
        var errors = results.Where(r => !r.Succeeded).SelectMany(r => r.Errors).ToArray();
        return errors.Length == 0 ? Ok() : Fail(errors);
    }
}
