using Xunit;

namespace KMoldApp.Tests.Database;

/// <summary>
/// 依赖外部环境（数据库连接串等）的 Fact —— 环境变量未设置时自动跳过，而不是失败。
/// </summary>
/// <remarks>
/// 让 `dotnet test` 在没有数据库的机器上（比如只跑单元测试的 CI 阶段）依然全绿，
/// 同时在配了连接串的环境里真的跑起来。跳过原因会写进测试报告，不会静默消失。
/// </remarks>
public sealed class EnvFactAttribute : FactAttribute
{
    public EnvFactAttribute(string environmentVariable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariable)))
            Skip = $"未设置环境变量 {environmentVariable}，跳过该 provider 的集成测试";
    }
}
