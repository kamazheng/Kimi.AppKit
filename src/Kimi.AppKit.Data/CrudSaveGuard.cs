using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Reflection;
using Kimi.AppKit.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kimi.AppKit.Data;

/// <summary>
/// <see cref="EfCrudDataSource{TContext,TEntity}.UpsertAsync"/> 的保存前校验与唯一约束冲突翻译。
/// </summary>
/// <remarks>
/// 【为什么在数据源而不在端点】端点手工读 body，minimal API 的自动校验不跑；
/// Excel 导入与 Blazor 直连也走同一个 UpsertAsync。放在这里一处覆盖全部入口。
///
/// 【审计字段不参与校验】<see cref="IAuditableEntity"/> 的四个属性由审计上下文在保存时填，
/// 校验时必为缺省值；若有人给它们标了 [Required]，不能算成客户端的错。
/// </remarks>
internal static class CrudSaveGuard
{
    private static readonly HashSet<string> AuditProperties =
    [
        nameof(IAuditableEntity.Updated), nameof(IAuditableEntity.UpdatedBy),
        nameof(IAuditableEntity.CreatedOn), nameof(IAuditableEntity.CreatedBy),
    ];

    /// <summary>
    /// 先排除审计属性、再校验：逐属性跑 ValidationAttribute（Required 失败则该属性不再跑其余特性，与
    /// <see cref="Validator"/> 一致），再跑类级特性，最后跑 <see cref="IValidatableObject"/>。
    /// 返回错误消息（空表示通过）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 不能「先 TryValidateObject 再丢审计错误」：它在任一属性失败时会提前停止，类级特性与
    /// IValidatableObject 不会运行，丢掉审计错误后保存会静默通过。
    /// ⚠️ ValidationContext 没有 ServiceProvider：自定义校验器里 <c>GetService</c> 返回 null。
    /// 没有显式 ErrorMessage 的内置特性用中文模板，字段名取 [Display(Name)]，没有则取属性名。
    /// </remarks>
    public static IReadOnlyList<string> Validate<TEntity>(TEntity item) where TEntity : class
    {
        var errors = new List<string>();
        var skip = item is IAuditableEntity ? AuditProperties : new HashSet<string>();
        var type = item.GetType();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0 || !prop.CanRead || skip.Contains(prop.Name)) continue;
            var attrs = prop.GetCustomAttributes<ValidationAttribute>(true).ToList();
            if (attrs.Count == 0) continue;

            var name = DisplayName(prop);
            var ctx = new ValidationContext(item) { MemberName = prop.Name, DisplayName = name };
            var value = prop.GetValue(item);

            var required = attrs.OfType<RequiredAttribute>().ToList();
            foreach (var attr in required.Concat(attrs.Except(required)))
            {
                var r = attr.GetValidationResult(value, ctx);
                if (r == ValidationResult.Success) continue;
                errors.Add(Message(attr, r!, name));
                if (attr is RequiredAttribute) break;
            }
        }

        var classCtx = new ValidationContext(item);
        foreach (var attr in type.GetCustomAttributes<ValidationAttribute>(true))
        {
            var r = attr.GetValidationResult(item, classCtx);
            if (r != ValidationResult.Success) errors.Add(r!.ErrorMessage ?? "校验失败。");
        }

        if (item is IValidatableObject v)
        {
            foreach (var r in v.Validate(classCtx))
            {
                if (r == ValidationResult.Success) continue;
                var members = r.MemberNames.ToList();
                if (members.Count > 0 && members.All(skip.Contains)) continue;
                errors.Add(r.ErrorMessage ?? "校验失败。");
            }
        }

        return errors;
    }

    private static string DisplayName(PropertyInfo prop) =>
        prop.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? prop.Name;

    private static string Message(ValidationAttribute attr, ValidationResult result, string name)
    {
        if (attr.ErrorMessage is not null || attr.ErrorMessageResourceType is not null)
            return result.ErrorMessage ?? "校验失败。";   // 显式消息优先

        return attr switch
        {
            RequiredAttribute => $"{name}不能为空。",
            MaxLengthAttribute m => $"{name}长度不能超过 {m.Length}。",
            MinLengthAttribute m => $"{name}长度不能少于 {m.Length}。",
            StringLengthAttribute { MinimumLength: > 0 } sl =>
                $"{name}长度必须在 {sl.MinimumLength} 到 {sl.MaximumLength} 之间。",
            StringLengthAttribute sl => $"{name}长度不能超过 {sl.MaximumLength}。",
            RangeAttribute rg => $"{name}必须在 {rg.Minimum} 到 {rg.Maximum} 之间。",
            _ => result.ErrorMessage ?? "校验失败。",
        };
    }

    /// <summary>是否唯一约束 / 唯一索引冲突（PG 23505、SQL Server 2601/2627、SQLite 2067/1555）。</summary>
    public static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e is not DbException db) continue;
            // DbException.SqlState：Npgsql 返回 SQLSTATE。
            if (db.SqlState == "23505") return true;
            // 不能引用 provider 包（Data 层不依赖任何 provider），按名字读属性。
            if (ReadInt(db, "Number") is 2601 or 2627) return true;
            if (ReadInt(db, "SqliteExtendedErrorCode") is 2067 or 1555) return true;
        }
        return false;
    }

    /// <summary>
    /// 给用户看的冲突提示：只含字段显示名，不含表名/约束名。能从异常里认出违例的索引名
    /// （PG ConstraintName / SQL Server 消息）就只列它的字段，认不出（如 SQLite）则列全部唯一索引。
    /// </summary>
    public static string UniqueMessage(DbContext db, Type entityType, DbUpdateException ex)
    {
        var unique = db.Model.FindEntityType(entityType)?.GetIndexes().Where(i => i.IsUnique).ToList() ?? [];

        var text = new List<string>();
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            text.Add(e.Message);
            if (e.GetType().GetProperty("ConstraintName")?.GetValue(e) is string c) text.Add(c);
        }
        var joined = string.Join("\n", text);
        var hit = unique.Where(i => i.GetDatabaseName() is { Length: > 0 } n && joined.Contains(n)).ToList();

        var fields = (hit.Count > 0 ? hit : unique)
            .Select(i => string.Join("+", i.Properties.Select(p => FieldName(entityType, p.Name))))
            .Distinct().ToArray();

        return fields.Length == 0
            ? "该记录已存在（唯一约束冲突），请更换后重试。"
            : $"该记录已存在（{string.Join(" / ", fields)} 的值不能重复），请更换后重试。";
    }

    private static string FieldName(Type type, string property) =>
        type.GetProperty(property) is { } p ? DisplayName(p) : property;

    private static int? ReadInt(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) as int?;
}
