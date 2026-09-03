using Microsoft.AspNetCore.Components;
using Bunit;
using Bunit.TestDoubles;
using Kimi.AppKit.Components.Components;
using Xunit;

namespace Kimi.AppKit.Components.Tests;

/// <summary>
/// 角色门禁。⚠️ 无权限时禁用并提示缺哪个角色，不要隐藏——这条 UX 原则本身就是本组件
/// 存在的理由，测试要覆盖到"提示文案里包含缺失角色名"这一点，不能只测有没有渲染。
/// </summary>
public class KRoleGateTests : BunitContext
{
    public KRoleGateTests() => this.AddAuthorization();

    [Fact]
    public void 持有所需角色时渲染_Authorized_插槽()
    {
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("alice");
        authContext.SetRoles("Admin");

        var cut = Render<KRoleGate>(p => p
            .Add(x => x.Roles, "Admin")
            .Add(x => x.Authorized, "<span class=\"ok\">已授权内容</span>")
            .Add(x => x.NotAuthorized, _ => (RenderFragment)(b => b.AddMarkupContent(0, "不该出现"))));

        Assert.Contains("已授权内容", cut.Markup);
        Assert.DoesNotContain("不该出现", cut.Markup);
    }

    [Fact]
    public void 不持有所需角色时禁用并提示缺哪个角色_不隐藏()
    {
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("bob");
        authContext.SetRoles("User"); // 持有 User，但门禁要求 Admin

        var cut = Render<KRoleGate>(p => p
            .Add(x => x.Roles, "Admin")
            .Add(x => x.Authorized, "不该出现")
            .Add(x => x.NotAuthorized, ctx => (RenderFragment)(b =>
                b.AddContent(0, $"缺少角色：{ctx.MissingRolesHint}"))));

        Assert.DoesNotContain("不该出现", cut.Markup);
        Assert.Contains("需要 Admin 角色", cut.Markup);
    }

    [Fact]
    public void 持有其中任意一个角色即可_OR语义()
    {
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("carol");
        authContext.SetRoles("Root"); // Roles 参数是 "Admin,Root"，持有其一即可

        var cut = Render<KRoleGate>(p => p
            .Add(x => x.Roles, "Admin,Root")
            .Add(x => x.Authorized, "已授权")
            .Add(x => x.NotAuthorized, _ => (RenderFragment)(b => b.AddContent(0, "不该出现"))));

        Assert.Contains("已授权", cut.Markup);
    }

    [Fact]
    public void 缺多个角色时提示文案用_或_而不是顿号()
    {
        // 顿号枚举会被误读成"需要同时持有两者"——Roles 是 OR 语义，必须用"或"。
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("dave");
        authContext.SetRoles("Guest");

        var cut = Render<KRoleGate>(p => p
            .Add(x => x.Roles, "Admin,Root")
            .Add(x => x.Authorized, "不该出现")
            .Add(x => x.NotAuthorized, ctx => (RenderFragment)(b => b.AddContent(0, ctx.MissingRolesHint))));

        Assert.Contains("需要以下任一角色：Admin 或 Root", cut.Markup);
    }

    [Fact]
    public void 角色名可以自定义格式化去掉前缀()
    {
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("erin");
        authContext.SetRoles("User");

        var cut = Render<KRoleGate>(p => p
            .Add(x => x.Roles, "MES_Admin")
            .Add(x => x.FormatRoleName, (Func<string, string>)(r => r.Replace("MES_", "")))
            .Add(x => x.Authorized, "不该出现")
            .Add(x => x.NotAuthorized, ctx => (RenderFragment)(b => b.AddContent(0, ctx.MissingRolesHint))));

        Assert.Contains("需要 Admin 角色", cut.Markup);
        Assert.DoesNotContain("MES_", cut.Markup);
    }
}
