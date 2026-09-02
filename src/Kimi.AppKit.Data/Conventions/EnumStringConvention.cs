using Kimi.AppKit.Data.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Kimi.AppKit.Data.Conventions;

/// <summary>
/// 把模型里全部枚举属性存成字符串，并加 CHECK 约束限定取值范围。
/// </summary>
/// <remarks>
/// 【为什么存字符串不存 int】整型枚举在数据库里就是一个数字，
/// 脱离代码去看一行数据完全不知道 <c>Status = 2</c> 是什么意思，
/// 排障、写报表、跑数据修复脚本时全靠人肉对照代码里的枚举定义。
///
/// 【⚠️ 改枚举成员必须生成迁移】
/// CHECK 约束在此约定生效的那一刻就把「取值范围」焊进了数据库层。
/// 新增枚举成员后忘记生成迁移，数据库里的约束还是旧的那几个值，
/// 写入新成员会撞约束错误——这个错误发生在写入那一刻，看错误信息完全想不到是这里。
///
/// 【⚠️ 列名必须按 provider 加引号】
/// PostgreSQL 会把不带引号的标识符折叠成小写，PascalCase 列名（<c>Status</c>）
/// 在约束表达式里写成裸 <c>Status</c> 会被解析成 <c>status</c>——建表当场失败。
/// SQL Server 不折叠，所以这个坑只在切到 PostgreSQL 时才暴露。
/// </remarks>
public static class EnumStringConvention
{
    /// <summary>为模型中所有枚举属性应用字符串转换与 CHECK 约束。</summary>
    public static ModelBuilder ApplyEnumStringConstraints(this ModelBuilder modelBuilder, IDbProviderDialect dialect)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is null) continue;   // 视图/无表映射的实体跳过

            var entity = modelBuilder.Entity(entityType.ClrType);

            foreach (var property in entityType.GetProperties())
            {
                var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!enumType.IsEnum) continue;

                var columnName = property.GetColumnName(
                    Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(tableName, entityType.GetSchema()));
                if (string.IsNullOrWhiteSpace(columnName)) continue;

                var converter = (ValueConverter)Activator.CreateInstance(
                    typeof(EnumToStringConverter<>).MakeGenericType(enumType))!;
                entity.Property(property.Name).HasConversion(converter).HasMaxLength(50);

                var quotedColumn = dialect.Quote(columnName);
                var allowedValues = string.Join(",", Enum.GetNames(enumType).Select(n => $"'{n}'"));
                var checkName = $"CK_{tableName}_{columnName}_Name";

                entity.ToTable(t => t.HasCheckConstraint(checkName, $"{quotedColumn} IN ({allowedValues})"));
            }
        }

        return modelBuilder;
    }
}
