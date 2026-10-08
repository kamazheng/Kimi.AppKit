using System.IO.Compression;
using System.Xml.Linq;
using Kimi.AppKit.Web.Excel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kimi.AppKit.Tests.Web.Excel;

/// <summary>
/// <see cref="IExcelService"/> 的行为特征测试（T1-T8，见 S1-imagesharp-decision.md §4）。
/// </summary>
/// <remarks>
/// 只经 <c>AddAppKitExcel()</c> 从容器取服务、不引用实现类型，所以换底层库不改断言。
/// xlsx 的结构断言（sheet 名、加粗、列宽）直接读 zip 内的 XML，不依赖任何 Excel 库。
/// </remarks>
public sealed class ExcelServiceTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public enum Color { Red, Green }

    public sealed class Row
    {
        public string Text { get; set; } = "";
        public int I { get; set; }
        public long L { get; set; }
        public decimal M { get; set; }
        public double D { get; set; }
        public bool B { get; set; }
        public Color C { get; set; }
        public DateTime T { get; set; }
        public int? Nullable { get; set; }
        public List<string> Tags { get; set; } = [];
    }

    public sealed class GuidRow
    {
        public Guid Id { get; set; }
        public DateTimeOffset At { get; set; }
    }

    public sealed class A { public string Value { get; set; } = ""; }
    public sealed class Cased { public string Name { get; set; } = ""; public int Qty { get; set; } = 7; }
    public sealed class Extra { public string Name { get; set; } = ""; public string Other { get; set; } = ""; }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // xlsx 的 <col width> 是「字符数 + 约 0.71 的单元格内边距」，Excel 界面显示的是不含边距的值。
    // 所以断言 0 <= 实际 - 目标 < 1：少于目标（边距丢失）或多出整 1 都会红。
    private static void AssertWidth(double expected, byte[] xlsx)
    {
        var delta = ColumnWidth(xlsx) - expected;
        Assert.InRange(delta, 0, 0.99999);
    }

    private static IExcelService Service() =>
        new ServiceCollection().AddAppKitExcel().BuildServiceProvider().GetRequiredService<IExcelService>();

    private static XDocument Part(byte[] xlsx, string path)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx));
        var entry = zip.GetEntry(path) ?? throw new InvalidOperationException($"xlsx 缺少 {path}");
        using var s = entry.Open();
        return XDocument.Load(s);
    }

    private static Stream Open(byte[] xlsx) => new MemoryStream(xlsx);

    [Fact]
    public void T1_导出是合法xlsx且sheet名为传入值()
    {
        var bytes = Service().Export([new A { Value = "x" }], "我的表");

        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
        Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
        var sheets = Part(bytes, "xl/workbook.xml").Descendants(Ns + "sheet").ToList();
        Assert.Equal("我的表", Assert.Single(sheets).Attribute("name")!.Value);
    }

    [Fact]
    public void T2_表头为标量属性名且加粗_集合属性不导出()
    {
        var bytes = Service().Export([new Row()]);

        var sheet = Part(bytes, "xl/worksheets/sheet1.xml");
        var strings = Part(bytes, "xl/sharedStrings.xml").Descendants(Ns + "si")
            .Select(si => string.Concat(si.Descendants(Ns + "t").Select(t => t.Value))).ToList();
        var headerCells = sheet.Descendants(Ns + "row").First().Elements(Ns + "c").ToList();
        var headers = headerCells.Select(c => c.Attribute("t")?.Value == "s"
            ? strings[int.Parse(c.Element(Ns + "v")!.Value)]
            : c.Descendants(Ns + "t").First().Value).ToList();

        Assert.Equal(["Text", "I", "L", "M", "D", "B", "C", "T", "Nullable"], headers);

        // 表头单元格的样式 → cellXfs[s] → fonts[fontId] 含 <b/>
        var styles = Part(bytes, "xl/styles.xml");
        var fonts = styles.Descendants(Ns + "fonts").First().Elements(Ns + "font").ToList();
        var xfs = styles.Descendants(Ns + "cellXfs").First().Elements(Ns + "xf").ToList();
        foreach (var cell in headerCells)
        {
            var xf = xfs[int.Parse(cell.Attribute("s")!.Value)];
            Assert.NotNull(fonts[int.Parse(xf.Attribute("fontId")!.Value)].Element(Ns + "b"));
        }
    }

    [Fact]
    public void T3_往返逐字段相等()
    {
        var svc = Service();
        var src = new Row
        {
            Text = "中文 text", I = -5, L = 9_000_000_000, M = 123.45m, D = 3.14159, B = true,
            C = Color.Green, T = new DateTime(2026, 10, 8, 13, 14, 15), Nullable = null,
        };
        var src2 = new Row { Text = "b", I = 2, L = 3, M = 0.5m, D = 1.5, B = false, C = Color.Red, T = new DateTime(2020, 1, 2), Nullable = 42 };

        var back = svc.Import<Row>(Open(svc.Export([src, src2])));

        Assert.Equal(2, back.Count);
        foreach (var (e, a) in new[] { (src, back[0]), (src2, back[1]) })
        {
            Assert.Equal(e.Text, a.Text); Assert.Equal(e.I, a.I); Assert.Equal(e.L, a.L);
            Assert.Equal(e.M, a.M); Assert.Equal(e.D, a.D); Assert.Equal(e.B, a.B);
            Assert.Equal(e.C, a.C); Assert.Equal(e.T, a.T); Assert.Equal(e.Nullable, a.Nullable);
        }
    }

    [Fact]
    public void T4_表头不区分大小写_多余列忽略_缺列保持默认()
    {
        var svc = Service();
        var bytes = svc.Export([new Extra { Name = "n", Other = "o" }]);
        var upper = svc.Import<Cased>(Open(bytes));
        Assert.Equal("n", Assert.Single(upper).Name);
        Assert.Equal(7, upper[0].Qty); // Qty 列不存在 → 构造函数默认值

        // 表头大小写不同：导出 Cased，再以全小写属性名的类型导入
        var bytes2 = svc.Export([new Cased { Name = "z", Qty = 3 }]);
        Assert.Equal(3, Assert.Single(svc.Import<LowerCased>(Open(bytes2))).qty);
    }

    public sealed class LowerCased { public string name { get; set; } = ""; public int qty { get; set; } }

    [Fact]
    public void T5_只有表头为空列表()
    {
        var svc = Service();
        Assert.Empty(svc.Import<A>(Open(svc.Export(Array.Empty<A>()))));
    }

    private static byte[] TwoSheets()
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var s1 = wb.Worksheets.Add("first");
        s1.Cell(1, 1).Value = "Value"; s1.Cell(2, 1).Value = "from-first";
        var s2 = wb.Worksheets.Add("second");
        s2.Cell(1, 1).Value = "Value"; s2.Cell(2, 1).Value = "from-second-1"; s2.Cell(3, 1).Value = "from-second-2";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    [Fact]
    public void T5_sheetIndex选择第N个sheet()
    {
        var svc = Service();
        Assert.Equal("from-first", Assert.Single(svc.Import<A>(Open(TwoSheets()))).Value);
        Assert.Equal(["from-second-1", "from-second-2"],
            svc.Import<A>(Open(TwoSheets()), sheetIndex: 1).Select(a => a.Value));
    }

    [Fact]
    public void T5_sheetIndex越界抛ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Service().Import<A>(Open(TwoSheets()), sheetIndex: 5));
    }

    [Fact]
    public void T6_不可寻址流可导入()
    {
        var svc = Service();
        var bytes = svc.Export([new A { Value = "ok" }]);
        var back = svc.Import<A>(new NonSeekableStream(new MemoryStream(bytes)));
        Assert.Equal("ok", Assert.Single(back).Value);
    }

    [Theory]
    [InlineData("汉汉汉汉汉汉汉汉汉汉", 22)]
    [InlineData("abc", 8)]
    public void T7_列宽按显示宽度_CJK计2_下限8(string value, int expected) =>
        AssertWidth(expected, Service().Export([new A { Value = value }]));

    [Fact]
    public void T7_列宽上限60() =>
        AssertWidth(60, Service().Export([new A { Value = new string('a', 100) }]));

    private static double ColumnWidth(byte[] xlsx) =>
        double.Parse(Part(xlsx, "xl/worksheets/sheet1.xml").Descendants(Ns + "col").First().Attribute("width")!.Value,
            System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void T8_Guid与DateTimeOffset往返相等()
    {
        var svc = Service();
        var src = new GuidRow { Id = Guid.NewGuid(), At = new DateTimeOffset(2026, 10, 8, 1, 2, 3, TimeSpan.Zero) };

        var back = Assert.Single(svc.Import<GuidRow>(Open(svc.Export([src]))));

        Assert.Equal(src.Id, back.Id);
        Assert.Equal(src.At, back.At);
    }
}
