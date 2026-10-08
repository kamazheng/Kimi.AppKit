using Kimi.AppKit.Web.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace Kimi.AppKit.Tests.Web;

/// <summary>
/// RFC 8707 <c>resource</c> 参数：单值写进授权请求，多值启动即拒绝（OpenIdConnectMessage 传不了重复参数）。
/// </summary>
public class OidcResourceIndicatorTests
{
    private static OpenIdConnectOptions BuildOidc(params string[] resources)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppKitAuthentication(new ConfigurationBuilder().Build(), o =>
        {
            o.Issuer = "https://auth.example.com";
            o.ClientId = "client";
            foreach (var r in resources) o.Resources.Add(r);
        });
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(KAuthenticationSchemes.Oidc);
    }

    private static async Task<OpenIdConnectMessage> Redirect(OpenIdConnectOptions oidc)
    {
        var http = new DefaultHttpContext();
        var context = new RedirectContext(
            http,
            new AuthenticationScheme(KAuthenticationSchemes.Oidc, null, typeof(OpenIdConnectHandler)),
            oidc,
            new AuthenticationProperties())
        {
            ProtocolMessage = new OpenIdConnectMessage(),
        };
        await oidc.Events.RedirectToIdentityProvider(context);
        return context.ProtocolMessage;
    }

    [Fact]
    public async Task 配置单个resource时写入授权请求()
    {
        var message = await Redirect(BuildOidc("https://api.example/"));

        Assert.Equal("https://api.example/", message.GetParameter("resource"));
    }

    [Fact]
    public async Task 未配置resource时不带该参数()
    {
        var message = await Redirect(BuildOidc());

        Assert.Null(message.GetParameter("resource"));
    }

    [Fact]
    public void 配置多个resource时启动即拒绝()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildOidc("https://a.example/", "https://b.example/"));

        Assert.Contains("Resources", ex.Message);
    }
}
