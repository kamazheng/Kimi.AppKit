using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Kimi.AppKit.Web.Crud;

/// <summary>
/// <c>MapCrudEndpoints&lt;T&gt;()</c> 的返回值：把读路由与写路由分成两个原生
/// <see cref="RouteGroupBuilder"/>，让授权可以分别挂。
/// </summary>
/// <remarks>
/// 【为什么读写要分开挂授权】这正是前身出事故的地方：它把「读写任意表」压缩成一个端点组，
/// 于是**授权只能整组一刀切**。实际结果是写端点标了 <c>[Authorize]</c>、读端点忘了标，
/// 变成任何人可匿名读全库。分成两个 group 之后，「谁能读」与「谁能写」是两个必须各自回答的问题。
///
/// 【为什么暴露原生 builder 而不是自造一层】
/// <see cref="Read"/> 与 <see cref="Write"/> 就是 ASP.NET 的 <see cref="RouteGroupBuilder"/>，
/// 消费方可以直接用生态里所有既有的扩展方法（<c>RequireRateLimiting</c>、<c>AddEndpointFilter</c>、
/// <c>WithOpenApi</c>…）。自造一个 <c>IEndpointConventionBuilder</c> 包装层只会
/// 让这些扩展方法用不上，而它一点额外能力都没提供。
/// </remarks>
public sealed class KCrudEndpoints
{
    internal KCrudEndpoints(RouteGroupBuilder read, RouteGroupBuilder write)
    {
        Read = read;
        Write = write;
    }

    /// <summary>只读路由组：列表、按主键取单条、导出。</summary>
    public RouteGroupBuilder Read { get; }

    /// <summary>写入路由组：新增/更新、删除、导入。</summary>
    public RouteGroupBuilder Write { get; }

    /// <summary>给读路由挂授权策略。</summary>
    public KCrudEndpoints RequireReadAuthorization(params string[] policyNames)
    {
        Read.RequireAuthorization(policyNames);
        return this;
    }

    /// <summary>给写路由挂授权策略。</summary>
    public KCrudEndpoints RequireWriteAuthorization(params string[] policyNames)
    {
        Write.RequireAuthorization(policyNames);
        return this;
    }

    /// <summary>
    /// 读写用同一套策略。
    /// </summary>
    /// <remarks>
    /// ⚠️ 提供这个便捷方法是为了少写一行，**不是**推荐做法。
    /// 「能看」和「能改」在多数系统里不是同一批人；用它之前先确认这个实体真的不需要区分。
    /// </remarks>
    public KCrudEndpoints RequireAuthorization(params string[] policyNames)
    {
        Read.RequireAuthorization(policyNames);
        Write.RequireAuthorization(policyNames);
        return this;
    }

    /// <summary>
    /// 把读路由开放给匿名访问。
    /// </summary>
    /// <remarks>
    /// ⚠️ 必须显式调用才生效——出厂默认是要求登录的。
    /// 这个方向是刻意的：忘了写 = 更严，而不是更松。
    /// </remarks>
    public KCrudEndpoints AllowAnonymousRead()
    {
        Read.AllowAnonymous();
        return this;
    }
}
