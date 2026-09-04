using Kimi.AppKit.Core.Abstractions;

namespace KMoldApp.Data;

/// <summary>
/// 设计时的占位身份。
/// </summary>
/// <remarks>
/// <c>dotnet ef</c> 不经过 DI 容器，而 <see cref="KMoldDbContext"/> 的构造需要
/// <see cref="IKCurrentUser"/>。设计时只用来构建模型、不产生任何需要落审计的写操作，
/// 所以给一个固定值即可。
///
/// ⚠️ 放在 Data 工程而不是各迁移工程里：两个迁移工程都要用它，
/// 各写一份就是两份会漂移的真相。
/// </remarks>
public sealed class DesignTimeCurrentUser : IKCurrentUser
{
    /// <summary>单例。</summary>
    public static readonly DesignTimeCurrentUser Instance = new();

    private DesignTimeCurrentUser() { }

    /// <inheritdoc />
    public ValueTask<string> GetUserNameAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult("design-time");

    /// <inheritdoc />
    public ValueTask<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}
