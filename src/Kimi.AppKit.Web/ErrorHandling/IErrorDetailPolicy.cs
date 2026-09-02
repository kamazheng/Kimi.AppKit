namespace Kimi.AppKit.Web.ErrorHandling;

/// <summary>
/// 是否把完整异常详情（<c>ToString()</c>，含 InnerException 链与堆栈）返回给客户端。
/// </summary>
/// <remarks>
/// 【⚠️ 默认值必须是 false】
/// 前身实现的默认值是 <c>true</c>——完整异常链**未经认证即可触达**任意能触发 500 的匿名接口，
/// 内部类型名、文件路径、EF Core 异常里可能带的 SQL 片段全部泄露。
/// 对只服务一个内网工厂的应用这是「方便调试」，对交付给不受控客户环境的商业产品，
/// 这是标准的信息泄露入口。
///
/// 「设置项没读到就沿用旧默认 true」本身也是一种 fail-open：读取失败时应该
/// 倒向**更安全**的一侧（不暴露），而不是倒向「上次成功读到的值，如果从来没成功过就是 true」。
/// </remarks>
public interface IErrorDetailPolicy
{
    /// <summary>本次请求是否应该暴露详细错误信息。</summary>
    ValueTask<bool> ShouldExposeDetailsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 恒定策略：不经任何配置源，直接返回固定值。
/// </summary>
/// <remarks>
/// 用于两种场景：① 开发环境，恒 true 方便调试；② 消费方尚未接入运行期可改的设置系统时的兜底。
/// **生产环境不应该用恒 true 的实例**——见 <see cref="IErrorDetailPolicy"/> 的默认值原则。
/// </remarks>
public sealed class FixedErrorDetailPolicy(bool exposeDetails) : IErrorDetailPolicy
{
    /// <summary>默认关闭详情的实例，供未显式配置时使用。</summary>
    public static readonly FixedErrorDetailPolicy Disabled = new(exposeDetails: false);

    /// <inheritdoc />
    public ValueTask<bool> ShouldExposeDetailsAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(exposeDetails);
}
