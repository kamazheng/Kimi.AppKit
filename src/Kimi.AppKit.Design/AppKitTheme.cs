using Kimi.AppKit.Design.Tokens;
using MudBlazor;

namespace Kimi.AppKit.Design;

/// <summary>
/// 部署方可覆盖的主色。终端用户仍然拿不到调色板——那条决策没有变
/// （旧代码挂过 <c>MudThemeManager</c>，改完不落库、刷新即丢，还让截图与工单对不上）。
/// 这里开放的是**部署方在初始化时**传入的覆盖，不是运行期界面。
/// </summary>
/// <param name="Primary">主操作色。默认制图蓝。</param>
/// <param name="Accent">强调色。默认黄铜。</param>
/// <param name="Blueprint">深色表面/抽屉底。默认蓝图深蓝。</param>
public sealed record BrandColors(string? Primary = null, string? Accent = null, string? Blueprint = null);

/// <summary>
/// 「蓝图 Cyanotype」设计系统的 <see cref="MudTheme"/> 预设。
/// </summary>
/// <remarks>
/// 【与前身的差异】前身两个服务各有一份逐字节相同（除注释外）的 <c>KMoldTheme.cs</c>，
/// 颜色值独立写死两遍。这里从 <see cref="BlueprintPalette"/> 常量构建，
/// 且颜色值本身不再重复出现在类型定义里——只出现在常量类那一处。
/// </remarks>
public static class AppKitTheme
{
    private static readonly string[] SansStack =
    [
        "IBM Plex Sans", "PingFang SC", "Microsoft YaHei", "Noto Sans SC", "system-ui", "sans-serif",
    ];

    /// <summary>默认主题实例（无品牌覆盖）。多数消费方直接用这个。</summary>
    public static readonly MudTheme Instance = Build();

    /// <summary>
    /// 构建主题，可选按 <paramref name="brand"/> 覆盖主色/强调色/深色表面。
    /// </summary>
    public static MudTheme Build(BrandColors? brand = null)
    {
        var primary = brand?.Primary ?? BlueprintPalette.Draft;
        var accent = brand?.Accent ?? BlueprintPalette.Brass;
        var blueprint = brand?.Blueprint ?? BlueprintPalette.Blueprint;

        return new MudTheme
        {
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = BlueprintPalette.Radius,
                DrawerWidthLeft = "248px",
                AppbarHeight = "56px",
            },

            PaletteLight = new PaletteLight
            {
                Primary = primary,
                Secondary = accent,
                Tertiary = blueprint,
                Success = BlueprintPalette.Ok,
                Warning = BlueprintPalette.Warn,
                Error = BlueprintPalette.Error,
                Info = primary,

                Black = blueprint,
                Background = BlueprintPalette.Mist,
                BackgroundGray = "#E8EDF2",
                Surface = BlueprintPalette.Paper,

                // AppBar 用纸白而非主色：控制台里彩色顶栏会和页面内的主色按钮抢注意力，
                // 品牌存在感交给深色抽屉去承担。
                AppbarBackground = BlueprintPalette.Paper,
                AppbarText = BlueprintPalette.Ink,

                // 抽屉是蓝图深色：登录页与控制台共享同一块深蓝，跨页面维持同一个身份。
                DrawerBackground = blueprint,
                DrawerText = "rgba(255,255,255,.72)",
                DrawerIcon = "rgba(255,255,255,.56)",

                TextPrimary = BlueprintPalette.Ink,
                TextSecondary = BlueprintPalette.Ink2,
                TextDisabled = "rgba(21,28,36,.38)",
                ActionDefault = BlueprintPalette.Ink2,
                Divider = BlueprintPalette.Hair,
                LinesDefault = BlueprintPalette.Hair,
                TableLines = BlueprintPalette.Hair,
            },

            PaletteDark = new PaletteDark
            {
                Primary = BlueprintPalette.Dark.Draft,
                Secondary = BlueprintPalette.Dark.Brass,
                Tertiary = "#8AB4DC",
                Success = "#4CAF7D",
                Warning = BlueprintPalette.Dark.Brass,
                Error = "#E5776F",
                Info = BlueprintPalette.Dark.Draft,

                Black = "#07131C",
                Background = BlueprintPalette.Dark.Mist,
                BackgroundGray = "#0A1219",
                Surface = BlueprintPalette.Dark.Paper,

                AppbarBackground = BlueprintPalette.Dark.Paper,
                AppbarText = BlueprintPalette.Dark.Ink,

                DrawerBackground = "#07203A",
                DrawerText = "rgba(255,255,255,.70)",
                DrawerIcon = "rgba(255,255,255,.52)",

                TextPrimary = BlueprintPalette.Dark.Ink,
                TextSecondary = BlueprintPalette.Dark.Ink2,
                TextDisabled = "rgba(228,234,240,.38)",
                ActionDefault = BlueprintPalette.Dark.Ink2,
                Divider = BlueprintPalette.Dark.Hair,
                LinesDefault = BlueprintPalette.Dark.Hair,
                TableLines = BlueprintPalette.Dark.Hair,
            },

            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = SansStack,
                    FontSize = ".9375rem",
                    FontWeight = "400",
                    LineHeight = "1.6",
                    LetterSpacing = "0",
                },
                H4 = new H4Typography
                {
                    FontFamily = SansStack,
                    FontSize = "1.5rem",
                    FontWeight = "600",
                    LineHeight = "1.3",
                    LetterSpacing = "-.01em",
                },
                H5 = new H5Typography
                {
                    FontFamily = SansStack,
                    FontSize = "1.25rem",
                    FontWeight = "600",
                    LineHeight = "1.35",
                    LetterSpacing = "-.005em",
                },
                H6 = new H6Typography
                {
                    FontFamily = SansStack,
                    FontSize = "1rem",
                    FontWeight = "600",
                    LineHeight = "1.5",
                    LetterSpacing = "0",
                },
                Subtitle1 = new Subtitle1Typography
                {
                    FontFamily = SansStack,
                    FontSize = ".9375rem",
                    FontWeight = "500",
                    LineHeight = "1.5",
                },
                Subtitle2 = new Subtitle2Typography
                {
                    FontFamily = SansStack,
                    FontSize = ".8125rem",
                    FontWeight = "600",
                    LineHeight = "1.5",
                },
                Body1 = new Body1Typography
                {
                    FontFamily = SansStack,
                    FontSize = ".9375rem",
                    FontWeight = "400",
                    LineHeight = "1.65",
                },
                Body2 = new Body2Typography
                {
                    FontFamily = SansStack,
                    FontSize = ".875rem",
                    FontWeight = "400",
                    LineHeight = "1.65",
                },
                Caption = new CaptionTypography
                {
                    FontFamily = SansStack,
                    FontSize = ".75rem",
                    FontWeight = "400",
                    LineHeight = "1.55",
                },

                // ⚠️ 按钮文字不做 uppercase：MudBlazor 默认把按钮文本转大写，
                // 对中文无效、对 client_id 这类混排却会改变字形，同一个词在按钮与正文里长得不一样。
                Button = new ButtonTypography
                {
                    FontFamily = SansStack,
                    FontSize = ".875rem",
                    FontWeight = "600",
                    LineHeight = "1.75",
                    LetterSpacing = ".01em",
                    TextTransform = "none",
                },
            },
        };
    }
}
