namespace Fixture;

public sealed class Unrelated(ModeOptions options)
{
    public int Count => options.Used;

    // 与 ModeOptions 无关的另一个类，恰好也有叫 Mode 的属性
    public string Mode { get; set; } = "Mode";

    public string Describe() => "Mode and DeadList are only words here";
}
