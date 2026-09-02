namespace Kimi.AppKit.Web.Excel;

/// <summary>Excel 导入导出。</summary>
/// <remarks>
/// 【实现选型：NPOI，停在 2.7.1】见仓库根 <c>Directory.Packages.props</c> 里的详细说明——
/// NPOI 2.8.0 起改用 OSMF 维护费协议，营利使用且年营收 ≥ US$10,000 需付费；
/// 2.7.1 仍是 Apache-2.0。这是许可决策不是技术决策，升级前必须重新评估。
/// </remarks>
public interface IExcelService
{
    /// <summary>
    /// 把一组对象导出成 Excel 文件字节流。
    /// </summary>
    /// <typeparam name="T">行类型，其公开可读属性即导出的列。</typeparam>
    /// <param name="items">数据行。</param>
    /// <param name="sheetName">工作表名。</param>
    byte[] Export<T>(IEnumerable<T> items, string sheetName = "Sheet1");

    /// <summary>
    /// 从 Excel 文件解析出一组对象。
    /// </summary>
    /// <remarks>
    /// ⚠️ 列名与属性名的匹配**不区分大小写**但要求完全一致；导出时用什么表头，
    /// 导入时就期望什么表头。不匹配的列会被忽略，不匹配的必需属性保持默认值——
    /// 这不算错误，调用方如果需要"缺列必须报错"的语义，应在导入后自行校验。
    /// </remarks>
    /// <typeparam name="T">行类型，须有无参构造函数。</typeparam>
    /// <param name="content">Excel 文件内容。</param>
    /// <param name="sheetIndex">工作表索引，默认第一个。</param>
    IReadOnlyList<T> Import<T>(Stream content, int sheetIndex = 0) where T : new();
}
