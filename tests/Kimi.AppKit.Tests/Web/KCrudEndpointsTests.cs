using System.Net;
using System.Net.Http.Json;
using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Web.Authorization;
using Kimi.AppKit.Web.Crud;
using Kimi.AppKit.Web.Excel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Kimi.AppKit.Tests.Web;

/// <summary>
/// <c>MapCrudEndpoints&lt;T&gt;()</c> 的端到端行为。
/// </summary>
/// <remarks>
/// 【为什么必须起真服务器】授权、路由优先级、模型绑定这三样都只有真发一次 HTTP 请求才验得到。
/// 直接 new 出 handler 调用一条都测不出来——而这三样恰恰是本次替换通用端点要解决的问题。
///
/// 【本组最重要的一条】<c>匿名访问返回401</c>。前身把「读写任意表」压缩成一个端点组，
/// 授权只能整组一刀切，实际结果是写端点标了 <c>[Authorize]</c>、读端点忘了标，
/// **任何人可匿名读全库**。这条测试就是钉死「出厂即要求登录」。
/// </remarks>
public sealed class KCrudEndpointsTests
{
    private sealed record Widget
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>内存数据源。本组测的是端点管线，不是数据访问。</summary>
    private sealed class FakeSource : ICrudDataSource<Widget>
    {
        public List<Widget> Items { get; } = [];

        public Task<KPage<Widget>> LoadAsync(KQuery query, CancellationToken cancellationToken = default)
        {
            var q = Items.AsEnumerable();
            if (!string.IsNullOrEmpty(query.Search))
                q = q.Where(w => w.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
            foreach (var (key, value) in query.Filters)
                if (key.Equals(nameof(Widget.Name), StringComparison.OrdinalIgnoreCase))
                    q = q.Where(w => w.Name == value);

            var all = q.ToList();
            var page = all.Skip(query.Skip).Take(query.PageSize).ToList();
            return Task.FromResult(new KPage<Widget>(page, all.Count, query.Page, query.PageSize));
        }

        public Task<Widget?> GetAsync(object id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(w => w.Id.ToString() == id.ToString()));

        public Task<KResult> UpsertAsync(Widget item, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(item.Name)) return Task.FromResult(KResult.Fail("名称不能为空。"));
            Items.RemoveAll(w => w.Id == item.Id);
            Items.Add(item);
            return Task.FromResult(KResult.Ok());
        }

        public Task<KResult> DeleteAsync(object id, CancellationToken cancellationToken = default)
        {
            var removed = Items.RemoveAll(w => w.Id.ToString() == id.ToString());
            return Task.FromResult(removed > 0 ? KResult.Ok() : KResult.Fail("记录不存在。"));
        }
    }

