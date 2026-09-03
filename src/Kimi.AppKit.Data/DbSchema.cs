namespace Kimi.AppKit.Data;

/// <summary>
/// 数据库 schema 名的单一真相源。
/// </summary>
/// <remarks>
/// 【⚠️ 常量名与取值都不要改】P2 的量化调研里这两个常量在最大消费方（MES）有 **86 处**引用，
/// 是所有被评估能力里用量最高的一个。改名会引发大面积编译失败；改**取值**更糟——
/// 那会让既有数据库里的表全部「找不到」，且要靠一次全表迁移才能恢复。
///
/// 【为什么分两个 schema】<see cref="Reference"/> 放基础数据/配置（变更少、常被别的表引用），
/// <see cref="Data"/> 放业务流水（增长快）。分开后备份策略与权限可以分别设定。
/// </remarks>
public static class DbSchema
{
    /// <summary>基础数据/配置表所在 schema。</summary>
    public const string Reference = "Reference";

    /// <summary>业务数据表所在 schema。</summary>
    public const string Data = "Data";
}
