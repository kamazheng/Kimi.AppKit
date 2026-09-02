using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Core.Hosting;
using Xunit;

namespace Kimi.AppKit.Tests.Core;

/// <summary>实体基类的相等性语义。</summary>
public class BaseAuditableEntityTests
{
    private sealed class Widget : BaseAuditableEntity;
    private sealed class Gadget : BaseAuditableEntity;

    [Fact]
    public void 同类型同主键视为同一实体()
    {
        Assert.Equal(new Widget { Id = 7 }, new Widget { Id = 7 });
        Assert.Equal(new Widget { Id = 7 }.GetHashCode(), new Widget { Id = 7 }.GetHashCode());
    }

    [Fact]
    public void 不同类型即使主键相同也不相等()
    {
        // 否则 Widget#1 与 Gadget#1 会在混合集合里互相顶掉。
        Assert.NotEqual<object>(new Widget { Id = 1 }, new Gadget { Id = 1 });
    }

    [Fact]
    public void 未落库的实例互不相等()
    {
        // Id 还是 0 的两个新对象若被判为相等，
        // 「新建三行再一起保存」会在集合里被 Distinct 掉两行，且不报错。
        var a = new Widget();
        var b = new Widget();

        Assert.NotEqual(a, b);
        Assert.Equal(a, a);
        Assert.Equal(2, new HashSet<Widget> { a, b }.Count);
    }

    [Fact]
    public void 默认是有效状态()
    {
        // 新建实体默认 Active = true，否则一保存就被全局查询过滤器隐藏，
        // 表现为「保存成功但列表里看不到」。
        Assert.True(new Widget().Active);
    }
}

/// <summary>双协议地址解析。</summary>
public class SchemeUrlResolverTests
{
    private const string Https = "https://files.example.com";
    private const string Http = "http://files.example.com";

    [Fact]
    public void https_页面选_https_地址()
    {
        // https 页面加载 http 资源会被浏览器按 mixed content 拦掉。
        Assert.Equal(Https, SchemeUrlResolver.Resolve("https://app.example.com", Https, Http));
    }

    [Fact]
    public void http_页面选_http_地址()
    {
        // 脱域机器不信任企业根证书，访问 https 下游直接报证书错误。
        Assert.Equal(Http, SchemeUrlResolver.Resolve("http://app.example.com", Https, Http));
    }

    [Theory]
    [InlineData("HTTPS://app.example.com")]
    [InlineData("HttPs://app.example.com")]
    public void 协议判断不区分大小写(string baseAddress) =>
        Assert.Equal(Https, SchemeUrlResolver.Resolve(baseAddress, Https, Http));

    [Fact]
    public void 缺一端时回退另一端()
    {
        Assert.Equal(Https, SchemeUrlResolver.Resolve("http://app.example.com", Https, null));
        Assert.Equal(Http, SchemeUrlResolver.Resolve("https://app.example.com", null, Http));
    }

    [Fact]
    public void 两端皆空返回空串而不是抛异常()
    {
        // 下游地址没配是配置问题，不该让整个页面白屏。
        Assert.Equal(string.Empty, SchemeUrlResolver.Resolve("https://app.example.com", null, null));
        Assert.Equal(string.Empty, SchemeUrlResolver.Resolve(null, null, null));
    }

    [Fact]
    public void 基地址为空时按_http_处理()
    {
        // 保守选择：http 目标在两种环境下都至少可达，https 目标在脱域机器上直接不可用。
        Assert.Equal(Http, SchemeUrlResolver.Resolve(null, Https, Http));
    }
}
