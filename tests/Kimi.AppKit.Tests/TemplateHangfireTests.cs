using System.Text.RegularExpressions;
using Xunit;

namespace Kimi.AppKit.Tests;

/// <summary>
/// 模板 Program.cs 不得再自己调 <c>AddHangfireServer()</c>：
/// <c>AddAppKitHangfire</c> 内部已经注册了一个服务器，再调一次就是两个 BackgroundJobServer
/// 抢同一个队列——不报错，只是每个作业的并发度悄悄翻倍、心跳记录翻倍。
/// </summary>
public sealed class TemplateHangfireTests
{
    [Fact]
    public void 模板_Program_不重复注册_Hangfire_服务器()
    {
        var program = Path.Combine(RepoRoot.Find(), "samples", "AppTemplate.Next", "KMoldApp", "Program.cs");
        var code = Regex.Replace(File.ReadAllText(program), @"//.*", "");

        Assert.DoesNotContain("AddHangfireServer", code);
        Assert.Contains("AddAppKitHangfire", code);
    }
}
