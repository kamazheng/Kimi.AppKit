using System.Reflection;

namespace KMoldApp.Shared.Constants;

/// <summary>
/// 应用角色常量——OpenID 服务器上注册的角色全名（前缀 + 角色名）。
/// </summary>
/// <remarks>
/// ⚠️ **生成项目后必须改 <see cref="Prefix"/>**，出厂值只是占位。
/// 角色全名须与 IdP 上注册的完全一致，否则 role claim 匹配不上、页面一律 403，
/// 且失败是静默的——令牌里明明有角色，接口却一路拒绝。
///
/// 【为什么是 <c>const</c> 而不是配置】<c>[Authorize(Roles = ...)]</c> 的特性参数
/// 必须是编译期常量。角色名因此只能在编译期定下来，这是 .NET 的约束不是设计选择。
/// 需要运行期可变的授权，用策略（见 <see cref="AppPolicies"/>）而不是角色名。
/// </remarks>
public static class AppRoles
{
    /// <summary>
    /// 角色名前缀。⚠️ 生成项目后改成本应用在 IdP 上的注册名。
    /// </summary>
    /// <remarks>
    /// 旧模板这里取的是 <c>AppConstant.AppShortName</c>，而那个常量把项目名写死在
    /// 一个跟角色毫无关系的「应用常量」类里，改名时极易漏改。前缀是**角色的一部分**，
    /// 就放在角色定义旁边。
    /// </remarks>
    private const string Prefix = "KMoldApp_";

    /// <summary>系统级角色前缀，区分于项目相关角色。</summary>
    private const string SystemPrefix = Prefix + "System.";

    /// <summary>超级管理员。</summary>
    public const string Root = Prefix + nameof(Root);

    /// <summary>管理员。⚠️ 模板内置，勿删——管理菜单与数据维护页都依赖它。</summary>
    public const string Admin = Prefix + nameof(Admin);

    /// <summary>普通用户。</summary>
    public const string User = SystemPrefix + nameof(User);

    /// <summary>
    /// 反射生成的全部角色全名。单一真相源——新增角色只需加一个常量，
    /// 不必再同步维护第二份列表。
    /// </summary>
    public static readonly string[] All = typeof(AppRoles)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();
}
