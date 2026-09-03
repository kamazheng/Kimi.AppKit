using Kimi.AppKit.Core.Abstractions;
using Kimi.AppKit.Core.Contracts;
using Kimi.AppKit.Web.Excel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Kimi.AppKit.Web.Crud;

/// <summary>
/// 给一个已登记的实体映射出一组受控的 CRUD 端点。
/// </summary>
/// <remarks>
/// 【它取代了什么】前身把「读写任意表」压缩成一个端点组（<c>/api/v1/GeneralDb/*</c>），
/// 表名由客户端以字符串传入。两个后果：
/// <list type="number">
/// <item>**没有挂授权的自然位置**——授权只能对「所有表」整组一刀切，
///       实际结果是写端点标了 <c>[Authorize]</c>、读端点忘了标，任何人可匿名读全库</item>
/// <item>「这个系统对外开放了哪些表」这件事，**代码里没有任何一处在说明**</item>
/// </list>
/// 改成每实体显式映射之后，两个问题同时消失：登记清单就是开放面清单，
/// 而每个实体的读、写各自挂自己的策略。
///
/// 【⚠️ 出厂即要求登录】路由组默认带 <c>RequireAuthorization()</c>。
/// 要开放匿名读必须显式调 <see cref="KCrudEndpoints.AllowAnonymousRead"/>——
/// 方向是刻意的：忘了写等于更严，而不是更松。
/// </remarks>
public static class KCrudEndpointRouteBuilderExtensions
{
    /// <summary>
    /// 映射 <typeparamref name="TEntity"/> 的 CRUD 端点。
    /// </summary>
    /// <param name="endpoints">路由构建器。</param>
    /// <param name="prefix">
    /// 路由前缀。默认 <c>api/crud/{实体名小写}</c>。
    /// </param>
    /// <remarks>
    /// ⚠️ 需要先经 <c>services.AddKCrud&lt;TContext&gt;().AddEntity&lt;TEntity&gt;()</c> 登记，
    /// 否则解析不出 <see cref="ICrudDataSource{T}"/>，请求会在第一次命中时抛。
    ///
    /// ⚠️ <c>new()</c> 约束来自 Excel 导入（要能逐行构造实体），
    /// 与 <c>KEntityCrudPage&lt;T&gt;</c> 的约束一致。这对 EF 实体不构成负担——
    /// EF Core 本来就要求可构造，没有无参构造的类型压根映射不出来。
    /// </remarks>
    public static KCrudEndpoints MapCrudEndpoints<TEntity>(
        this IEndpointRouteBuilder endpoints, string? prefix = null)
        where TEntity : class, new()
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // ⚠️ 导出/导入 handler 依赖 IExcelService。没注册的话，minimal API 会把这个
        //    解析不到的接口参数**推断成请求体**，启动期抛「Body (Inferred)」——
        //    那条错误信息指不到真正原因。这里提前给出可行动的提示。
        if (endpoints.ServiceProvider.GetService<IExcelService>() is null)
        {
            throw new InvalidOperationException(
                $"MapCrudEndpoints<{typeof(TEntity).Name}>() 需要 IExcelService（导出/导入用），" +
                "但容器里没有注册。请先调用 services.AddAppKitExcel()。");
        }

        var name = typeof(TEntity).Name;
        var root = prefix ?? $"api/crud/{name.ToLowerInvariant()}";

        // 读写分成两个 group，才可能分别挂授权。两组共用同一个路由前缀。
        var read = endpoints.MapGroup(root).WithTags(name);
        var write = endpoints.MapGroup(root).WithTags(name);

        read.RequireAuthorization();
        write.RequireAuthorization();

        // ⚠️ "/export" 与 "/{id}" 同前缀不冲突：ASP.NET 路由的字面量段优先级高于参数段，
        //    /export 永远命中导出而不会被当成 id。这条依赖路由优先级规则，改路由名前先确认。
        read.MapGet("/", LoadAsync<TEntity>).WithName($"{name}_List");
        read.MapGet("/export", ExportAsync<TEntity>).WithName($"{name}_Export");
        read.MapGet("/{id}", GetAsync<TEntity>).WithName($"{name}_Get");

        // ⚠️ 请求体**不能**声明成 `TEntity item` 参数，尽管那才是 minimal API 的常规写法。
        //    路由 handler 的参数类型一旦是开放泛型形参，RouteHandlerAnalyzer 就无法静态推断
        //    「这是 body 还是路由/查询参数」，直接抛 NullReferenceException 让整个编译失败
        //    （AD0001）。方法组、lambda、加 [FromBody] 三种写法实测全崩；
        //    返回类型里带 Ok<TEntity> 反而没事——是**参数**触发的。
        //    改为在 handler 内手动读 body，再用 .Accepts<TEntity>() 把 OpenAPI 元数据补回来。
        write.MapPost("/", UpsertAsync<TEntity>)
             .Accepts<TEntity>("application/json")
             .WithName($"{name}_Upsert");
        write.MapPost("/import", ImportAsync<TEntity>).WithName($"{name}_Import").DisableAntiforgery();
        write.MapDelete("/{id}", DeleteAsync<TEntity>).WithName($"{name}_Delete");

