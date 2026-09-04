using System.Net;

namespace KMoldApp.Tests.Endpoints;

/// <summary>
/// 现场登录（密码 / 扫码）两条通道的回归网。
/// </summary>
/// <remarks>
/// 【为什么这几条值得单独测】这两个页面走**静态 SSR**，而静态 SSR 的失败形态
/// 几乎全是静默的：表单字段没有 <c>name</c> 就提交空值、防伪元数据缺失就形同虚设、
/// 回跳地址不收敛就是开放重定向。三者在界面上都看不出任何异常。
/// </remarks>
[Collection(nameof(AppCollection))]
public sealed class LoginPageTests(AppFixture fixture)
{
    /// <remarks>
    /// ⚠️ 这条挡的是静态 SSR 最经典的坑：换用 MudBlazor 的输入控件后，
    /// 它们不继承 <c>InputBase</c>，**不生成 name 属性**，于是提交上来所有字段都是空的，
    /// 而页面渲染完全正常、控制台一条错误也没有。
    /// </remarks>
    [DatabaseFact]
    public async Task 密码登录表单的每个字段都带name属性()
    {
        var client = fixture.CreateClient();
        if (client is null) return;

        var html = await client.GetStringAsync("/password-login");

        Assert.Contains("name=\"username\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"password\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"returnUrl\"", html, StringComparison.Ordinal);

        // 防伪令牌由 <AntiforgeryToken /> 渲染。没有它，UseAntiforgery 无从校验。
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
    }

    /// <remarks>
    /// ⚠️ 端点必须用 <c>[FromForm]</c> **显式绑定**参数。
    /// 图省事去 <c>ReadFormAsync</c> 手取的话，端点拿不到防伪元数据，
    /// <c>UseAntiforgery</c> 中间件会直接放行——防伪形同虚设，
    /// 而页面上那个 <c>&lt;AntiforgeryToken /&gt;</c> 还在，看起来一切正常。
    /// </remarks>
    [DatabaseFact]
    public async Task 没有防伪令牌的登录提交被拒()
    {
        var client = fixture.CreateClient();
        if (client is null) return;

        var response = await client.PostAsync("/authentication/password-login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = "someone",
                ["password"] = "whatever",
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>登录页与二维码打印页都必须匿名可达——要登录才能看登录页是死循环。</summary>
    [DatabaseFact]
    public async Task 登录相关页面匿名可达()
    {
        var client = fixture.CreateClient();
        if (client is null) return;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/password-login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/qr-print")).StatusCode);
    }

    /// <remarks>
    /// ⚠️ 二维码密文被篡改、伪造、过期时必须回**同一个**结果。
    /// 区分开来等于告诉攻击者「这张卡是真的，只是过期了」。
    /// </remarks>
    [DatabaseFact]
    public async Task 伪造的二维码密文不会签发会话()
    {
        var client = fixture.CreateClient();
        if (client is null) return;

        var response = await client.PostAsync("/authentication/qr-login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["payload"] = "这不是一个合法的密文",
            }));

        // 防伪先拦（400）或准入/解密后拒（302 回登录页）都可以，
        // 唯独不能是「签发了会话」。
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            c => c.Contains(".AspNetCore.Cookies", StringComparison.Ordinal));
    }
}
