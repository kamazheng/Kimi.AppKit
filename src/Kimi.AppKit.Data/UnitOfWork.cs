using Kimi.AppKit.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kimi.AppKit.Data;

/// <summary>
/// 事务的**唯一入口**。把一组操作包进一个事务，失败整体回滚。
/// </summary>
/// <remarks>
/// 【为什么只能有一个入口，不允许业务代码自己开事务】
/// 前身有两套实现：一套正确地把 <c>BeginTransactionAsync</c> 包在
/// <c>CreateExecutionStrategy().ExecuteAsync</c> 里，另一套裸调
/// <c>Database.BeginTransactionAsync()</c>，注释坦言「项目未开
/// <c>EnableRetryOnFailure</c> 故安全」。这种「当前恰好安全」的状态经不起时间考验——
/// 一旦为生产可靠性开启瞬时故障重试，裸事务会直接抛
/// <c>InvalidOperationException: The configured execution strategy does not support
/// user-initiated transactions</c>，而这通常只在**生产**环境开重试，
/// 开发和测试环境从未触发过。
///
/// 收成一个入口之后，「事务怎么开」只有一处代码要对，不会再出现第二种写法。
/// </remarks>
public sealed class UnitOfWork(DbContext context)
{
    /// <summary>
    /// 在一个事务里执行一组操作。<paramref name="action"/> 内部的所有 <c>SaveChanges</c>
    /// 属于同一个事务，任一步抛异常则整体回滚。
    /// </summary>
    public async Task<KResult> ExecuteAsync(
        Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // 已在外层事务里时不要再开一个：嵌套事务在多数 provider 上不被支持，
            // UnitOfWork 允许在别的 UnitOfWork.ExecuteAsync 内部再调用一次而不出错。
            var owned = context.Database.CurrentTransaction is null;
            var tx = owned
                ? await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            try
            {
                await action(cancellationToken).ConfigureAwait(false);
                if (tx is not null) await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return KResult.Ok();
            }
            catch (Exception ex)
            {
                if (tx is not null) await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return KResult.Fail(ex.Message);
            }
            finally
            {
                if (tx is not null) await tx.DisposeAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }
}
