using System.Text.Json;
using Kimi.AppKit.Core.Http;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace Kimi.AppKit.Http;

/// <summary>
/// Refit 客户端注册的标准封装（决策 14：废弃 <c>IStandardApi</c>，客户端契约改用 Refit）。
/// </summary>
/// <remarks>
/// 【为什么不直接用 Refit 官方的 <c>AddRefitClient&lt;T&gt;()</c>】
/// 那个方法在解析不出源生成实现时会静默回落到反射请求构建器——回落本身没有编译期或
/// 明显的运行期信号，只有在 trimmed/Native AOT 发布产物上才会炸
/// （<c>System.NotSupportedException: This interface needs the reflection request
/// builder, which is not installed</c>）。Refit 官方自己的
/// <c>AddRefitGeneratedClient&lt;T&gt;()</c> 强制走源生成实现，生成失败就编译期报错，
/// 这正是决策 14 要的效果，本类内部就调它。
///
/// 【这一层还做了两件官方方法没做的事】
/// 1. 每次调用都要传一份 <see cref="RefitSettings"/> 的话，每个消费方各自拼一份
///    JSON 约定与错误处理——这正是本仓库反复踩过的「每个人抄一遍」。JSON 固定用
///    <see cref="JsonSerializerDefaults.Web"/>（与 <c>KHttpCrudDataSource</c>/
///    <c>GetFromJsonAsync</c> 的既有行为一致，否则大小写策略不一致会导致换传输层后
///    突然反序列化不出来），失败响应经 <see cref="KHttpResponseExtensions.BuildException"/>
///    转成信息完整的异常（提取 <c>detail</c>/<c>message</c>/<c>title</c>，而不是
///    Refit 默认 <c>ApiException</c> 只报状态码）。
/// 2. 顺手把 <c>BaseAddress</c> 也配好，调用方不用再单独 <c>ConfigureHttpClient</c>。
///
/// 【⚠️ 为什么方法不叫 <c>AddRefitGeneratedClient</c>】
/// 叫那个名字会与 Refit 官方的 <c>Refit.HttpClientFactoryExtensions.AddRefitGeneratedClient&lt;T&gt;()</c>
/// 撞名。实测：只要调用文件里有 <c>using Refit;</c>，
/// <c>services.AddRefitGeneratedClient&lt;T&gt;()</c>（不传 baseAddress）就**编译通过**，
/// 静默解析到官方那个无参重载——统一 JSON 约定、<c>ExceptionFactory</c>、
/// <c>CollectionFormat</c> 全部失效，症状与决策 14 要消灭的那些一模一样
/// （错误提示退化成状态码、数组查询条件被静默忽略），且只在运行时暴露。
/// 换成本包专属的名字后，「有没有走统一封装」才是 grep 得出来的。
///
/// 【⚠️ 不要给调用方开洞传自定义 <see cref="RefitSettings"/>】
/// 统一配置正是这个类存在的意义；一旦开洞，第一个「特殊情况」就会让约定重新分裂。
/// 真需要不同行为时，应评估是否要在这里加一个新的可选参数（如 <c>httpClientName</c>），
/// 而不是整个替换 settings。
///
/// 【⚠️ 「零反射」只覆盖请求构建，不覆盖 JSON】
/// 源生成消灭的是 Refit 拼请求那一段的反射；序列化仍走
/// <see cref="SystemTextJsonContentSerializer"/> + 一份没有 <c>TypeInfoResolver</c> 的
/// <see cref="JsonSerializerOptions"/>，即基于反射的 <c>System.Text.Json</c>。
/// 实测把本工程 <c>IsAotCompatible=true</c> 编译**不报任何 IL2026/IL3050**——不是因为安全，
/// 而是 Refit 在序列化器内部把这些告警抑制掉了（其 <c>ReflectionFallbackJustification</c>）。
/// 也就是说真上 Native AOT 时没有编译期信号，只会在运行时炸。当前消费方（Blazor WASM，
/// 裁剪但未关反射）跑得通；哪天真要 Native AOT，解法是往
/// <see cref="JsonSerializerOptions.TypeInfoResolver"/> 挂一个源生成的
/// <c>JsonSerializerContext</c>，而不是回头怀疑 Refit。
/// </remarks>
public static class RefitServiceCollectionExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 注册一个 Refit 生成的客户端，绑定到 <paramref name="baseAddress"/>。
    /// </summary>
    /// <typeparam name="T">Refit 接口类型（方法上带 <c>[Get]</c>/<c>[Post]</c> 等特性）。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="baseAddress">
    /// 绝对地址。实测（Refit 15.2）带路径前缀时**不需要**末尾斜杠：
    /// <c>http://host/api</c> 与 <c>http://host/api/</c> 配 <c>[Get("/things/{id}")]</c>
    /// 都得到 <c>http://host/api/things/1</c>，不会像裸 <c>HttpClient</c> 那样把 <c>/api</c> 吃掉。
    /// </param>
    /// <remarks>
    /// 数组类型的查询参数按 <see cref="CollectionFormat.Multi"/> 展开成重复 key
    /// （<c>key=a&amp;key=b</c>），与 ASP.NET Core <c>[FromQuery] string[]</c> 的
    /// 默认绑定方式一致——用默认的 <see cref="CollectionFormat.RefitParameterFormatter"/>
    /// 会拼成逗号分隔的单个值，服务端那种绑定方式认不出来，表现为查询条件被静默忽略。
    /// </remarks>
    public static IHttpClientBuilder AddAppKitRefitClient<T>(
        this IServiceCollection services, string baseAddress) where T : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseAddress);

        var settings = new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(JsonOptions),
            CollectionFormat = CollectionFormat.Multi,
            ExceptionFactory = async response =>
            {
                if (response.IsSuccessStatusCode) return null;

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return KHttpResponseExtensions.BuildException(response.StatusCode, content);
            },
        };

        return services
            .AddRefitGeneratedClient<T>(settings)
            .ConfigureHttpClient(client => client.BaseAddress = new Uri(baseAddress, UriKind.Absolute));
    }
}
