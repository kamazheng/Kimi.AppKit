namespace Kimi.AppKit.Design.Tokens;

/// <summary>
/// 「蓝图 Cyanotype」设计系统的配色令牌——**唯一真相源**。
/// </summary>
/// <remarks>
/// 【它解决的问题】前身把同一套颜色手写在四个地方：两个服务各一份
/// <c>KMoldTheme.cs</c>（C# <c>MudTheme</c>）与各一份 <c>kmold-tokens.css</c>（CSS 变量）。
/// 改一处颜色，另外三处不会自动跟着变——只能靠人记得去改，改漏的后果是控制台与登录页、
/// 或者两个服务之间，慢慢变成两套配色，谁也说不清哪个是对的。
///
/// 【为什么不做成真正的代码生成】评估过用构建期任务从这些常量吐出 CSS 文件，
/// 但那会给一个纯前端资产引入代码生成步骤，让消费方的构建变慢变复杂——
/// 收益（省下手写 CSS 的几行）配不上代价。现在的做法是 CSS 文件仍然手写，
/// 但 <c>BlueprintPaletteConsistencyTests</c> 会解析 CSS 里的 <c>:root</c> 声明，
/// 逐一比对这里的常量值，**改一处忘了改另一处，CI 就红**——效果等价，成本低得多。
///
/// 【为什么是这套颜色】取自模具设计图的氰版蓝（blueprint）而非通用 SaaS 蓝：
/// 面向的用户在车间与工厂机房，界面要像量具与图纸的语言，不是营销落地页的语言。
/// </remarks>
public static class BlueprintPalette
{
    /// <summary>蓝图深蓝——登录页底、抽屉底、深色表面。</summary>
    public const string Blueprint = "#0B2B4A";

    /// <summary>蓝图深蓝，更深一档。</summary>
    public const string BlueprintDeep = "#07203A";

    /// <summary>蓝图深蓝，提亮一档。</summary>
    public const string BlueprintLift = "#123B63";

    /// <summary>制图蓝——一切可点击的主操作。</summary>
    public const string Draft = "#1E5F9E";

    /// <summary>制图蓝，悬停态。</summary>
    public const string DraftHover = "#17527F";

    /// <summary>制图蓝，浅底。</summary>
    public const string DraftSoft = "#E8F0F8";

    /// <summary>黄铜——强调、刻度、环境警示。电极黄铜是模具车间的常见材料。</summary>
    public const string Brass = "#C4841D";

    /// <summary>黄铜，浅底。</summary>
    public const string BrassSoft = "#FBF2E0";

    /// <summary>正文墨色（冷调，避免暖白带来的"文档感"）。</summary>
    public const string Ink = "#151C24";

    /// <summary>次要文字。</summary>
    public const string Ink2 = "#4A5765";

    /// <summary>三级文字/禁用态。</summary>
    public const string Ink3 = "#74818F";

    /// <summary>纸白。</summary>
    public const string Paper = "#FFFFFF";

    /// <summary>雾灰底色。</summary>
    public const string Mist = "#F2F5F8";

    /// <summary>发丝分隔线。</summary>
    public const string Hair = "#DCE3EA";

    /// <summary>成功语义色。</summary>
    public const string Ok = "#1F7A4D";

    /// <summary>警告语义色。</summary>
    public const string Warn = "#B26A00";

    /// <summary>错误语义色。</summary>
    public const string Error = "#B3261E";

    /// <summary>错误语义色，浅底。</summary>
    public const string ErrorSoft = "#FCEDEC";

    /// <summary>圆角。量具与夹具没有圆润的角，但全直角在屏幕上过于生硬。</summary>
    public const string Radius = "3px";

    /// <summary>大圆角（卡片一类更大的容器）。</summary>
    public const string RadiusLarge = "6px";

    /// <summary>正文字体栈。Latin 字体必须排在中文字体前面，否则数字与 client_id 也会落到中文字体上。</summary>
    public const string FontStack =
        "'IBM Plex Sans', 'PingFang SC', 'Microsoft YaHei', 'Noto Sans SC', system-ui, sans-serif";

    /// <summary>等宽字体栈，用于 ID / 代码 / 数值对齐场景。</summary>
    public const string MonoStack = "'IBM Plex Mono', 'SFMono-Regular', 'Cascadia Mono', Consolas, monospace";

    /// <summary>
    /// 暗色模式下的对应色板。
    /// </summary>
    /// <remarks>
    /// ⚠️ 这套颜色**不会自动生效**——挂载它的作用域由消费方决定（通常是登录页外壳的某个类名），
    /// 不要挂到 <c>:root</c>：控制台由 MudBlazor 的 palette 上色，若 <c>MudThemeProvider</c>
    /// 没开 <c>IsDarkMode</c>，Mud 的 palette 恒为亮色，把 <c>:root</c> 的墨色改暗会让
    /// 一切靠继承取色的 MudBlazor 元素（未选中的 tab、断线重连遮罩……）变成浅底浅字，
    /// 实测对比度低到 1.1:1，且只在用户系统是暗色时复现。
    /// </remarks>
    public static class Dark
    {
        public const string Paper = "#16202B";
        public const string Mist = "#0E1720";
        public const string Ink = "#E4EAF0";
        public const string Ink2 = "#A9B6C4";
        public const string Ink3 = "#8492A1";
        public const string Hair = "#2A3846";
        public const string Draft = "#5FA0DC";
        public const string DraftHover = "#7BB4E8";
        public const string DraftSoft = "#1B2E42";
        public const string Brass = "#E0A33D";
        public const string BrassSoft = "#2A2214";
        public const string ErrorSoft = "#33191A";
    }
}
