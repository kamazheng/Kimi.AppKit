using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;

namespace Kimi.AppKit.Core.Reflection;

/// <summary>
/// 从反射成员推导人类可读的展示名——反射驱动的表格列头、表单标签、枚举下拉项共用同一套规则，
/// 避免每个组件各自实现一遍"有没有 [Display]，没有就拆词"的逻辑。
/// </summary>
public static class DisplayLabelExtensions
{
    /// <summary>
    /// 展示名。优先取 <see cref="DisplayAttribute.Name"/>，否则把 PascalCase 名字拆成空格分隔的单词。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="MemberInfo"/> 而不是 <see cref="PropertyInfo"/>，是因为枚举成员在反射里是
    /// <see cref="FieldInfo"/>——同一套规则要同时服务"实体属性"（表格列/表单字段）与
    /// "枚举值"（<c>KEnumChip</c> 下拉项），两者的公共基类只有 <see cref="MemberInfo"/>。
    /// </remarks>
    public static string GetDisplayLabel(this MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);

        var display = member.GetCustomAttribute<DisplayAttribute>();
        if (!string.IsNullOrWhiteSpace(display?.Name)) return display.Name;

        return SplitPascalCase(member.Name);
    }

    private static string SplitPascalCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
