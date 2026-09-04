using System.Net;

namespace KMoldApp.Tests.Endpoints;

/// <summary>
/// 管线上几处「症状离根因极远」的回归网。
/// </summary>
/// <remarks>
/// 【这组测试的来历】每一条都对应一个**实际发生过、且零报错**的缺陷。
/// 它们共同的特征是：应用照常启动、构建与其余测试全绿，
/// 只有真的按那条路径发一次请求才看得见。
/// </remarks>
[Collection(nameof(AppCollection))]
public sealed class PipelineRegressionTests(AppFixture fixture)
{
    /// <remarks>
    /// 【它挡的是什么】不显式调 <c>UseRouting()</c> 时框架把路由中间件插到管线**最前面**，
    /// 于是 <c>UseStatusCodePagesWithReExecute</c> 重新执行请求时不再经过路由匹配，
    /// 端点为 null，落进「默认拒绝」的兜底策略 —— 未登录用户被 302 到 IdP。
    ///
    /// ⚠️ 后果链极长且完全指不到根因：浏览器请求一个已失效的
    /// <c>/_framework/*.pdb</c>（发布后还缓存着旧 index.html 就会发生），
    /// 本该拿到 404 自愈，实际收到跨域重定向 → 控制台只报 CORS 被拦 →
    /// mono 加载失败 → WASM 起不来 → 页面底部弹出 Blazor 的黄色未处理错误条。
    /// </remarks>
    [DatabaseFact]
    public async Task 不存在的框架资源返回404而不是跳登录()
    {
        var client = fixture.CreateClient();
        if (client is null) return;

        var response = await client.GetAsync("/_framework/definitely-not-a-real-asset.pdb");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <remarks>
    /// 【它挡的是什么】未认证的 API 请求被重定向到登录页，调用方拿到的是一整页 HTML，
    /// <c>response.json()</c> 当场抛 SyntaxError，错误信息完全指不到「其实是没登录」。
    /// ⚠️ 修复必须同时挂在 Cookie 与 OIDC 两个 handler 上：配了 OIDC 时挑战根本不走 Cookie。
    /// </remarks>
    [DatabaseFact]
    public async Task 未认证的API请求返回401而不是重定向()
    {
        var client = fixture.CreateClient();
        if (client is null) return;

        var response = await client.GetAsync("/api/crud/setting");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    /// <remarks>
    /// 不存在的 API 地址要如实回 404 + <c>problem+json</c>。
    /// ⚠️ 把「不存在」伪装成「要登录」只会让调用方查错查到别处去，
    /// 而地址存不存在本来就不是秘密。
    /// </remarks>
    [DatabaseFact]
    public async Task 不存在的API地址返回problemjson的404()
    {
        var client = fixture.CreateClient();
        if (client is null) return;

        var response = await client.GetAsync("/api/definitely-not-a-real-endpoint");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>首页必须匿名可达——登录页自己也是这个 WASM 应用的一部分。</summary>
    [DatabaseFact]
    public async Task 首页匿名可达()
    {
        var client = fixture.CreateClient();
        if (client is null) return;

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
