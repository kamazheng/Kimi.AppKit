using System.Text;

namespace Kimi.AppKit.Web.Excel;

/// <summary>
/// 导出列宽：按文本「显示宽度」计算，**不按字体量宽**。
/// </summary>
/// <remarks>
/// 容器镜像（aspnet:10.0）没有系统字体，按字体量宽的实现会对 CJK 给出被严重低估的宽度，
/// 且结果随运行环境而变。这里 CJK/全角计 2、其余计 1，列宽 = clamp(最大显示宽度 + 2, 8, 60)，
/// 确定性、可单测。
/// </remarks>
internal static class ExcelColumnWidth
{
    private const int Padding = 2;
    private const int Min = 8;
    private const int Max = 60;

    /// <summary>文本显示宽度（多行取最长一行）。</summary>
    public static int DisplayWidth(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var best = 0;
        var line = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\n') { best = Math.Max(best, line); line = 0; continue; }
            line += IsWide(rune.Value) ? 2 : 1;
        }

        return Math.Max(best, line);
    }

    /// <summary>加边距并夹到 [8, 60]。</summary>
    public static double Clamp(int maxDisplayWidth) => Math.Clamp(maxDisplayWidth + Padding, Min, Max);

    private static bool IsWide(int c) =>
        c is >= 0x1100 and <= 0x115F or >= 0x2E80 and <= 0xA4CF or >= 0xAC00 and <= 0xD7A3
            or >= 0xF900 and <= 0xFAFF or >= 0xFE30 and <= 0xFE6F or >= 0xFF00 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6 or >= 0x1F300 and <= 0x1F64F or >= 0x1F900 and <= 0x1F9FF
            or >= 0x20000 and <= 0x3FFFD;
}
