using System.Reflection;
using KMoldApp.Data;
using Kimi.AppKit.Data.Providers;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace KMoldApp.Tests.Database;

/// <summary>
/// 双 provider 迁移程序集的结构约束。
/// </summary>
/// <remarks>
/// 【为什么要有】迁移文件里的列类型是**生成那一刻**按当时 provider 烤进去的字符串。
/// 曾经两个 provider 共用一套迁移，于是 <c>dotnet ef migrations script</c> 换个 provider
/// 只换掉标识符引号（<c>"X"</c> ↔ <c>[X]</c>），列类型仍是另一个库的写法——
/// SQL Server 的脚本里出现 <c>character varying</c> / <c>boolean</c> / <c>xid</c>。
/// 脚本照常生成、能通过 code review，**拿到目标库上执行才炸**。
///
/// 这组测试钉死「两套迁移各自存在且互不为空」。它挡不住「内容写错」——
/// 那要靠 <see cref="DatabaseProviderIntegrationTests"/> 真连库跑。
/// 但它能挡住「有人把其中一套删了/合并了」这种结构性回退。
/// </remarks>
public class MigrationAssemblyTests
{
    [Theory]
    [InlineData(ProviderSetup.NpgsqlMigrationsAssembly)]
    [InlineData(ProviderSetup.SqlServerMigrationsAssembly)]
    public void 每个provider都有自己的迁移程序集且含至少一个迁移(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var migrations = assembly.GetTypes()
            .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        Assert.NotEmpty(migrations);
    }

    [Fact]
    public void 两个迁移程序集是不同的程序集()
    {
        // 一旦有人把 MigrationsAssembly 常量改成同一个值，双 provider 的隔离就没了，
        // 而且不会有任何编译错误——那正是这条断言要挡住的回退。
        Assert.NotEqual(
            ProviderSetup.NpgsqlMigrationsAssembly,
            ProviderSetup.SqlServerMigrationsAssembly);
    }
}