        return new KCrudEndpoints(read, write);
    }

    // ── 读 ──────────────────────────────────────────────────────────────

    private static async Task<Ok<KPage<TEntity>>> LoadAsync<TEntity>(
        KQueryRequest query,
        ICrudDataSource<TEntity> source,
        CancellationToken cancellationToken)
        => TypedResults.Ok(await source.LoadAsync(query.Query, cancellationToken));

    private static async Task<Results<Ok<TEntity>, NotFound>> GetAsync<TEntity>(
        string id,
        ICrudDataSource<TEntity> source,
        CancellationToken cancellationToken)
    {
        var item = await source.GetAsync(id, cancellationToken);
        return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
    }

    private static async Task<Results<FileContentHttpResult, NoContent>> ExportAsync<TEntity>(
        KQueryRequest query,
        ICrudDataSource<TEntity> source,
        IExcelService excel,
        CancellationToken cancellationToken)
    {
        // ⚠️ 导出走与列表**完全相同**的查询路径，只是不分页——否则「导出的和看到的不一致」
        //    这种缺陷只有用户拿两份数据比对时才会发现。
        var page = await source.LoadAsync(
            query.Query with { Page = 1, PageSize = KQuery.MaxPageSize }, cancellationToken);

        if (page.TotalCount == 0) return TypedResults.NoContent();

        var bytes = excel.Export(page.Items, typeof(TEntity).Name);
        return TypedResults.File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{typeof(TEntity).Name}.xlsx");
    }

    // ── 写 ──────────────────────────────────────────────────────────────

    /// <summary>新增或更新。请求体手动读，原因见映射处的注释。</summary>
    private static async Task<Results<Ok<TEntity>, ValidationProblem, BadRequest<string>>> UpsertAsync<TEntity>(
        HttpContext context,
        ICrudDataSource<TEntity> source,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        TEntity? item;
        try
        {
            item = await context.Request.ReadFromJsonAsync<TEntity>(cancellationToken);
        }
        catch (System.Text.Json.JsonException ex)
        {
            // 反序列化失败是**客户端**的问题，返回 400 而不是让它冒到全局异常处理器变成 500。
            return TypedResults.BadRequest($"请求体不是合法 JSON：{ex.Message}");
        }

        if (item is null) return TypedResults.BadRequest("请求体为空。");

        var result = await source.UpsertAsync(item, cancellationToken);
        return result.Succeeded
            ? TypedResults.Ok(item)
            : ToValidationProblem(result);
    }

    private static async Task<Results<NoContent, ValidationProblem>> DeleteAsync<TEntity>(
        string id,
        ICrudDataSource<TEntity> source,
        CancellationToken cancellationToken)
    {
        var result = await source.DeleteAsync(id, cancellationToken);
        return result.Succeeded
            ? TypedResults.NoContent()
            : ToValidationProblem(result);
    }

    private static async Task<Ok<KImportReport>> ImportAsync<TEntity>(
        IFormFile file,
        ICrudDataSource<TEntity> source,
        IExcelService excel,
        CancellationToken cancellationToken)
        where TEntity : class, new()
    {
        await using var stream = file.OpenReadStream();
        var rows = excel.Import<TEntity>(stream);

        var errors = new List<KImportError>();
        var succeeded = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var result = await source.UpsertAsync(rows[i], cancellationToken);
            if (result.Succeeded)
            {
                succeeded++;
                continue;
            }

            // ⚠️ 行号 = 下标 + 2：+1 转成 1-based，再 +1 跳过表头，与用户在 Excel 左侧看到的一致。
            //    前身是任一行出错就整体抛，用户只知道"导入失败"，得把表二分重传去定位。
            errors.Add(new KImportError(i + 2, string.Join("；", result.Errors)));
        }

        return TypedResults.Ok(new KImportReport(succeeded, errors));
    }

    /// <summary>
    /// 把 <see cref="KResult"/> 的失败原因转成 <c>ValidationProblem</c>。
    /// </summary>
    /// <remarks>
    /// ⚠️ 用 400 而不是 500：<see cref="KResult"/> 承载的是**预期内**的业务失败
    /// （并发冲突、记录不存在），不是意外。返回 500 会让全局异常处理器把它当事故上报，
    /// 而用户其实只需要刷新重试。
    /// </remarks>
    private static ValidationProblem ToValidationProblem(KResult result) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [string.Empty] = [.. result.Errors],
        });
}
