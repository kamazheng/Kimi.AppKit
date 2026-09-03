namespace Kimi.AppKit.Core.Contracts;

/// <summary>
/// 走 <c>multipart/form-data</c> 的端点描述。
/// </summary>
/// <typeparam name="TOutbound">出参类型。</typeparam>
/// <remarks>
/// 【为什么普通端点没有对应的契约，而这个有】普通 JSON 端点已经全部改用
/// **Refit** 声明——一个接口方法就是一个端点，路径、方法、入参、出参都在签名里，
/// 客户端实现由源生成器产出。手写契约在那条路上没有任何剩余价值。
///
/// multipart 留下来，是因为它**确实不同**：请求体是流 + 表单字段的组合，
/// 承载文件的类型在两端还不一样（浏览器侧 <c>IBrowserFile</c>、服务端 <c>IFormFile</c>），
/// 没法用一个 Refit 签名同时表达。把它单列成一个契约，是为了让
/// 「这个端点不走 JSON」这件事**在类型层面可见**——
/// 前身把一个上传端点挂在通用 JSON 契约下却走了完全不同的发送路径，
/// 而这个例外只有读实现才能发现。
///
/// 【已删除的部分】本文件原名 <c>IStandardApi.cs</c>，还含
/// <c>IStandardApi&lt;,&gt;</c> / <c>StandardApi&lt;,&gt;</c> / <c>StandardApiOnlyIn</c> /
/// <c>StandardApiOnlyOut</c> 四个类型。它们的形状是「请求对象兼作结果槽」
/// （<c>Response</c> 是可变属性，实例一次性、非线程安全），改用 Refit 后消费方归零，
/// 整族删除。若日后从 MES 导入那 378 个派生类，**不要**把这套契约复活——
/// 那是要改写成 Refit 接口的工作量，不是要保留的兼容面。
/// </remarks>
public interface IMultipartApi<TOutbound>
{
    /// <summary>相对路径。不要带前导斜杠。</summary>
    string Path { get; }

    /// <summary>要上传的内容。</summary>
    FileUploadPayload? Payload { get; }

    /// <summary>随文件一起提交的表单字段。</summary>
    IReadOnlyDictionary<string, string> Fields { get; }
}
