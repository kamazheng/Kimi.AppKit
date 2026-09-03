using System.Text.Json;
using Kimi.AppKit.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kimi.AppKit.Data.Settings;

/// <summary>
/// 落库的设置项读写。四层解析：数据库 → 调用方默认值 → 注册的默认值（并回写）→ <c>default(T)</c>。
/// </summary>
/// <typeparam name="TContext">应用的 <see cref="DbContext"/>。</typeparam>
/// <typeparam name="TEntity">
/// 消费方自己声明的设置实体，见 <see cref="IKSettingEntity"/>——**实体不在包里**。
/// </typeparam>
/// <remarks>
/// 【⚠️ 读操作会写数据库】<see cref="GetAsync{T}"/> 在数据库里没有该键、
/// 而 <see cref="SettingDefaults"/> 里登记过默认值时，会**把默认值插进数据库**。
/// 这是刻意的（管理界面才能看到并编辑这一项），但有两个后果必须知道：
/// <list type="number">
/// <item>**只读副本上会失败。** 若把读请求路由到只读库，第一次读一个未初始化的设置就会抛</item>
/// <item>它参与调用方所在的事务。在一个大事务里读设置，这次插入会跟着一起回滚</item>
/// </list>
/// 不想要这个副作用时，用 <see cref="GetRawJsonAsync"/>——它纯读。
///
/// 【为什么不缓存】设置的语义是「运维在界面上改、**立刻**生效」。
/// 进程内缓存会让多副本部署出现「同一个操作在不同副本上结果不同」（铁律 6）。
/// 确实需要削减查询次数的调用方，应当在自己那一层按自己的容忍度缓存
/// （样例的 <c>SettingBackedErrorDetailPolicy</c> 就是这么做的，TTL 1 分钟）。
/// </remarks>
public sealed class KSettingService<TContext, TEntity>(
    TContext db,
    SettingDefaults defaults,
    ILogger<KSettingService<TContext, TEntity>> logger) : ISettingService
    where TContext : DbContext
    where TEntity : class, IKSettingEntity, new()
{
    private DbSet<TEntity> Settings => db.Set<TEntity>();

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(
        string key, T? defaultValue = default, CancellationToken cancellationToken = default)
        where T : ISetting
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var stored = await FindAsync(key, cancellationToken);
        if (stored is not null) return Deserialize<T>(stored.Value, key);

        // 调用方给了默认值：用它，并在该键已登记时落库，好让管理界面看得见这一项。
        if (defaultValue is not null)
        {
            if (defaults.All.TryGetValue(key, out var registered))
            {
                await TryInsertAsync(key, Serialize(defaultValue), registered.ValueTypeFullName,
                    defaultValue.Description, registered.IsSystem, cancellationToken);
            }

            return defaultValue;
        }

        // 回落到注册的默认值。
        if (defaults.All.TryGetValue(key, out var entry))
        {
            await TryInsertAsync(key, entry.DefaultValueJson, entry.ValueTypeFullName,
                entry.Description, entry.IsSystem, cancellationToken);
            return Deserialize<T>(entry.DefaultValueJson, key);
        }

        return default;
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        where T : ISetting
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var json = Serialize(value);
        var typeName = TypeName(typeof(T));
        var stored = await FindAsync(key, cancellationToken);

        if (stored is not null)
        {
            stored.Value = json;
            stored.ValueTypeFullName = typeName;
            stored.Description = value.Description;
        }
        else
        {
            defaults.All.TryGetValue(key, out var registered);
            Settings.Add(new TEntity
            {
                Name = key,
                Value = json,
                ValueTypeFullName = typeName,
                Description = value.Description,
                IsSystem = registered?.IsSystem ?? false,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Settings.AnyAsync(s => s.Name == key, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var stored = await FindAsync(key, cancellationToken);
        if (stored is null)
        {
            logger.LogDebug("设置 {Key} 不存在，删除无需执行。", key);
            return;
        }

        if (stored.IsSystem)
        {
            throw new InvalidOperationException(
                $"设置 {key} 是系统项，不能删除。要恢复出厂值请用 {nameof(ResetToDefaultAsync)}。");
        }

        Settings.Remove(stored);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ResetToDefaultAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!defaults.All.TryGetValue(key, out var entry))
        {
            throw new InvalidOperationException($"设置 {key} 没有登记过默认值，无法恢复。");
        }

        var stored = await FindAsync(key, cancellationToken);
        if (stored is not null)
        {
            stored.Value = entry.DefaultValueJson;
            stored.ValueTypeFullName = entry.ValueTypeFullName;
            stored.Description = entry.Description;
            stored.IsSystem = entry.IsSystem;
        }
        else
        {
            Settings.Add(new TEntity
            {
                Name = key,
                Value = entry.DefaultValueJson,
                ValueTypeFullName = entry.ValueTypeFullName,
                Description = entry.Description,
                IsSystem = entry.IsSystem,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> GetRawJsonAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var stored = await FindAsync(key, cancellationToken);
        return stored?.Value;
    }

    private Task<TEntity?> FindAsync(string key, CancellationToken cancellationToken) =>
        Settings.FirstOrDefaultAsync(s => s.Name == key, cancellationToken);

    /// <summary>
    /// 插入一条设置；并发下已被别人插入时**吞掉冲突**。
    /// </summary>
    /// <remarks>
    /// ⚠️ 两个请求同时首次读同一个未初始化的设置，两边都会走到这里，其中一个必然撞唯一索引。
    /// 这不是错误——另一个请求已经把值写好了，本次读到的默认值也是对的。
    /// 但失败的那条 Added 实体**必须从变更追踪器里摘掉**，否则调用方稍后任何一次
    /// <c>SaveChanges</c> 都会重放这次插入并再次抛出，而堆栈指向的是**那次**保存，
    /// 与真正的原因隔了十万八千里。
    /// </remarks>
    private async Task TryInsertAsync(
        string key, string json, string typeName, string? description, bool isSystem,
        CancellationToken cancellationToken)
    {
        var entity = new TEntity
        {
            Name = key,
            Value = json,
            ValueTypeFullName = typeName,
            Description = description,
            IsSystem = isSystem,
        };

        Settings.Add(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("设置 {Key} 首次读取，已按登记的默认值落库。", key);
        }
        catch (DbUpdateException ex)
        {
            db.Entry(entity).State = EntityState.Detached;
            logger.LogDebug(ex, "设置 {Key} 已被并发请求写入，本次插入忽略。", key);
        }
    }

    private T? Deserialize<T>(string json, string key) where T : ISetting
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            // ⚠️ 不要把 json 记进日志：设置项里可能有连接串、密钥这类东西。
            logger.LogError(ex, "设置 {Key} 的存储值无法反序列化为 {Type}，本次按未设置处理。",
                key, typeof(T).Name);
            return default;
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    /// <summary>取 <c>Namespace.TypeName, AssemblyName</c>，去掉版本与公钥。</summary>
    /// <remarks>
    /// 只给管理界面渲染编辑器用。带上版本号的话，程序集一升版这个字段就对不上了。
    /// </remarks>
    private static string TypeName(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";
}
