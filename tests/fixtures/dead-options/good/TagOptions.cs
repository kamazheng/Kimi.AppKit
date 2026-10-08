// 夹具：getter-only 集合属性通过 .Tags.Add 使用，扫描器应通过。
namespace Fixture;

public sealed class TagOptions
{
    public List<string> Tags { get; } = [];
}
