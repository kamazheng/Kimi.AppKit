using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Kimi.AppKit.Data.Modeling;

/// <summary>把属性以 JSON 文本存进单列。</summary>
public static class KJsonConversionExtensions
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 为属性配置 JSON 转换（序列化成字符串列）。
    /// </summary>
    /// <typeparam name="TProperty">属性类型。</typeparam>
    /// <param name="propertyBuilder">属性 builder。</param>
    /// <param name="options">序列化选项。默认大小写不敏感。</param>
    /// <remarks>
    /// 【为什么必须一并设 <see cref="ValueComparer"/>】EF Core 的变更检测对引用类型
    /// 默认只比引用。存 JSON 的属性通常是集合或复杂对象，**原地修改它不会被检测到**，
    /// 保存时静默丢失改动。
    ///
    /// ⚠️ **比较不可只比长度**：等长内容替换（例如把一个文件 id 换成同位数的另一个）
    /// 会被判为「未变更」，改动静默丢失。这里按**序列化后的内容**比较。
    ///
    /// ⚠️ **快照不可返回同一引用**：原值快照必须是独立副本，否则调用方原地 mutate 之后
    /// 「旧值」和「新值」指向同一个对象，怎么比都相等。这里走一次序列化往返做深拷贝。
    ///
    /// ⚠️ 深拷贝与内容比较都要序列化，属性很大时有成本。存大对象请评估是否改用
    /// EF Core 原生的 JSON 列映射（<c>OwnsOne(...).ToJson()</c>）。
    /// </remarks>
    public static PropertyBuilder<TProperty> HasJsonConversion<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        var jsonOptions = options ?? DefaultOptions;

        propertyBuilder
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<TProperty>(v, jsonOptions)!)
            .HasMaxLength(-1)
            .IsUnicode(true);

        propertyBuilder.Metadata.SetValueComparer(new ValueComparer<TProperty>(
            (c1, c2) => JsonSerializer.Serialize(c1, jsonOptions)
                        == JsonSerializer.Serialize(c2, jsonOptions),
            c => c == null ? 0 : JsonSerializer.Serialize(c, jsonOptions).GetHashCode(StringComparison.Ordinal),
            c => c == null
                ? c
                : JsonSerializer.Deserialize<TProperty>(
                    JsonSerializer.Serialize(c, jsonOptions), jsonOptions)!));

        return propertyBuilder;
    }
}
