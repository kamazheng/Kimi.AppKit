using Kimi.AppKit.Web.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Xunit;

namespace Kimi.AppKit.Tests.Web;

/// <summary>
/// JWT Bearer 装配的默认值与可覆盖性。
///
/// 【为什么这组测试值得写】首版实现把 RequireHttpsMetadata / RoleClaimType / ClockSkew
/// 写死在方法体里——用户指出这是把"无条件关证书校验"换成了"无条件用这三个值"，
/// 同一类问题换了个位置。这里验证默认值安全、且三项都能被显式覆盖。
/// </summary>
public class JwtBearerSetupTests
{
    [Fact]
    public void 默认值安全且合理()
    {
        var options = new JwtBearerOptions();
        options.ConfigureAppKitJwtBearer("https://auth.example.com", "my-api");

        Assert.True(options.RequireHttpsMetadata);           // 生产安全默认
        Assert.Equal("role", options.TokenValidationParameters.RoleClaimType);
        Assert.Equal(TimeSpan.Zero, options.TokenValidationParameters.ClockSkew);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.Equal("my-api", options.TokenValidationParameters.ValidAudience);
    }

    [Fact]
    public void 三个曾经写死的值都可以被覆盖()
    {
        var options = new JwtBearerOptions();
        options.ConfigureAppKitJwtBearer(
            "https://auth.example.com", "my-api",
            requireHttpsMetadata: false,
            roleClaimType: "roles",
            clockSkew: TimeSpan.FromSeconds(30));

        Assert.False(options.RequireHttpsMetadata);
        Assert.Equal("roles", options.TokenValidationParameters.RoleClaimType);
        Assert.Equal(TimeSpan.FromSeconds(30), options.TokenValidationParameters.ClockSkew);
    }

    [Fact]
    public void 逃生舱可以在默认值之上继续调整()
    {
        var options = new JwtBearerOptions();
        var invoked = false;

        options.ConfigureAppKitJwtBearer(
            "https://auth.example.com", "my-api",
            configureValidation: p =>
            {
                invoked = true;
                p.ValidIssuers = ["https://auth.example.com", "https://legacy-auth.example.com"];
            });

        Assert.True(invoked);
        Assert.Contains("https://legacy-auth.example.com", options.TokenValidationParameters.ValidIssuers!);
    }

    [Fact]
    public void MapInboundClaims_恒为_false_不开放为参数()
    {
        // 这一条不是"不同部署需要不同值"的配置项，是让 RoleClaimType 参数本身生效的前提——
        // 保持 true 会让 role 被 JwtBearer 改写成 WS-* 长 URI，RoleClaimType 配什么都没用。
        var options = new JwtBearerOptions();
        options.ConfigureAppKitJwtBearer("https://auth.example.com", "my-api");

        Assert.False(options.MapInboundClaims);
    }
}
