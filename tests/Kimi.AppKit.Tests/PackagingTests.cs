using System.Reflection;
using Xunit;

namespace Kimi.AppKit.Tests;

/// <summary>
/// 打包契约的回归测试。
///
/// 【为什么需要它】发布链路的 gate 必须跑**真实断言**。
/// 参考教训：Kimi.MudBlazorExtentions 的 CI 里 run_test 跑的是一个 10 行的空 [Fact]，
/// 于是历次发布实质上没有任何自动化验证，直到有人手工发现问题为止。
/// </summary>
public class PackagingTests
{
    public static TheoryData<Assembly> PackageAssemblies() =>
    [
        typeof(AppKit.Core.AppKitMarker).Assembly,
        typeof(AppKit.Data.AppKitMarker).Assembly,
        typeof(AppKit.Web.AppKitMarker).Assembly,
    ];

    /// <summary>
    /// MinVer 必须从 git tag 推导出真实版本号。
    /// ⚠️ CI 的 actions/checkout 漏掉 fetch-depth: 0 时，MinVer 看不到任何 tag，
    /// 会静默地把所有包打成 0.0.0-alpha.0 —— 构建全绿、包却是废的。
    /// </summary>
    [Theory]
    [MemberData(nameof(PackageAssemblies))]
    public void 程序集带有非零版本号(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.False(string.IsNullOrWhiteSpace(informational));
        Assert.DoesNotContain("0.0.0", informational);
    }

    /// <summary>
    /// 依赖方向必须单向：Core 零依赖，不得反向引用 Data / Web。
    /// 违反时症状是 WASM 客户端被拖进 EF Core 与 SqlClient——原模板的 Shared 工程
    /// 就因为引用 Kimi.EFExtensions 而把 EFCore.SqlServer 带进了浏览器端。
    /// </summary>
    [Fact]
    public void Core_不依赖_EFCore_与_AspNetCore()
    {
        var referenced = typeof(AppKit.Core.AppKitMarker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToArray();

        Assert.DoesNotContain(referenced, n => n.StartsWith("Microsoft.EntityFrameworkCore"));
        Assert.DoesNotContain(referenced, n => n.StartsWith("Microsoft.AspNetCore"));
        Assert.DoesNotContain(referenced, n => n.StartsWith("Microsoft.Data.SqlClient"));
    }

    /// <summary>
    /// Data 包只能依赖 EF Core 的 Relational 层，**不得依赖任何具体 provider**。
    ///
    /// 这条守的是一个已经发生过的真实事故：前身 <c>Kimi.EFExtensions</c> 直接引用了
    /// <c>Microsoft.EntityFrameworkCore.SqlServer</c>，于是引用它的共享工程把
    /// <c>Microsoft.Data.SqlClient</c> 一路拖进了 **Blazor WebAssembly 浏览器端** ——
    /// 一个永远不可能连数据库的地方，白白背了几 MB 的负载。
    ///
    /// 更根本的问题是：包一旦绑定某个 provider，「支持双 provider」这个承诺就是假的。
    /// </summary>
    [Fact]
    public void Data_不依赖任何具体的数据库_provider()
    {
        var referenced = typeof(AppKit.Data.AppKitMarker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToArray();

        Assert.DoesNotContain(referenced, n => n.StartsWith("Microsoft.EntityFrameworkCore.SqlServer"));
        Assert.DoesNotContain(referenced, n => n.StartsWith("Microsoft.Data.SqlClient"));
        Assert.DoesNotContain(referenced, n => n.StartsWith("Npgsql"));
    }
}
