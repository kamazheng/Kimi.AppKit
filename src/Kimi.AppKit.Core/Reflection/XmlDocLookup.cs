using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Kimi.AppKit.Core.Reflection;

/// <summary>
/// 读取编译产出的 XML 文档注释，把 <c>&lt;summary&gt;</c> 拿来当界面提示文字用
/// （表单字段的 helper text、枚举下拉项的 tooltip、数据库列注释）。
/// </summary>
/// <remarks>
/// 【它解决的问题】字段的业务含义已经写在实体的 XML 注释里了。没有这个工具时，
/// 同一句说明要在实体注释、表单 HelperText、数据库列注释三处各写一遍，然后各自腐化。
///
/// 【⚠️ 必须先 Init，否则所有查询静默返回 null】没初始化时不抛异常——
/// 界面只是「没有提示文字」，看起来像没配，而不是像出错。
/// 服务端在启动时调 <see cref="TryInitFromBaseDirectory"/>，
/// WASM 端调 <see cref="InitAsync"/> 从 wwwroot 拉。
///
/// 【⚠️ 要让编译产出 XML】消费方工程需要开
/// <c>&lt;GenerateDocumentationFile&gt;true&lt;/GenerateDocumentationFile&gt;</c>，
/// 否则根本没有文件可读。
/// </remarks>
public static partial class XmlDocLookup
{
    private static XDocument? _doc;
    private static readonly Lock Gate = new();

    /// <summary>
    /// Blazor WebAssembly 端初始化：从 wwwroot 用 <see cref="HttpClient"/> 拉取 XML 文档。
    /// </summary>
    /// <param name="httpClient">指向应用根的 HttpClient。</param>
    /// <param name="xmlPath">XML 文件相对 wwwroot 的路径，例如 <c>"MyApp.Shared.xml"</c>。</param>
    /// <remarks>
    /// ⚠️ <paramref name="xmlPath"/> **没有默认值**，必须显式传。
    /// 前身实现把它默认成了另一个项目的文件名（<c>"CDU_TMES.Shared.xml"</c>）——
    /// 换个项目照抄这行代码，拉取 404、静默失败，界面上所有提示文字全部消失，且不报错。
    /// </remarks>
    public static async Task InitAsync(HttpClient httpClient, string xmlPath)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlPath);

        var xml = await httpClient.GetStringAsync(xmlPath).ConfigureAwait(false);
        var doc = XDocument.Parse(xml);
        lock (Gate) _doc = doc;
    }

    /// <summary>服务端初始化：从绝对路径加载 XML 文档。</summary>
    public static void InitFromFile(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"未找到 XML 文档文件：{fullPath}", fullPath);

        var doc = XDocument.Load(fullPath);
        lock (Gate) _doc = doc;
    }

    /// <summary>
    /// 在程序集所在目录（含子目录）里找并加载指定文件名的 XML 文档；成功返回 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// 返回 bool 而不抛异常：XML 注释是**锦上添花**的能力，
    /// 缺了它应用照常工作（只是没有提示文字），不该让整个应用起不来。
    /// </remarks>
    public static bool TryInitFromBaseDirectory(string xmlFileName)
    {
        if (string.IsNullOrWhiteSpace(xmlFileName)) return false;

        var baseDir = AppContext.BaseDirectory;
        var directPath = Path.Combine(baseDir, xmlFileName);
        if (File.Exists(directPath))
        {
            InitFromFile(directPath);
            return true;
        }

        foreach (var path in Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories))
        {
            if (!string.Equals(Path.GetFileName(path), xmlFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                InitFromFile(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
            {
                continue;   // 这个文件读不了就找下一个同名文件
            }
        }

        return false;
    }

    /// <summary>类型的 <c>&lt;summary&gt;</c>。未初始化或没有注释时返回 null。</summary>
    public static string? GetXmlSummary(this Type type) =>
        GetSummaryById($"T:{type.FullName}");

    /// <summary>属性的 <c>&lt;summary&gt;</c>。</summary>
    public static string? GetXmlSummary(this PropertyInfo prop) =>
        GetSummaryById($"P:{prop.DeclaringType!.FullName}.{prop.Name}");

    /// <summary>字段的 <c>&lt;summary&gt;</c>（枚举成员也是字段）。</summary>
    public static string? GetXmlSummary(this FieldInfo field) =>
        GetSummaryById($"F:{field.DeclaringType!.FullName}.{field.Name}");

    /// <summary>方法的 <c>&lt;summary&gt;</c>。</summary>
    public static string? GetXmlSummary(this MethodInfo method) =>
        GetSummaryById(BuildMethodDocId(method));

    /// <summary>枚举**值**的 <c>&lt;summary&gt;</c>。</summary>
    public static string? GetXmlSummary(this Enum enumValue)
    {
        var enumType = enumValue.GetType();
        var memberName = Enum.GetName(enumType, enumValue);
        return memberName is null ? null : GetSummaryById($"F:{enumType.FullName}.{memberName}");
    }

    /// <summary>按名字取枚举成员的 <c>&lt;summary&gt;</c>。</summary>
    public static string? GetXmlEnumFieldSummary(this Type enumType, string fieldName) =>
        enumType.IsEnum ? GetSummaryById($"F:{enumType.FullName}.{fieldName}") : null;

    /// <summary>取枚举全部成员的 <c>&lt;summary&gt;</c>，用于一次性构造下拉项的 tooltip。</summary>
    public static IReadOnlyDictionary<string, string?> GetXmlEnumFieldSummaries(this Type enumType)
    {
        if (!enumType.IsEnum) return new Dictionary<string, string?>();

        return Enum.GetNames(enumType)
            .ToDictionary(name => name, name => GetXmlEnumFieldSummary(enumType, name));
    }

    private static string? GetSummaryById(string docId)
    {
        // 未初始化：静默返回 null。见类注释的第一条警告。
        XDocument? doc;
        lock (Gate) doc = _doc;
        if (doc is null) return null;

        var summary = doc.Descendants("member")
            .FirstOrDefault(m => (string?)m.Attribute("name") == docId)?
            .Elements("summary").FirstOrDefault()?
            .Value.Trim();

        return string.IsNullOrWhiteSpace(summary) ? null : NormalizeWhitespace(summary);
    }

    private static string BuildMethodDocId(MethodInfo method)
    {
        var declaring = method.DeclaringType!.FullName;
        var parameters = method.GetParameters();

        if (parameters.Length == 0) return $"M:{declaring}.{method.Name}";

        var paramList = string.Join(",", parameters.Select(p => ToDocTypeName(p.ParameterType)));
        return $"M:{declaring}.{method.Name}({paramList})";
    }

    /// <summary>
    /// 把 CLR 类型名转成 XML 文档 ID 里的写法（泛型用 <c>{}</c> 而非 <c>&lt;&gt;</c>）。
    /// </summary>
    private static string ToDocTypeName(Type t)
    {
        if (t.IsGenericType)
        {
            var baseName = t.GetGenericTypeDefinition().FullName!.Split('`')[0];
            var args = t.GetGenericArguments().Select(ToDocTypeName);
            return $"{baseName}{{{string.Join(",", args)}}}";
        }
        if (t.IsArray) return $"{ToDocTypeName(t.GetElementType()!)}[]";
        if (t.IsByRef) return $"{ToDocTypeName(t.GetElementType()!)}&";
        return t.FullName!;
    }

    /// <summary>把多行注释压成每行单空格分隔，去掉源码缩进带来的空白。</summary>
    private static string? NormalizeWhitespace(string s)
    {
        var lines = s.ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => Whitespace().Replace(line.Trim(), " "))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
