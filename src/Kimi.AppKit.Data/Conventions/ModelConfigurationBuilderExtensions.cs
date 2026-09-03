using Microsoft.EntityFrameworkCore;

namespace Kimi.AppKit.Data.Conventions;

/// <summary>
/// 全局类型约定。
/// </summary>
public static class ModelConfigurationBuilderExtensions
{
    /// <summary>
    /// 挂上 AppKit 的全局类型约定。在 <c>ConfigureConventions</c> 里调用。
    /// </summary>
    /// <remarks>
    /// 继承 <see cref="Auditing.AuditableDbContext"/> 时它已被自动调用；
    /// 用组合方式接入审计的上下文需要自己调一次。
    /// </remarks>
    public static ModelConfigurationBuilder ApplyAppKitConventions(this ModelConfigurationBuilder builder)
    {
        // ⚠️ 全局强制，不给逐属性配的机会。
        // Npgsql 的 timestamptz 只接受 Offset == 0，漏配一个属性就会在 PG 上写入时抛，
        // 且只有在收到非 UTC 输入时才触发——本地开发和集成测试通常都用 UtcNow，测不出来。
        builder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();

        return builder;
    }
}
