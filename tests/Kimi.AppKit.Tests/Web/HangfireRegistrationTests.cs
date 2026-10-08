using Kimi.AppKit.Data.Providers;
using Kimi.AppKit.Web.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Kimi.AppKit.Tests.Web;

public sealed class HangfireRegistrationTests
{
    /// <summary>
    /// <c>AddAppKitHangfire</c> 自己已含服务器；调用方若再 <c>AddHangfireServer()</c> 就会有两个。
    /// 这条断言钉住「包内只注册一个」，配合模板守护测试保证整条链路上只有一个。
    /// </summary>
    [Fact]
    public void AddAppKitHangfire_只注册一个后台服务器()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppKitHangfire(DatabaseProvider.SqlServer, "Server=.;Database=x;", _ => { });

        // AddHangfireServer 以 IHostedService 形态登记；本测试容器里没有其他托管服务，计数即服务器数。
        var servers = services.Count(d => d.ServiceType == typeof(IHostedService));

        Assert.Equal(1, servers);
    }
}
