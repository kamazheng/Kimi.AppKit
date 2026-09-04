using Kimi.AppKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace KMoldApp.Data.Entities;

/// <summary>
/// 邮件模板。
/// </summary>
/// <remarks>
/// ⚠️ 表与 schema 的映射走 <c>OnModelCreating</c> 里的 <c>ToSchemaTable</c>，
/// **不用 <c>[Table(..., Schema = "Reference")]</c> 特性**——旧版本用特性把
/// schema 名写成了字面量，与 <c>DbSchema.Reference</c> 常量各写一份，改一处漏一处。
/// </remarks>
public class EmailTemplate : ISoftDeleteEntity
{
    /// <summary>主键。</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>模板名。</summary>
    [Required]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>模板中可用的占位参数，用 <c>{}</c> 包裹、逗号分隔，如 <c>{Name}, {Email}</c>。</summary>
    [StringLength(500)]
    [Comment("Email template parameters quoted with {} separated by comma, e.g. {Name}, {Email}")]
    public string? Parameters { get; set; }

    /// <summary>备注。</summary>
    [StringLength(500)]
    public string? Remark { get; set; }

    /// <summary>邮件主题。</summary>
    [Required]
    [StringLength(500)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>邮件正文（HTML）。</summary>
    public string? Body { get; set; }

    /// <summary>软删除标记。由框架拦截 <c>Remove()</c> 自动维护，**不要手写赋值**。</summary>
    public bool Active { get; set; } = true;
}
