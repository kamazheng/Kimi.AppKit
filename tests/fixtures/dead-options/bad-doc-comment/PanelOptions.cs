// 夹具：死选项只在「定义文件内的文档注释」里被提到，没有任何代码引用。
namespace Fixture;

public sealed class PanelOptions
{
    public int Height { get; set; } = 10;

    /// <summary>死选项：配合 <see cref="DocOnlyOption"/> 使用——但它本身没有任何代码引用。</summary>
    public int Width { get; set; } = 5;

    /// <summary>仅被上面那条注释提到，代码里没人读。</summary>
    public string DocOnlyOption { get; set; } = "";
}
