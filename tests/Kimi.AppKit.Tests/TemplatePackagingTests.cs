using System.Diagnostics;
using System.IO.Compression;
using Xunit;

namespace Kimi.AppKit.Tests;

/// <summary>
/// 模板包（Kimi.AppKit.Templates）内容的回归测试：真打一次包，再读 nupkg 清单。
///
/// 【为什么要真打包】模板内容是另一棵源码树，不参与编译，且打包「成功」不代表内容对——
/// 已删除的文件、不该进包的本地产物、漏掉的点开头文件（.dockerignore）都不会有任何告警，
/// 只有解开 nupkg 才看得见。
/// </summary>
public sealed class TemplatePackagingTests : IClassFixture<TemplatePackagingTests.TemplatePackage>
{
    private readonly TemplatePackage _pkg;

    public TemplatePackagingTests(TemplatePackage pkg) => _pkg = pkg;

    [Fact]
    public void 模板包不含_dev_sln()
        => Assert.DoesNotContain(_pkg.Entries, e => e.EndsWith(".dev.sln", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void 模板包不含已删除的_generate_dockerfile_脚本()
        => Assert.DoesNotContain(_pkg.Entries, e => e.Contains("generate-dockerfile", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void 模板包不含_migrate_prod_脚本()
        => Assert.DoesNotContain(_pkg.Entries, e => e.Contains("migrate-prod", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void 模板包不含只能在_AppKit_仓内运行的_selftest_脚本()
        => Assert.DoesNotContain(_pkg.Entries, e => e.Contains("template-selftest", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void 模板包含_Dockerfile_与_dockerignore()
    {
        Assert.Contains(_pkg.Entries, e => e.EndsWith("content/kimiapp/Dockerfile", StringComparison.Ordinal));
        Assert.Contains(_pkg.Entries, e => e.EndsWith("content/kimiapp/.dockerignore", StringComparison.Ordinal));
    }

    /// <summary>真打一次模板包并缓存条目清单（整个类只打一次）。</summary>
    public sealed class TemplatePackage : IDisposable
    {
        private readonly string _outDir = Path.Combine(Path.GetTempPath(), "kimi-appkit-tpl-" + Guid.NewGuid().ToString("N"));

        public IReadOnlyList<string> Entries { get; }

        public TemplatePackage()
        {
            var csproj = Path.Combine(FindRepoRoot(), "src", "Kimi.AppKit.Templates", "Kimi.AppKit.Templates.csproj");
            Directory.CreateDirectory(_outDir);

            using var p = Process.Start(new ProcessStartInfo("dotnet", $"pack \"{csproj}\" -c Release -o \"{_outDir}\" --nologo -v q")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, "dotnet pack 模板包失败：\n" + output);

            var nupkg = Directory.GetFiles(_outDir, "Kimi.AppKit.Templates.*.nupkg").Single();
            using var zip = ZipFile.OpenRead(nupkg);
            Entries = zip.Entries.Select(e => e.FullName).ToList();
        }

        public void Dispose()
        {
            try { Directory.Delete(_outDir, recursive: true); } catch (IOException) { /* 临时目录，清不掉不影响结论 */ }
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kimi.AppKit.slnx"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根（Kimi.AppKit.slnx）");
        }
    }
}
