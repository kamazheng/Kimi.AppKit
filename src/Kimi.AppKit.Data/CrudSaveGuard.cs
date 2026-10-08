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

    /// <summary>用标准 DataAnnotations 校验本对象的全部属性；返回错误消息（空表示通过）。</summary>
    public static IReadOnlyList<string> Validate<TEntity>(TEntity item) where TEntity : class
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(item, new ValidationContext(item), results, validateAllProperties: true);

        var isAuditable = item is IAuditableEntity;
        return
        [
            .. results
                .Where(r => !isAuditable || !r.MemberNames.Any() || r.MemberNames.Any(m => !AuditProperties.Contains(m)))
                .Select(r => r.ErrorMessage ?? "校验失败。"),
        ];
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

    /// <summary>给用户看的冲突提示：只含模型属性名，不含表名/约束名。</summary>
    public static string UniqueMessage(DbContext db, Type entityType)
    {
        var fields = db.Model.FindEntityType(entityType)?.GetIndexes()
            .Where(i => i.IsUnique)
            .Select(i => string.Join("+", i.Properties.Select(p => p.Name)))
            .Distinct()
            .ToArray() ?? [];

        return fields.Length == 0
            ? "该记录已存在（唯一约束冲突），请更换后重试。"
            : $"该记录已存在（{string.Join(" / ", fields)} 的值不能重复），请更换后重试。";
    }

    private static int? ReadInt(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) as int?;
}
