// 夹具：死选项取了个通用名（Mode），别处恰好有同名词（无关类的同名属性、字符串字面量），
// 整词匹配会误以为「有引用」。另含一个无人使用的只读集合属性（getter-only + 初始化器）。
namespace Fixture;

public sealed class ModeOptions
{
    public int Used { get; set; }

    public string Mode { get; set; } = "";

    public List<string> DeadList { get; } = [];
}
