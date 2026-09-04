using KMoldApp.Tests.Endpoints;
using Xunit;

namespace KMoldApp.Tests;

/// <summary>需要真实数据库的集成测试。未配连接串时**跳过而不是失败**。</summary>
/// <remarks>
/// ⚠️ 跳过原因会写进测试报告，不会静默消失——这条很重要：
/// 一组「因为环境没配所以从没跑过」的测试，比没有测试更危险，
/// 因为它在 CI 的绿色里看起来像是覆盖到了。
/// </remarks>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppFixture.NpgsqlEnv)))
            Skip = $"未设置 {AppFixture.NpgsqlEnv}，跳过需要真实数据库的集成测试";
    }
}
