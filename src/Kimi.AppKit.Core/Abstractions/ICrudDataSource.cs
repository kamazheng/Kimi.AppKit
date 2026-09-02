namespace Kimi.AppKit.Core.Abstractions;

using Kimi.AppKit.Core.Contracts;

/// <summary>
/// 列表/表单组件取数与存数的唯一入口。**这是整个框架「渲染模式无关」的支点。**
/// </summary>
/// <typeparam name="T">行/表单的数据类型。</typeparam>
/// <remarks>
/// 【它解决的问题】同一套 CRUD 界面要能跑在三种宿主里：
/// <list type="bullet">
/// <item>Blazor WebAssembly —— 浏览器里没有 DbContext，只能走 HTTP</item>
/// <item>Blazor Server / 静态 SSR —— 可以直连 DbContext，多绕一圈 HTTP 是纯浪费</item>
/// <item>集成测试 —— 需要一个内存实现，不想起 Web 主机</item>
/// </list>
/// 组件只认这个接口，三种实现各自提供。**没有这层抽象，组件就会绑死在 HttpClient 上**，
/// 于是 Blazor Server 的宿主被迫为自己的页面提供一套 REST API 去自己调自己。
///
/// 【⚠️ 授权不在这一层】实现方必须自己保证鉴权。前身模板的通用查询端点把
/// 「读任意表」压缩成一个入口，结果读端点忘了加 <c>[Authorize]</c> 而写端点加了——
/// 变成任何人可匿名读全库。这不是偶然疏漏：这种抽象**消灭了挂授权的自然位置**。
/// 因此 HTTP 实现方必须走「显式白名单 + 默认拒绝」，而不是「忘了标注就裸奔」。
/// </remarks>
public interface ICrudDataSource<T>
{
    /// <summary>分页查询。</summary>
    Task<KPage<T>> LoadAsync(KQuery query, CancellationToken cancellationToken = default);

    /// <summary>按主键取单条。不存在返回 null。</summary>
    Task<T?> GetAsync(object id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 新增或更新。
    /// </summary>
    /// <remarks>
    /// ⚠️ <typeparamref name="T"/> **必须携带并发版本号**，实现方必须比对它。
    /// 前身实现用 <c>CurrentValues.SetValues(dto)</c> 覆盖已查出的实体，而 DTO 不带影子
    /// <c>RowVersion</c> 属性，于是并发令牌完全不参与——两个用户先后编辑，
    /// 后者**永远无感覆盖**前者（典型 lost update），而模型里明明配了并发令牌。
    /// </remarks>
    Task<KResult> UpsertAsync(T item, CancellationToken cancellationToken = default);

    /// <summary>按主键删除。</summary>
    Task<KResult> DeleteAsync(object id, CancellationToken cancellationToken = default);
}
