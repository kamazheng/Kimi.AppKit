namespace Kimi.AppKit.Core.Contracts;

/// <summary>
/// 一个 HTTP 端点的**类型化描述**：路径、方法、入参类型、出参类型。
/// 客户端与服务端共享同一份定义，端点签名的改动因此是编译期可见的。
/// </summary>
/// <typeparam name="TInbound">入参类型。</typeparam>
/// <typeparam name="TOutbound">出参类型。</typeparam>
/// <remarks>
/// 【⚠️ 实例是一次性的，不可复用、不是线程安全的】
/// <see cref="Response"/> 是可变属性，同一个实例既承载入参又承载调用后被回填的结果。
/// 这意味着：**一次调用一个新实例**，不要缓存、不要在并发路径上共享同一个 api 对象。
///
/// 这个形状不够纯（请求对象兼作结果槽），但它是既成事实：仅 MES 一个消费方就有
/// 378 个派生类、174 处客户端调用、301 处服务端读 <see cref="Response"/>。
/// 保持源码兼容的收益远大于「重新设计成不可变」的收益，所以这里选择把约束显式写出来，
/// 而不是推倒重来。新代码建议直接用发送方法的**返回值**，不要读 <see cref="Response"/>。
///
/// 【⚠️ multipart 端点不适用本契约】
/// 文件上传走 <c>multipart/form-data</c>，不经本契约默认的 JSON 序列化路径。
/// 那类端点请实现 <see cref="IMultipartApi{TOutbound}"/>，不要继续伪装成普通 API——
/// 前身实现里有一个上传 API 挂在本契约下却走了完全不同的路径，且这个例外没在类型层面表达出来。
/// </remarks>
public interface IStandardApi<TInbound, TOutbound>
{
    /// <summary>相对路径，如 <c>api/v1/workorder/get</c>。不要带前导斜杠。</summary>
    string Path { get; }

    /// <summary>HTTP 方法。</summary>
    HttpMethod HttpMethod { get; }

    /// <summary>入参。</summary>
    TInbound? Request { get; }

    /// <summary>
    /// 出参。由发送方在调用完成后回填。
    /// ⚠️ 新代码请用发送方法的返回值，不要读这个属性——它存在只是为了兼容既有的 301 处用法。
    /// </summary>
    TOutbound? Response { get; set; }
}

/// <summary>
/// 走 <c>multipart/form-data</c> 的端点。与 <see cref="IStandardApi{TInbound,TOutbound}"/>
/// 分开，是为了让「这个端点不走 JSON」这件事在类型层面可见，而不是靠读实现才发现。
/// </summary>
/// <typeparam name="TOutbound">出参类型。</typeparam>
public interface IMultipartApi<TOutbound>
{
    /// <summary>相对路径。</summary>
    string Path { get; }

    /// <summary>要上传的内容。</summary>
    FileUploadPayload? Payload { get; }

    /// <summary>随文件一起提交的表单字段。</summary>
    IReadOnlyDictionary<string, string> Fields { get; }
}

/// <summary>
/// <see cref="IStandardApi{TInbound,TOutbound}"/> 的抽象基类。派生类只需给出
/// <see cref="Path"/> 与 <see cref="HttpMethod"/>，入参经构造函数传入。
/// </summary>
public abstract class StandardApi<TInbound, TOutbound>(TInbound request)
    : IStandardApi<TInbound, TOutbound>
{
    /// <inheritdoc />
    public abstract string Path { get; }

    /// <inheritdoc />
    public abstract HttpMethod HttpMethod { get; }

    /// <inheritdoc />
    public TInbound? Request { get; } = request;

    /// <inheritdoc />
    public TOutbound? Response { get; set; }
}

/// <summary>只有入参、返回 bool 的端点。</summary>
public abstract class StandardApiOnlyIn<TInbound>(TInbound request)
    : StandardApi<TInbound, bool>(request);

/// <summary>无入参、只有出参的端点。</summary>
public abstract class StandardApiOnlyOut<TOutbound>() : StandardApi<int, TOutbound>(0);

/// <summary>无入参、返回 bool 的端点。</summary>
public abstract class StandardApi() : StandardApi<int, bool>(0);
