namespace Kimi.AppKit.Core.Contracts;

/// <summary>
/// 批量导入的结果：成功多少行、哪些行失败、为什么。
/// </summary>
/// <remarks>
/// 【为什么不是 <c>bool</c>】前身的导入端点返回 <c>bool</c>，任一行出错就整体抛异常。
/// 用户看到的是一个「导入失败」的红框，**不知道是第几行、也不知道为什么**——
/// 于是只能把几百行的表逐段二分重传去定位。
///
/// 【⚠️ 行号是"人看到的行号"，不是数组下标】<see cref="KImportError.RowNumber"/> 从 1 开始
/// 且**包含表头**，与用户在 Excel 左侧看到的行号一致。
/// 用下标会让「第 7 行有问题」在 Excel 里指到第 6 行，比不给行号更糟。
/// </remarks>
/// <param name="SucceededCount">成功写入的行数。</param>
/// <param name="Errors">失败的行。空列表表示全部成功。</param>
public sealed record KImportReport(int SucceededCount, IReadOnlyList<KImportError> Errors)
{
    /// <summary>全部成功。</summary>
    public static KImportReport Success(int count) => new(count, []);

    /// <summary>是否一行都没失败。</summary>
    public bool IsCompleteSuccess => Errors.Count == 0;

    /// <summary>失败行数。</summary>
    public int FailedCount => Errors.Count;
}

/// <summary>导入过程中单独一行的失败原因。</summary>
/// <param name="RowNumber">出错的行号，从 1 开始且含表头，与 Excel 左侧显示一致。</param>
/// <param name="Reason">失败原因，要能直接展示给最终用户。</param>
public sealed record KImportError(int RowNumber, string Reason);
