using Kimi.AppKit.Crud.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace Kimi.AppKit.Crud.Tests;

/// <summary>
/// HTTP 失败响应 → 用户看得懂的一句话。
/// </summary>
/// <remarks>
/// 【这一组在压什么】服务端的错误信息本来质量很高
/// （「块 X 可预留量只有 0（在库 1 − 已预留 1），要求 1」），
/// 早先的实现把**整个响应正文**当作失败原因，于是用户在提示条里看到的是一整段 JSON：
/// RFC 链接、状态码、traceId 全在里面，唯一有用的那句埋在中间。
///
/// ⚠️ 判据不是「能取到消息」，而是**取到的正是那一句、且不带任何开发者视角的噪音**。
/// </remarks>
public class KHttpResultExtensionsTests
{
    private const string ReserveDetail =
        "块 P20-BLK-101-A 可预留量只有 0.0000（在库 1.0000 − 已预留 1.0000），要求 1";

    [Fact]
    public async Task 取的是detail不是整段JSON()
    {
        var response = Problem(HttpStatusCode.BadRequest, $$"""
            {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request",
             "status":400,"detail":"{{ReserveDetail}}","traceId":"00-1afd62dc"}
            """);

        var problem = await response.ToKProblemAsync();

        Assert.Equal(ReserveDetail, problem.Message);
        Assert.DoesNotContain("traceId", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("rfc9110", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// .NET 给 <c>ArgumentException</c> 追加的参数名尾巴要去掉。
    /// </summary>
    /// <remarks>
    /// ⚠️ 服务层用 <c>ArgumentException</c> 表达业务拒绝是刻意的选择，
    /// 所以**每一条**业务失败消息都带这个尾巴——不是个别情况。
    /// </remarks>
    [Fact]
    public async Task 去掉参数名尾巴()
    {
        var response = Problem(HttpStatusCode.BadRequest,
            $$"""{"detail":"{{ReserveDetail}} (Parameter 'quantity')"}""");

        var problem = await response.ToKProblemAsync();

        Assert.Equal(ReserveDetail, problem.Message);
    }

    [Fact]
    public async Task 没有detail时回落到title()
    {
        var response = Problem(HttpStatusCode.BadRequest,
            """{"title":"请求的库位不存在","status":400}""");

        Assert.Equal("请求的库位不存在", (await response.ToKProblemAsync()).Message);
    }

    /// <summary>校验错误按字段归位，而不是糊成一条通知。</summary>
    /// <remarks>
    /// ⚠️ 丢进一条通知里的话，用户还得自己在表单上找是哪一项出错——
    /// 字段一多就等于没提示。
    /// </remarks>
    [Fact]
    public async Task 校验错误保留字段归属()
    {
        var response = Problem(HttpStatusCode.BadRequest, """
            {"title":"One or more validation errors occurred.","status":400,
             "errors":{"Quantity":["必须大于 0"],"LotNumber":["必填","长度不能超过 50"]}}
            """);

        var problem = await response.ToKProblemAsync();

        Assert.Equal(2, problem.FieldErrors.Count);
        Assert.Equal(["必须大于 0"], problem.FieldErrors["Quantity"]);
        Assert.Equal(["必填", "长度不能超过 50"], problem.FieldErrors["LotNumber"]);
    }

    /// <summary>自定义中间件常把 errors 的值写成单个字符串而非数组。</summary>
    /// <remarks>
    /// ⚠️ 只认数组的话，那类响应会**静默**退化成「没有字段级错误」——
    /// 页面上什么都不显示，而响应里明明写着原因。
    /// </remarks>
    [Fact]
    public async Task 单字符串形态的字段错误也认()
    {
        var response = Problem(HttpStatusCode.BadRequest, """{"errors":{"Name":"必填"}}""");

        Assert.Equal(["必填"], (await response.ToKProblemAsync()).FieldErrors["Name"]);
    }

    /// <summary>正文不是 JSON 时不能把它当消息。</summary>
    /// <remarks>
    /// ⚠️ API 落进 SPA 状态码页会被重写成一整页 HTML。原样当作失败原因的话，
    /// 用户会收到一屏 HTML 源码。
    /// </remarks>
    [Fact]
    public async Task 正文是HTML时不倒给用户()
    {
        var response = Problem(HttpStatusCode.InternalServerError,
            "<!DOCTYPE html><html><body>Server Error</body></html>");

        var problem = await response.ToKProblemAsync();

        Assert.DoesNotContain("<", problem.Message, StringComparison.Ordinal);
        Assert.Contains("500", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>401/403/409 的下一步各不相同，正文为空时靠状态码兜底。</summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "登录")]
    [InlineData(HttpStatusCode.Forbidden, "权限")]
    [InlineData(HttpStatusCode.Conflict, "他人修改")]
    public async Task 空正文时按状态码给出下一步(HttpStatusCode status, string expected)
    {
        var problem = await Problem(status, string.Empty).ToKProblemAsync();

        Assert.Contains(expected, problem.Message, StringComparison.Ordinal);
    }

    /// <summary>服务端说得比兜底文案更具体时，听服务端的。</summary>
    [Fact]
    public async Task 有detail时不用兜底文案()
    {
        var response = Problem(HttpStatusCode.Conflict,
            """{"detail":"这张领料单已经被王工执行过了"}""");

        Assert.Equal("这张领料单已经被王工执行过了", (await response.ToKProblemAsync()).Message);
    }

    /// <summary>任何非 2xx 都必须给出一句非空的原因——不能有静默失败。</summary>
    /// <remarks>
    /// ⚠️ 这条压的是「点了按钮什么都没发生」那类缺陷的源头：
    /// 只要消息可能为空，调用方就会写出「有消息才提示」的判断。
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task 任何失败都有非空原因(HttpStatusCode status)
    {
        foreach (var body in new[] { string.Empty, "{}", "not json", """{"detail":""}""" })
        {
            var result = await Problem(status, body).ToKResultAsync();

            Assert.False(result.Succeeded);
            Assert.All(result.Errors, e => Assert.False(string.IsNullOrWhiteSpace(e)));
            Assert.NotEmpty(result.Errors);
        }
    }

    [Fact]
    public async Task 成功响应没有失败详情可解析()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        Assert.True((await response.ToKResultAsync()).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => response.ToKProblemAsync());
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/problem+json") },
            },
        };
}
