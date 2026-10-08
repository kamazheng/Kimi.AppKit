using Xunit;

namespace Kimi.AppKit.Tests;

/// <summary>
/// 包内 README 契约：9 个包（8 库 + 模板）的 README 会被 src/Directory.Build.props 以
/// <c>PackagePath="\"</c> 原样打进 nupkg，nuget.org 包页直接展示它。
/// 因此「P0 占位」之类的开发期措辞一旦残留，就会公开发布出去。
/// 这里读的就是被打包的那份源文件（打包规则只是原样拷贝），不必在单测里真跑 dotnet pack。
/// </summary>
public class PackageReadmeTests
{
    private static readonly string[] ForbiddenPhrases = ["P0 占位", "P0 阶段", "占位包", "尚未填充"];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kimi.AppKit.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根（Kimi.AppKit.slnx）");
    }

    public static TheoryData<string> PackageReadmes()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "README.md", SearchOption.AllDirectories)
                     .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
            data.Add(Path.GetRelativePath(RepoRoot(), path));
        return data;
    }

    [Fact]
    public void 九个包各有_README()
    {
        Assert.Equal(9, PackageReadmes().Count);
    }

    [Theory]
    [MemberData(nameof(PackageReadmes))]
    public void README_不含开发期占位措辞(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));
        foreach (var phrase in ForbiddenPhrases)
            Assert.DoesNotContain(phrase, text);
    }
}
