namespace Kimi.AppKit.Core.Entities;

/// <summary>
/// 可审计 + 可软删除实体的基类。业务实体一般从这里派生。
/// </summary>
/// <remarks>
/// 【相等性按主键】两个实例只要 <see cref="Id"/> 相同就视为同一实体，
/// 这样 <c>Contains</c>/<c>Distinct</c>/集合比较才有正确的领域语义。
/// ⚠️ <see cref="Id"/> 为 0（尚未落库）的实例一律视为**互不相等**——
/// 否则两个新建但还没保存的对象会被集合当成同一个而丢掉一个。
/// </remarks>
public abstract class BaseAuditableEntity : IAuditableEntity, ISoftDeleteEntity
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>是否有效。false = 已软删除。对用户无意义，不出现在自动生成的表格里。</summary>
    [HideFromTable]
    public bool Active { get; set; } = true;

    /// <inheritdoc />
    [HideFromTable]
    public DateTimeOffset Updated { get; set; }

    /// <inheritdoc />
    [HideFromTable]
    public string? UpdatedBy { get; set; }

    /// <inheritdoc />
    [HideFromTable]
    public DateTimeOffset CreatedOn { get; set; }

    /// <inheritdoc />
    [HideFromTable]
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is BaseAuditableEntity other
        && GetType() == other.GetType()
        && Id != 0
        && Id == other.Id;

    /// <inheritdoc />
    public override int GetHashCode() => Id == 0 ? base.GetHashCode() : HashCode.Combine(GetType(), Id);
}
