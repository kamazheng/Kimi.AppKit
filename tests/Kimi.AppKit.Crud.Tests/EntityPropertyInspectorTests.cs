using Kimi.AppKit.Core.Entities;
using Kimi.AppKit.Core.Reflection;
using Xunit;

namespace Kimi.AppKit.Crud.Tests;

/// <summary>
/// 反射驱动的表格列/表单字段与通用搜索共用同一份"哪些属性算数据库字段"的判断。
/// 排序（Id → Name → Description → 其余）与过滤规则一旦出错，KEntityTable/KEntityForm 的
/// 呈现和 ReflectiveCrudDataSource 的搜索范围会同时跑偏。
/// </summary>
public class EntityPropertyInspectorTests
{
    private interface ISoftDelete
    {
        bool Active { get; set; }
    }

    private sealed class Widget : ISoftDelete
    {
        public int Id { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public bool Active { get; set; }

        [HideFromTable]
        public string InternalCode { get; set; } = string.Empty;

        public string ReadOnlyComputed => "x";

        public List<string> Tags { get; set; } = [];
    }

    [Fact]
    public void Id_Name_Description优先排在最前面()
    {
        var properties = EntityPropertyInspector.GetEditableProperties(typeof(Widget));

        Assert.Equal(["Id", "Name", "Description", "Price", "Active"], properties.Select(p => p.Name));
    }

    [Fact]
    public void 贴了HideFromTable的属性被排除()
    {
        var properties = EntityPropertyInspector.GetEditableProperties(typeof(Widget));

        Assert.DoesNotContain(properties, p => p.Name == "InternalCode");
    }

    [Fact]
    public void 只读属性被排除因为没有setter()
    {
        var properties = EntityPropertyInspector.GetEditableProperties(typeof(Widget));

        Assert.DoesNotContain(properties, p => p.Name == "ReadOnlyComputed");
    }

    [Fact]
    public void 集合属性被排除()
    {
        var properties = EntityPropertyInspector.GetEditableProperties(typeof(Widget));

        Assert.DoesNotContain(properties, p => p.Name == "Tags");
    }

    [Fact]
    public void 指定排除接口后接口自带的属性不出现()
    {
        var properties = EntityPropertyInspector.GetEditableProperties(typeof(Widget), [typeof(ISoftDelete)]);

        Assert.DoesNotContain(properties, p => p.Name == "Active");
    }

    [Fact]
    public void 不指定排除接口时接口属性照常出现()
    {
        var properties = EntityPropertyInspector.GetEditableProperties(typeof(Widget));

        Assert.Contains(properties, p => p.Name == "Active");
    }
}
