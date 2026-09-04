using Xunit;

namespace KMoldApp.Tests.Database;

/// <summary>
/// 所有「打真实数据库」的测试类都必须加入这个 collection。
/// </summary>
/// <remarks>
/// 【为什么】xUnit 默认把**不同测试类**放进不同 collection 并行执行，
/// 而这些测试各自都要 <c>EnsureDeletedAsync</c> + <c>EnsureCreatedAsync</c> 同一个测试库——
/// 并行时互相清库，症状是 <c>3D000: database "..." does not exist</c>，
/// 而且**单独跑每个类都通过**，只有整套一起跑才炸。
///
/// ⚠️ 新增打真库的测试类时记得加 <c>[Collection(DatabaseCollection.Name)]</c>，
/// 否则它会重新引入这个竞态。
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public class DatabaseCollection
{
    public const string Name = "database";
}
