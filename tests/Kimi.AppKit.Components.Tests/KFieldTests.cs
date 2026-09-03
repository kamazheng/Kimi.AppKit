using System.Linq.Expressions;
using Bunit;
using Kimi.AppKit.Components.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Xunit;

namespace Kimi.AppKit.Components.Tests;

/// <summary>
/// 原生表单字段：给静态 SSR 用（登录/找回密码页）。⚠️ 当前只覆盖字符串字段，
/// 覆盖 Auth 现有全部 23 处 InputText 用法。测试要覆盖到"生成了 name 属性"这一点——
/// MudBlazor 的表单控件在静态 SSR 下不生成 name，这正是 KField（原生）存在的理由。
/// </summary>
public class KFieldTests : BunitContext
{
    private sealed class Model
    {
        public string? Email { get; set; }
    }

    [Fact]
    public void 渲染出带name属性的原生输入框()
    {
        var model = new Model();

        var cut = Render<EditForm>(p => p
            .Add(f => f.Model, model)
            .Add(f => f.ChildContent, (RenderFragment<EditContext>)(_ => builder =>
            {
                builder.OpenComponent<KField>(0);
                builder.AddComponentParameter(1, nameof(KField.Label), "邮箱");
                builder.AddComponentParameter(2, nameof(KField.Id), "Input.Email");
                builder.AddComponentParameter(3, nameof(KField.Value), model.Email);
                builder.AddComponentParameter(4, nameof(KField.For), (Expression<Func<string?>>)(() => model.Email));
                builder.CloseComponent();
            })));

        var input = cut.Find("input");
        Assert.Equal("Input.Email", input.GetAttribute("name"));
        Assert.Equal("邮箱", cut.Find("label").TextContent);
    }
}
