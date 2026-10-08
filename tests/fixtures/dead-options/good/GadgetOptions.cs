// 夹具：所有可写选项都有引用，扫描器应通过（退出码 0）。
namespace Fixture;

public sealed class GadgetOptions
{
    public int Capacity { get; set; } = 3;
    public string Label { get; init; } = "";
    public int ReadOnlyComputed => Capacity * 2;
}