    private static async Task<(IHost Host, HttpClient Client, FakeSource Source)> StartAsync(
        Action<KCrudEndpoints>? configure = null)
    {
        var source = new FakeSource();

        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddAuthentication("Test").AddScheme<TestSchemeOptions, TestHandler>("Test", _ => { });
                    services.AddDefaultDenyAuthorization();
                    services.AddSingleton<ICrudDataSource<Widget>>(source);
                    services.AddSingleton<IExcelService, NpoiExcelService>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e =>
                    {
                        var endpoints = e.MapCrudEndpoints<Widget>();
                        configure?.Invoke(endpoints);
                    });
                }))
            .StartAsync();

        return (host, host.GetTestClient(), source);
    }

    [Fact]
    public async Task 匿名访问读端点返回401()
    {
        var (host, client, _) = await StartAsync();
        using var _h = host;

        var response = await client.GetAsync("/api/crud/widget");

        // ⚠️ 这条是替换通用端点的核心回归。松了就等于回到"任何人可匿名读全库"。
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 匿名访问写端点也返回401()
    {
        var (host, client, _) = await StartAsync();
        using var _h = host;

        var response = await client.DeleteAsync("/api/crud/widget/1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 显式放行后读端点可匿名而写端点仍然401()
    {
        var (host, client, source) = await StartAsync(e => e.AllowAnonymousRead());
        using var _h = host;
        source.Items.Add(new Widget { Id = 1, Name = "螺栓" });

        var read = await client.GetAsync("/api/crud/widget");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        // 读写分成两个 group 的意义就在这里：放开读不会连带放开写。
        var write = await client.DeleteAsync("/api/crud/widget/1");
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    [Fact]
    public async Task 列表返回分页结果且总数是过滤后的总数()
    {
        var (host, client, source) = await StartAsync(e => e.AllowAnonymousRead());
        using var _h = host;
        for (var i = 1; i <= 7; i++) source.Items.Add(new Widget { Id = i, Name = $"W{i:D2}" });

        var page = await client.GetFromJsonAsync<KPage<Widget>>("/api/crud/widget?page=1&pageSize=3");

        Assert.NotNull(page);
        Assert.Equal(7, page!.TotalCount);   // 过滤后总数，不是当前页条数
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public async Task 未知查询参数进筛选而保留字不进()
    {
        var (host, client, source) = await StartAsync(e => e.AllowAnonymousRead());
        using var _h = host;
        source.Items.Add(new Widget { Id = 1, Name = "螺栓" });
        source.Items.Add(new Widget { Id = 2, Name = "垫片" });

        var page = await client.GetFromJsonAsync<KPage<Widget>>("/api/crud/widget?pageSize=50&Name=螺栓");

        Assert.NotNull(page);
        Assert.Equal(1, page!.TotalCount);
    }

    [Fact]
    public async Task 查询串里的特殊字符能正确取值()
    {
        var (host, client, source) = await StartAsync(e => e.AllowAnonymousRead());
        using var _h = host;
        source.Items.Add(new Widget { Id = 1, Name = "A&B#1" });
        source.Items.Add(new Widget { Id = 2, Name = "A" });

        // ⚠️ 前身把参数直接插值进 URL 且全仓 Uri.EscapeDataString 出现 0 次，
        //    含 & / # 的值会被截断 —— 静默取错值，不报错。
        var encoded = Uri.EscapeDataString("A&B#1");
        var page = await client.GetFromJsonAsync<KPage<Widget>>($"/api/crud/widget?pageSize=50&Name={encoded}");

        Assert.NotNull(page);
        Assert.Equal(1, page!.TotalCount);
        Assert.Equal("A&B#1", page.Items[0].Name);
    }

    [Fact]
    public async Task 字面量路由段优先于参数段()
    {
        var (host, client, source) = await StartAsync(e => e.AllowAnonymousRead());
        using var _h = host;
        source.Items.Add(new Widget { Id = 1, Name = "螺栓" });

        // /export 不能被 /{id} 抢走——否则导出会变成"查一条 id 为 export 的记录"。
        var response = await client.GetAsync("/api/crud/widget/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task 取不到的记录返回404而不是200带空值()
    {
        var (host, client, _) = await StartAsync(e => e.AllowAnonymousRead());
        using var _h = host;

        var response = await client.GetAsync("/api/crud/widget/999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 业务校验失败返回400而不是500()
    {
        var (host, client, _) = await StartAsync(e => { e.AllowAnonymousRead(); e.Write.AllowAnonymous(); });
        using var _h = host;

        // KResult 承载的是预期内的业务失败，不是意外。返回 500 会让全局异常处理器把它当事故上报，
        // 而用户其实只需要改一下输入。
        var response = await client.PostAsJsonAsync("/api/crud/widget", new Widget { Id = 1, Name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 请求体不是合法JSON返回400而不是500()
    {
        var (host, client, _) = await StartAsync(e => e.Write.AllowAnonymous());
        using var _h = host;

        var content = new StringContent("{ 这不是 json", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/crud/widget", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>永远视为已登录的认证方案，用来把「授权」与「认证」分开验。</summary>
    private sealed class TestSchemeOptions : Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions;

    private sealed class TestHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<TestSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : Microsoft.AspNetCore.Authentication.AuthenticationHandler<TestSchemeOptions>(options, logger, encoder)
    {
        protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync() =>
            // 不带身份：本组测的是「没登录会不会被拦下」，登录后的路径由显式放行的用例覆盖。
            Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
    }
}
