namespace Kimi.AppKit.Core.Entities;

/// <summary>
/// 声明「并发令牌是一个**真实的 CLR 属性**」，而不是影子属性。
/// </summary>
/// <remarks>
/// 【为什么需要它】默认的并发令牌是**影子属性**（SQL Server 用 <c>RowVersion</c>、
/// PostgreSQL 借用系统列 <c>xmin</c>）。影子属性不在 CLR 类型上，因此
/// **无法随 JSON 序列化往返**——这让「经 HTTP 编辑一条记录」在结构上不可能成功：
///
/// <list type="number">
/// <item>客户端 GET 拿到实体，JSON 里没有令牌（影子属性序列化不出来）</item>
/// <item>客户端 POST 回来，反序列化出的实体令牌是默认值</item>
/// <item><c>Update(item)</c> 拿默认值当 <c>OriginalValue</c> 拼 WHERE 子句</item>
/// <item>匹配不到任何行 → <c>DbUpdateConcurrencyException</c></item>
/// </list>
///
/// 用户看到的是「这条记录已被其他人修改，请刷新后重试」——**而实际上没有任何冲突**，
/// 刷新多少次都一样。
///
/// 【为什么是字符串而不是 byte[]】跨 provider 一致。SQL Server 的 <c>rowversion</c>
/// 是 <c>byte[]</c>、PostgreSQL 的 <c>xmin</c> 是 <c>uint</c>，两者都由**数据库**生成；
/// 而本接口的令牌由**应用**在每次保存时换新值，两个 provider 行为完全相同，
/// 也不依赖任何 provider 专有类型。
///
/// 【先例】ASP.NET Core Identity 的 <c>IdentityUser.ConcurrencyStamp</c> 正是这个做法。
///
/// 【⚠️ 什么时候不用它】纯服务端读写、不经 HTTP 往返的实体保持影子属性即可——
/// 数据库原生的 <c>rowversion</c>/<c>xmin</c> 不需要应用维护，更省心也更可靠。
/// </remarks>
public interface IConcurrencyStamped
{
    /// <summary>
    /// 并发令牌。**由框架在每次保存时自动换新值，业务代码不要手写。**
    /// </summary>
    /// <remarks>
    /// ⚠️ 客户端编辑时必须把读到的值**原样带回来**——它是「我基于哪个版本做的修改」
    /// 这一事实的唯一载体。丢掉它等于放弃并发保护（后写覆盖前写且无人知晓）。
    /// </remarks>
    string? ConcurrencyStamp { get; set; }
}
