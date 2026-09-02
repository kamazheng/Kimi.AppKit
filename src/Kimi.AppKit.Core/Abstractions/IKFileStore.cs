namespace Kimi.AppKit.Core.Abstractions;

using Kimi.AppKit.Core.Contracts;

/// <summary>上传结果。</summary>
/// <param name="FileId">文件标识，用于后续取回或引用。</param>
/// <param name="Url">可直接访问的地址。⚠️ 若实现方用签名 URL，这个值是**有时效**的，不要落库。</param>
public sealed record KUploadResult(string FileId, string? Url = null);

/// <summary>文件元数据。</summary>
/// <param name="FileId">文件标识。</param>
/// <param name="FileName">原始文件名。</param>
/// <param name="Length">字节数。</param>
/// <param name="ContentType">MIME 类型。</param>
public sealed record KFileInfo(string FileId, string FileName, long Length, string? ContentType);

/// <summary>
/// 文件存取。把「文件存哪、怎么取」与业务代码解耦。
/// </summary>
/// <remarks>
/// ⚠️ **大小上限必须由实现方（服务端）说了算，不能只在客户端组件里校验。**
/// 前身模板在三个上传组件里各写了一份上限（120MB / 20MB / 120MB，互不一致），
/// 接口的默认参数写 50MB 而实现写 120MB——C# 默认参数按**调用点静态类型**解析，
/// 于是走接口调用的地方实际拿到的是 50MB。更糟的是服务端根本没设
/// <c>[RequestSizeLimit]</c>，Kestrel 默认约 28.6MB，**客户端校验通过、服务端照样拒收**。
/// </remarks>
public interface IKFileStore
{
    /// <summary>上传。<paramref name="progress"/> 报告 0..1 的进度。</summary>
    Task<KUploadResult> UploadAsync(
        FileUploadPayload payload,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>取元数据。不存在返回 null。</summary>
    Task<KFileInfo?> GetInfoAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>取访问地址。</summary>
    Task<string?> GetUrlAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>删除。</summary>
    Task<KResult> DeleteAsync(string fileId, CancellationToken cancellationToken = default);
}
