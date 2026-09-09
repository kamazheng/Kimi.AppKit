namespace Kimi.AppKit.Core.Contracts;

/// <summary>
/// 从服务端 <c>ProblemDetails</c> 还原出来的失败详情。
/// </summary>
/// <remarks>
/// 【为什么 <see cref="KResult"/> 不够用】它的 <c>Errors</c> 是一串字符串，
/// 表达不了「哪一条错误属于哪个字段」。而校验失败最有用的呈现方式是把消息挂回
/// 出错的那个输入框——丢进一条通知里的话，用户还得自己在表单上找是哪一项。
///
/// 【⚠️ <see cref="Message"/> 是已经清理过的人话，不是原始正文】
/// 服务端的 <c>detail</c> 已经写清了「是什么、为什么、怎么办」，
/// 但 .NET 的 <c>ArgumentException</c> 会在末尾追加 <c>(Parameter 'xxx')</c>——
/// 那是给开发者看的，出现在给仓管的提示里只会造成困惑。
/// </remarks>
/// <param name="Status">HTTP 状态码。</param>
/// <param name="Message">给用户看的失败原因，**永不为空串**。</param>
/// <param name="FieldErrors">字段名 → 该字段的错误消息。没有字段级错误时为空字典。</param>
public sealed record KProblem(
    int Status,
    string Message,
    IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors)
{
    /// <summary>没有字段级错误。</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoFieldErrors =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>把字段级错误摊平成一串「字段：消息」，供只能显示文本的场合使用。</summary>
    /// <remarks>
    /// ⚠️ 没有字段级错误时返回 <see cref="Message"/> 本身，**不返回空列表**——
    /// 返回空的话调用方很容易写出「有错误才提示」的判断，于是这类失败变成完全静默。
    /// </remarks>
    public IReadOnlyList<string> ToMessages() =>
        FieldErrors.Count == 0
            ? [Message]
            : [.. FieldErrors.SelectMany(pair => pair.Value.Select(m => $"{pair.Key}：{m}"))];
}
