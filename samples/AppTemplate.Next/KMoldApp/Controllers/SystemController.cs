using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace KMoldApp.Controllers;

/// <summary>
/// 系统信息端点。
/// </summary>
/// <remarks>
/// 【⚠️ 相对旧模板删掉了四个「测试端点」】
/// <c>AbnormalExceptionTest</c>（故意抛异常）、<c>CustomizeExceptionTest</c>、
/// <c>TestLogLevel</c>（运行期改日志级别）、<c>LocalizeTest</c>。
/// 它们是开发期自检，不该出现在交付给客户的产品里——
/// 其中改日志级别的那个如果被匿名触达，既是信息泄露也是拒绝服务面。
/// 需要这类自检就写集成测试，不要在生产 API 上留后门。
///
/// 【⚠️ 首屏不再依赖本控制器】旧骨架的 <c>App.razor</c> 用
/// <c>autostart="false"</c> + 手动 <c>Blazor.start()</c>，启动前要先 fetch 环境名——
/// 那个请求撞上默认拒绝策略返回 401，于是 WASM 环境静默退化成 Production，
/// 首页白屏。新骨架用官方标准的自动启动，**这条依赖整个消失了**。
/// </remarks>
[ApiController]
[Route("api/v1/[controller]/[action]")]
public sealed class SystemController(IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>当前 ASP.NET Core 环境名。</summary>
    /// <remarks>
    /// ⚠️ 匿名可达：环境名是要显示在界面角标上的（让人一眼看出这是 Staging 不是生产），
    /// 登录页上就要能取到。它不含任何业务信息。
    /// </remarks>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult EnvironmentName() =>
        Ok(new { environment = environment.EnvironmentName });

    /// <summary>当前登录用户的身份信息。</summary>
    /// <remarks>⚠️ 需要登录——它回显 claim，其中可能有邮箱、部门等个人信息。</remarks>
    [HttpGet]
    public IActionResult UserInfo() =>
        Ok(new
        {
            name = User.Identity?.Name,
            isAuthenticated = User.Identity?.IsAuthenticated ?? false,
            roles = User.FindAll(ClaimTypes.Role).Concat(User.FindAll("role"))
                .Select(c => c.Value).Distinct().ToArray(),
        });
}
