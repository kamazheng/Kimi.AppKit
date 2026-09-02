namespace Kimi.AppKit.Core.Contracts;

/// <summary>
/// 平台中立的文件上传载荷。
/// </summary>
/// <remarks>
/// 【为什么不直接用 IBrowserFile / IFormFile】
/// 前身实现把这两个类型放进了同一个共享契约类（浏览器直传用前者、服务端代传用后者，互斥地二选一），
/// 结果是共享工程被迫引用 <c>Microsoft.AspNetCore.Components.WebAssembly</c> 与
/// <c>Microsoft.AspNetCore.Http</c>——**把 UI 框架类型和 MVC 模型绑定类型焊进了本该平台无关的传输契约**。
/// 任何非 Blazor 的消费方（控制台工具、后台服务、集成测试）都要跟着连坐。
///
/// 现在的分工：契约层只认 <see cref="Stream"/>；两端各自在边缘写一个扩展方法完成转换
/// （<c>IBrowserFile.ToPayload()</c> / <c>IFormFile.ToPayload()</c>），互不知道对方存在。
///
/// 【⚠️ Content 的所有权不转移】本类型**不持有** Stream 的生命周期，不实现 IDisposable。
/// 调用方负责 using。这是刻意的：Stream 常常来自 <c>IBrowserFile.OpenReadStream()</c>
/// 这类有自己释放时机的来源，让载荷去 Dispose 它会造成重复释放。
/// </remarks>
/// <param name="Content">文件内容流。调用方保留所有权与释放责任。</param>
/// <param name="FileName">原始文件名。⚠️ 来自客户端，**不可信**——服务端落盘前必须自行生成安全名称。</param>
/// <param name="Length">字节数。用于在读流之前做大小校验，避免先读后拒。</param>
/// <param name="ContentType">MIME 类型。⚠️ 同样由客户端上报，**不可作为格式判据**；按文件头魔数判定。</param>
public sealed record FileUploadPayload(
    Stream Content,
    string FileName,
    long Length,
    string? ContentType = null);
