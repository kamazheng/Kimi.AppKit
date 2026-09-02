using System.Text.RegularExpressions;
using Kimi.AppKit.Design.Tokens;
using Xunit;

namespace Kimi.AppKit.Tests.Design;

/// <summary>
/// 守住 <see cref="BlueprintPalette"/>（C# 常量）与 <c>kmold-tokens.css</c>（CSS 变量）的一致性。
/// </summary>
/// <remarks>
/// 【它防的问题】前身把同一套颜色手写在四个地方（两个服务各一份 C# 主题 + 一份 CSS），
/// 改一处忘了改另一处，控制台与登录页会慢慢变成两套配色。
///
/// 现在颜色值理论上只在 <see cref="BlueprintPalette"/> 里定义一次，但 CSS 文件仍然是
/// **手写**的（评估过用构建期代码生成，但给一个纯前端资产引入生成步骤的代价配不上收益）。
/// 这组测试是那份"理论上"的唯一保障：直接解析 CSS 里的 <c>:root</c> 声明，
/// 逐一比对 C# 常量，任何一处不一致就让 CI 红——不需要指望人记得同步。
/// </remarks>
public class BlueprintPaletteConsistencyTests
{
    private static readonly string CssPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Kimi.AppKit.Design", "wwwroot", "css", "kmold-tokens.css");

    private static readonly Lazy<IReadOnlyDictionary<string, string>> CssVariables = new(ParseRootVariables);

    [Theory]
    [InlineData("--k-blueprint", nameof(BlueprintPalette.Blueprint))]
    [InlineData("--k-blueprint-deep", nameof(BlueprintPalette.BlueprintDeep))]
    [InlineData("--k-blueprint-lift", nameof(BlueprintPalette.BlueprintLift))]
    [InlineData("--k-draft", nameof(BlueprintPalette.Draft))]
    [InlineData("--k-draft-hover", nameof(BlueprintPalette.DraftHover))]
    [InlineData("--k-draft-soft", nameof(BlueprintPalette.DraftSoft))]
    [InlineData("--k-brass", nameof(BlueprintPalette.Brass))]
    [InlineData("--k-brass-soft", nameof(BlueprintPalette.BrassSoft))]
    [InlineData("--k-ink", nameof(BlueprintPalette.Ink))]
    [InlineData("--k-ink-2", nameof(BlueprintPalette.Ink2))]
    [InlineData("--k-ink-3", nameof(BlueprintPalette.Ink3))]
    [InlineData("--k-paper", nameof(BlueprintPalette.Paper))]
    [InlineData("--k-mist", nameof(BlueprintPalette.Mist))]
    [InlineData("--k-hair", nameof(BlueprintPalette.Hair))]
    [InlineData("--k-ok", nameof(BlueprintPalette.Ok))]
    [InlineData("--k-warn", nameof(BlueprintPalette.Warn))]
    [InlineData("--k-error", nameof(BlueprintPalette.Error))]
    [InlineData("--k-error-soft", nameof(BlueprintPalette.ErrorSoft))]
    [InlineData("--k-radius", nameof(BlueprintPalette.Radius))]
    [InlineData("--k-radius-lg", nameof(BlueprintPalette.RadiusLarge))]
    public void CSS变量与C井常量一致(string cssVarName, string constantName)
    {
        var expected = typeof(BlueprintPalette)
            .GetField(constantName)!.GetValue(null)!.ToString()!;

        Assert.True(CssVariables.Value.TryGetValue(cssVarName, out var actual),
            $"CSS 里找不到 {cssVarName}，检查 kmold-tokens.css 的 :root 声明。");

        Assert.Equal(expected, actual, ignoreCase: true);
    }

    [Fact]
    public void 暗色令牌同样一致()
    {
        var darkVars = ParseMediaDarkVariables();

        Assert.Equal(BlueprintPalette.Dark.Paper, darkVars["--k-paper"], ignoreCase: true);
        Assert.Equal(BlueprintPalette.Dark.Draft, darkVars["--k-draft"], ignoreCase: true);
        Assert.Equal(BlueprintPalette.Dark.Brass, darkVars["--k-brass"], ignoreCase: true);
    }

    [Fact]
    public void CSS文件里的字体栈与常量一致()
    {
        Assert.Contains(BlueprintPalette.FontStack, ReadCss());
        Assert.Contains(BlueprintPalette.MonoStack, ReadCss());
    }

    private static string ReadCss()
    {
        Assert.True(File.Exists(CssPath), $"找不到 CSS 文件：{CssPath}");
        return File.ReadAllText(CssPath);
    }

    private static IReadOnlyDictionary<string, string> ParseRootVariables()
    {
        var css = ReadCss();
        var rootBlock = Regex.Match(css, @":root\s*\{([^}]*)\}", RegexOptions.Singleline).Groups[1].Value;
        return ExtractVariables(rootBlock);
    }

    private static IReadOnlyDictionary<string, string> ParseMediaDarkVariables()
    {
        var css = ReadCss();
        var darkBlock = Regex.Match(
            css, @"prefers-color-scheme:\s*dark\s*\)\s*\{\s*\.k-darkmode-scope\s*\{([^}]*)\}",
            RegexOptions.Singleline).Groups[1].Value;
        return ExtractVariables(darkBlock);
    }

    private static IReadOnlyDictionary<string, string> ExtractVariables(string block) =>
        Regex.Matches(block, @"(--k-[\w-]+)\s*:\s*([^;]+);")
            .ToDictionary(m => m.Groups[1].Value.Trim(), m => m.Groups[2].Value.Trim());
}
