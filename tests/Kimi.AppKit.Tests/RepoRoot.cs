namespace Kimi.AppKit.Tests;

/// <summary>定位仓库根（含 Kimi.AppKit.slnx 的目录），供读源码/打包的测试共用。</summary>
internal static class RepoRoot
{
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kimi.AppKit.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根（Kimi.AppKit.slnx）");
    }
}
