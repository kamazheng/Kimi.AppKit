namespace Fixture;

public sealed class PanelService(PanelOptions options)
{
    // 只读 Height；注释里提到 Width 与 DocOnlyOption 也不算引用。
    public int Area => options.Height * 2;
}
