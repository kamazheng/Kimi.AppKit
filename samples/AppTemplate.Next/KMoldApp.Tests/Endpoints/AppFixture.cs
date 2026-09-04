using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace KMoldApp.Tests.Endpoints;

/// <summary>整个测试类共用一个进程内应用实例。</summary>
/// <remarks>
/// 【⚠️ 为什么必须共用，不能每条测试各起一个】应用在一个进程里**只能启动一次**：
/// Hangfire 的部分配置写在全局静态上，第二次启动会抛
/// <c>Console is already initialized</c> 之类的异常。每条测试各起一个工厂时，
/// 第一条过、后面全挂，而失败信息里完全看不出「问题在于应用被启动了两次」。
///
/// 这也是仓库铁律「禁止新增进程内共享可变状态」在第三方库上的体现：
/// 改不了 Hangfire 的 API，只能让测试适应它。
///
/// ⚠️ **代价：一个进程只能验证一套配置。** 因此像「网络准入被拒时的登录页」
/// 这类需要另一套配置的场景，这里测不了，只能手工验证。
/// 取舍是：把这唯一的名额给「准入允许」那条路径——因为静默失效的坑
/// （表单字段没有 name、提交上来全是空值）全在那条路径上。
/// </remarks>
public sealed class AppFixture : IDisposable
{
    internal const string NpgsqlEnv = "TEST_NPGSQL_CONNECTION";

    private readonly WebApplicationFactory<Program>? _factory;

    public AppFixture()
    {
        var connectionString = Environment.GetEnvironmentVariable(NpgsqlEnv);
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["Database:Provider"] = "Npgsql",

                    // ⚠️ 刻意不配 OIDC。配了的话未认证请求会被重定向到一个不存在的 IdP，
                    //    断言就变成在测「重定向去了哪」而不是「是否被拦」。
                    //    应用在缺 OIDC 配置时会跳过注册并照常启动（见 Program.cs 的启动提示）。

                    // ⚠️ 必须放行回环，否则现场登录页因网络准入被拒而**不渲染表单**，
                    //    「表单字段带 name」那条断言会失败在一个与它无关的原因上。
                    //    ⚠️ 空白名单 = 拒绝（方向是刻意的：忘了配等于更严），
                    //    所以只设 AllowLoopback 不够，还得给一个非空网段。
                    ["Auth:NetworkGate:AllowedSubnets:0"] = "10.0.0.0/8",
                    ["Auth:NetworkGate:AllowLoopback"] = "true",
                }));

            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter, LoopbackRemoteIpFilter>());
        });
    }

    /// <summary>没有配数据库连接串时为 null，相关测试整体跳过。</summary>
    public HttpClient? CreateClient() => _factory?.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public void Dispose() => _factory?.Dispose();

    /// <summary>把来源 IP 填成回环，好让网络准入能判。</summary>
    /// <remarks>
    /// ⚠️ <c>TestServer</c> 不走真实套接字，<c>Connection.RemoteIpAddress</c> 是 **null**，
    /// 于是网络准入判成 <c>DeniedUnknownAddress</c>——连 <c>AllowLoopback</c> 都够不着，
    /// 因为压根不是回环地址。不补这一手，现场登录页在测试里永远渲染成「被拒」，
    /// 而「表单字段带 name」那条断言会失败在一个与它毫无关系的原因上。
    /// </remarks>
    private sealed class LoopbackRemoteIpFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, following) =>
                {
                    context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
                    await following();
                });

                next(app);
            };
    }
}

[CollectionDefinition(nameof(AppCollection))]
public sealed class AppCollection : ICollectionFixture<AppFixture>;
