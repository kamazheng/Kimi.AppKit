namespace Kimi.AppKit.Web.Excel;

/// <summary>Excel 导入导出。</summary>
/// <remarks>
/// 【边界】只负责「标量属性 ↔ 单 sheet 表格」。复杂带样式报表（多 sheet、合并单元格、条件格式）
/// 由应用直引 ClosedXML（版本经 CPM 与 AppKit 一致），**禁止引 NPOI**。
///
/// 【实现选型：ClosedXML（MIT）】实现类型为 internal，只经 <c>AddAppKitExcel()</c> 注册。
/// 不用 NPOI：2.7.x 依赖的 SixLabors.ImageSharp 2.x 全线有未修复公告，2.8+ 改用 OSMF 付费协议。
/// 详见仓库根 <c>Directory.Packages.props</c>。
/// 导出列宽按显示宽度计算（CJK 计 2，范围 8-60），不依赖系统字体，容器内行为一致。
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
    /// <param name="content">Excel 文件内容；不可寻址的流会先拷入内存。</param>
    /// <param name="sheetIndex">工作表索引，默认第一个。</param>
    IReadOnlyList<T> Import<T>(Stream content, int sheetIndex = 0) where T : new();
}
