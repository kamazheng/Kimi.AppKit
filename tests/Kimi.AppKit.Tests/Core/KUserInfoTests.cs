using System.Security.Claims;
using Kimi.AppKit.Core.Contracts;
using Xunit;

namespace Kimi.AppKit.Tests.Core;

/// <summary>
/// KUserInfo 读取显示名 claim 的契约。Auth 的 ClaimsDestinationPolicy 发的是 <c>display_name</c>，
/// 读错名字不会报错，只会让右上角显示名静默回退成登录名。
/// </summary>
public class KUserInfoTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test", KUserInfo.NameClaimType, KUserInfo.RoleClaimType));

    [Fact]
    public void 带_display_name_claim_时_DisplayName_取该值()
    {
        var info = KUserInfo.FromClaimsPrincipal(
            Principal(new Claim("name", "zhangsan"), new Claim("display_name", "张三")), "tok");

        Assert.Equal("张三", info.DisplayName);
        Assert.Equal("zhangsan", info.Name);
    }

    [Fact]
    public void 缺_display_name_claim_时回退到登录名()
    {
        var info = KUserInfo.FromClaimsPrincipal(Principal(new Claim("name", "zhangsan")), "tok");

        Assert.Equal("zhangsan", info.DisplayName);
    }
}
