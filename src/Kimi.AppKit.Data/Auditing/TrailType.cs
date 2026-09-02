namespace Kimi.AppKit.Data.Auditing;

/// <summary>审计记录的变更类型。</summary>
/// <remarks>
/// ⚠️ 以**字符串**存库（列上有 CHECK 约束）。新增成员必须生成迁移，
/// 否则库里仍是旧约束，写入新值会撞约束错误。
/// 重命名成员还要额外写一条 UPDATE 迁移存量行。
/// </remarks>
public enum TrailType
{
    /// <summary>无变更。</summary>
    None = 0,

    /// <summary>新增。</summary>
    Create = 1,

    /// <summary>修改。</summary>
    Update = 2,

    /// <summary>删除（含软删除）。</summary>
    Delete = 3
}
