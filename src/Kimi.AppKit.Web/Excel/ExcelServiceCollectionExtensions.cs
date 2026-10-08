using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kimi.AppKit.Web.Excel;

/// <summary>
/// Excel 导入导出的注册入口。
/// </summary>
/// <remarks>
/// ⚠️ 这个入口原本**不存在**：包里有 <see cref="IExcelService"/> 与
/// 内部实现，却没有任何一处告诉消费方该怎么注册。
/// 于是 <c>MapCrudEndpoints&lt;T&gt;()</c> 的导出/导入 handler 依赖它却解析不到，
/// minimal API 把这个未注册的接口参数**推断成请求体**，
/// 启动期抛「Body (Inferred)」——错误信息完全指不到「忘了注册 IExcelService」。
///
/// 这条缺口没被端点测试发现，因为测试里手动注册了它。
/// 又一次印证：包自己的测试绿 ≠ 消费方能用。
/// </remarks>
public static class ExcelServiceCollectionExtensions
{
    /// <summary>注册基于 ClosedXML 的 Excel 导入导出。已注册自定义实现时不覆盖。</summary>
    public static IServiceCollection AddAppKitExcel(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IExcelService, ClosedXmlExcelService>();
        return services;
    }
}
