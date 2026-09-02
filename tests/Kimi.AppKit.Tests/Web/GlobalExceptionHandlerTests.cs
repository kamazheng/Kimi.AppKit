using Kimi.AppKit.Web.ErrorHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kimi.AppKit.Tests.Web;

/// <summary>
/// 全局异常处理的默认行为——架构评审 R6。
/// </summary>
public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task 默认策略不暴露异常详情()
    {
        // 前身默认值是 true：完整异常链未经认证即可触达任意能触发 500 的匿名接口。
        // FixedErrorDetailPolicy.Disabled 就是新的默认落点。
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, FixedErrorDetailPolicy.Disabled);
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await handler.TryHandleAsync(context, new InvalidOperationException("连接字符串: Password=secret123"), CancellationToken.None);

        var body = await ReadBodyAsync(context);
        Assert.DoesNotContain("secret123", body);
        Assert.DoesNotContain("exception", body);
    }

    [Fact]
    public async Task 显式打开时暴露异常详情()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, new FixedErrorDetailPolicy(true));
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await handler.TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        var body = await ReadBodyAsync(context);
        Assert.Contains("exception", body);
    }

    [Fact]
    public async Task BaseException_的状态码与消息被采用()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, FixedErrorDetailPolicy.Disabled);
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await handler.TryHandleAsync(
            context, new BaseException("找不到该记录", System.Net.HttpStatusCode.NotFound), CancellationToken.None);

        Assert.Equal(404, context.Response.StatusCode);
        Assert.Contains("找不到该记录", await ReadBodyAsync(context));
    }

    [Fact]
    public async Task 非_BaseException_不把原始消息当标题暴露()
    {
        // 底层异常的 Message 可能含连接串、文件路径等信息，不能直接回显给客户端。
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, FixedErrorDetailPolicy.Disabled);
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await handler.TryHandleAsync(
            context, new Exception("Host=prod-db;Password=hunter2"), CancellationToken.None);

        Assert.DoesNotContain("hunter2", await ReadBodyAsync(context));
    }

    private static async Task<string> ReadBodyAsync(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }
}
