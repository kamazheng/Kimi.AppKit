using System.Net;
using System.Text;
using Kimi.AppKit.Http;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using Xunit;

namespace Kimi.AppKit.Tests.Http;

/// <summary>
/// <see cref="RefitServiceCollectionExtensions.AddAppKitRefitClient{T}"/> 的行为契约。
/// </summary>
public class RefitServiceCollectionExtensionsTests
{
    [Fact]
    public async Task 成功响应按_Web_命名约定反序列化()
    {
        var api = BuildApi(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":1,"name":"widget"}""", Encoding.UTF8, "application/json"),
        });

        var thing = await api.GetThingAsync(1);

        Assert.Equal(1, thing.Id);
        Assert.Equal("widget", thing.Name);
    }

    /// <summary>
    /// 失败响应必须经 <c>KHttpResponseExtensions.BuildException</c> 提取 <c>detail</c>，
    /// 不能让调用方看到 Refit 默认 <c>ApiException</c> 那句「状态码不代表成功」。
    /// </summary>
    [Fact]
    public async Task 失败响应经ExceptionFactory转成带detail的异常()
    {
        var api = BuildApi(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"detail":"块 X 可预留量只有 0"}""", Encoding.UTF8, "application/problem+json"),
        });

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => api.GetThingAsync(1));
        Assert.Equal("块 X 可预留量只有 0", ex.Message);
    }

    /// <summary>
    /// 数组查询参数必须按重复 key 展开，匹配 ASP.NET Core <c>[FromQuery] string[]</c> 的绑定方式。
    /// 用默认格式化会拼成逗号分隔的单个值，服务端认不出来，筛选条件被静默忽略。
    /// </summary>
    [Fact]
    public async Task 数组查询参数按重复key展开()
    {
        string? capturedQuery = null;
        var api = BuildApi(request =>
        {
            capturedQuery = request.RequestUri!.Query;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        });

        await api.SearchAsync(["a", "b"]);

        Assert.Equal("?tags=a&tags=b", capturedQuery);
    }

    private static ITestApi BuildApi(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var services = new ServiceCollection();
        services
            .AddAppKitRefitClient<ITestApi>("http://localhost/")
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(respond));

        return services.BuildServiceProvider().GetRequiredService<ITestApi>();
    }
}

/// <summary>
/// ⚠️ 必须是顶层接口，不能嵌套在测试类里——Refit 的源生成器按顶层类型定位接口。
/// </summary>
public interface ITestApi
{
    [Get("/things/{id}")]
    Task<TestThing> GetThingAsync(int id);

    [Get("/search")]
    Task<string[]> SearchAsync([Query] string[] tags);
}

public sealed record TestThing(int Id, string Name);

file sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
