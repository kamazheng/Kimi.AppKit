namespace Kimi.AppKit.Core.Entities;

/// <summary>
/// 标记某个属性不出现在自动生成的表格里（仍然参与持久化与表单）。
/// 用于 <c>Active</c> 这类对用户无意义的框架字段。
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HideFromTableAttribute : Attribute;
