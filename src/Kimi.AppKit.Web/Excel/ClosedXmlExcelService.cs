using System.Globalization;
using System.Reflection;
using ClosedXML.Excel;

namespace Kimi.AppKit.Web.Excel;

/// <summary>基于 ClosedXML（MIT）的 <see cref="IExcelService"/> 实现（.xlsx）。实现选型不进公开 API。</summary>
internal sealed class ClosedXmlExcelService : IExcelService
{
    /// <inheritdoc />
    public byte[] Export<T>(IEnumerable<T> items, string sheetName = "Sheet1")
    {
        var properties = ExportableProperties<T>();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        var widths = new int[properties.Length];

        for (var i = 0; i < properties.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = properties[i].Name;
            cell.Style.Font.Bold = true;
            widths[i] = ExcelColumnWidth.DisplayWidth(properties[i].Name);
        }

        var row = 2;
        foreach (var item in items)
        {
            for (var i = 0; i < properties.Length; i++)
            {
                var cell = sheet.Cell(row, i + 1);
                SetCellValue(cell, properties[i].GetValue(item));
                widths[i] = Math.Max(widths[i], ExcelColumnWidth.DisplayWidth(cell.GetFormattedString()));
            }

            row++;
        }

        for (var i = 0; i < properties.Length; i++)
            sheet.Column(i + 1).Width = ExcelColumnWidth.Clamp(widths[i]);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<T> Import<T>(Stream content, int sheetIndex = 0) where T : new()
    {
        ArgumentNullException.ThrowIfNull(content);

        // OpenXml 要求可寻址流；HTTP 上传流等不可寻址的先落到内存。
        var seekable = content;
        if (!content.CanSeek)
        {
            seekable = new MemoryStream();
            content.CopyTo(seekable);
            seekable.Position = 0;
        }

        using var workbook = new XLWorkbook(seekable);
        var sheet = workbook.Worksheet(sheetIndex + 1);
        var used = sheet.RangeUsed();
        if (used is null || used.RowCount() < 2) return [];

        var headerRow = used.FirstRow();
        var columns = MapColumns<T>(headerRow);
        var results = new List<T>();

        foreach (var row in used.Rows().Skip(1))
        {
            var item = new T();
            foreach (var (property, column) in columns)
            {
                var cell = row.Cell(column);
                if (cell.IsEmpty()) continue;

                var value = ReadCellValue(cell, property.PropertyType);
                if (value is not null) property.SetValue(item, value);
            }

            results.Add(item);
        }

        return results;
    }

    private static Dictionary<PropertyInfo, int> MapColumns<T>(IXLRangeRow headerRow)
    {
        var map = new Dictionary<PropertyInfo, int>();
        foreach (var property in ExportableProperties<T>())
        {
            for (var c = 1; c <= headerRow.CellCount(); c++)
            {
                var header = headerRow.Cell(c).GetString();
                if (!string.Equals(header, property.Name, StringComparison.OrdinalIgnoreCase)) continue;
                map[property] = c;
                break;
            }
        }

        return map;
    }

    private static PropertyInfo[] ExportableProperties<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && IsSimpleType(p.PropertyType))
            .ToArray();

    /// <summary>只导出标量属性，跳过集合与复杂类型——那些没有自然的单元格表示。</summary>
    private static bool IsSimpleType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
               || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(Guid);
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: return;
            case bool b: cell.Value = b; return;
            case DateTime dt: cell.Value = dt; return;
            case DateTimeOffset dto: cell.Value = dto.UtcDateTime; return;
            case decimal m: cell.Value = m; return;
            case long l: cell.Value = l; return;
            case double or float or int or short or byte:
                cell.Value = Convert.ToDouble(value, CultureInfo.InvariantCulture); return;
            default: cell.Value = value.ToString() ?? string.Empty; return;
        }
    }

    private static object? ReadCellValue(IXLCell cell, Type targetType)
    {
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var text = cell.GetString();

        if (t == typeof(string)) return text;
        if (t.IsEnum) return Enum.TryParse(t, text, ignoreCase: true, out var e) ? e : null;
        if (t == typeof(Guid)) return Guid.TryParse(text, out var g) ? g : null;
        if (t == typeof(DateTime)) return ReadDateTime(cell);
        if (t == typeof(DateTimeOffset))
            return ReadDateTime(cell) is { } dt
                ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
                : null;
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (t == typeof(bool)) return cell.DataType == XLDataType.Boolean ? cell.GetBoolean() : bool.Parse(text);

        return cell.DataType == XLDataType.Number
            ? Convert.ChangeType(cell.GetDouble(), t, CultureInfo.InvariantCulture)
            : Convert.ChangeType(text, t, CultureInfo.InvariantCulture);
    }

    private static DateTime? ReadDateTime(IXLCell cell) => cell.DataType switch
    {
        XLDataType.DateTime => cell.GetDateTime(),
        XLDataType.Number => DateTime.FromOADate(cell.GetDouble()),
        _ => null,
    };
}
