using System.Reflection;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace Kimi.AppKit.Web.Excel;

/// <summary>基于 NPOI 的 <see cref="IExcelService"/> 实现（.xlsx）。</summary>
public sealed class NpoiExcelService : IExcelService
{
    /// <inheritdoc />
    public byte[] Export<T>(IEnumerable<T> items, string sheetName = "Sheet1")
    {
        var properties = ExportableProperties<T>();

        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet(sheetName);

        var header = sheet.CreateRow(0);
        var headerStyle = workbook.CreateCellStyle();
        var boldFont = workbook.CreateFont();
        boldFont.IsBold = true;
        headerStyle.SetFont(boldFont);

        for (var i = 0; i < properties.Length; i++)
        {
            var cell = header.CreateCell(i);
            cell.SetCellValue(properties[i].Name);
            cell.CellStyle = headerStyle;
        }

        var rowIndex = 1;
        foreach (var item in items)
        {
            var row = sheet.CreateRow(rowIndex++);
            for (var i = 0; i < properties.Length; i++)
            {
                SetCellValue(row.CreateCell(i), properties[i].GetValue(item));
            }
        }

        for (var i = 0; i < properties.Length; i++) sheet.AutoSizeColumn(i);

        using var stream = new MemoryStream();
        workbook.Write(stream, leaveOpen: true);
        return stream.ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<T> Import<T>(Stream content, int sheetIndex = 0) where T : new()
    {
        using var workbook = new XSSFWorkbook(content);
        var sheet = workbook.GetSheetAt(sheetIndex);
        if (sheet is null || sheet.LastRowNum < 1) return [];

        var headerRow = sheet.GetRow(sheet.FirstRowNum);
        var columnIndexByProperty = new Dictionary<PropertyInfo, int>();

        foreach (var property in ExportableProperties<T>())
        {
            for (var c = headerRow.FirstCellNum; c < headerRow.LastCellNum; c++)
            {
                var header = headerRow.GetCell(c)?.StringCellValue;
                if (string.Equals(header, property.Name, StringComparison.OrdinalIgnoreCase))
                {
                    columnIndexByProperty[property] = c;
                    break;
                }
            }
        }

        var results = new List<T>();
        for (var r = sheet.FirstRowNum + 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null) continue;

            var item = new T();
            foreach (var (property, columnIndex) in columnIndexByProperty)
            {
                var cell = row.GetCell(columnIndex);
                if (cell is null) continue;

                var value = ReadCellValue(cell, property.PropertyType);
                if (value is not null) property.SetValue(item, value);
            }

            results.Add(item);
        }

        return results;
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

    private static void SetCellValue(ICell cell, object? value)
    {
        switch (value)
        {
            case null: return;
            case bool b: cell.SetCellValue(b); return;
            case DateTime dt: cell.SetCellValue(dt); return;
            case DateTimeOffset dto: cell.SetCellValue(dto.UtcDateTime); return;
            case double or float or decimal or int or long or short or byte:
                cell.SetCellValue(Convert.ToDouble(value)); return;
            default: cell.SetCellValue(value.ToString()); return;
        }
    }

    private static object? ReadCellValue(ICell cell, Type targetType)
    {
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (t == typeof(string)) return cell.ToString();
        if (t.IsEnum) return Enum.TryParse(t, cell.ToString(), ignoreCase: true, out var e) ? e : null;
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset))
            return cell.CellType == CellType.Numeric ? cell.DateCellValue : null;

        var text = cell.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : Convert.ChangeType(text, t);
    }
}
