// 夹具（不参与任何工程编译）：植入一个死选项，供 scripts/dead-options-scan.sh --self-test 校验扫描器会报错。
namespace Fixture;

public sealed class WidgetOptions
{
    public int LiveLimit { get; set; } = 10;

    /// <summary>死选项：全仓除定义处外没有任何引用。</summary>
    public string OrphanedOptionXyz { get; set; } = "";
}
