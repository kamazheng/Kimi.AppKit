# API 开发范式（强制）

> 第 0 条元规则同 `appkit-consumption.md`：**动手写任何端点之前，先确认 Kimi.AppKit 里
> 有没有现成的。** 本文件规定的是「包里没有、必须自己写」时的唯一写法。

---

## 一、`IStandardApi` 已废弃，不要找它，也不要重造

旧模板有一个 `IStandardApi` 的通用接口，把「读写任意实体」压缩成一组泛型端点。
**它已被明确否决**，理由不是风格问题：

- **开放面在代码里没有任何一处在说明。** 一个泛型端点等于「所有登记过的实体都对外开放」，
  于是「这个系统对外暴露了哪些表」无从回答，**权限判断因此无处可挂**。
- 每个实体真实的授权要求不同（有的只读、有的仅管理员、有的按所属人过滤），
  统一接口只能取最宽松的那个交集。

**替代方案是显式白名单**：`AddKCrud<TDbContext>().AddEntity<T>()` 逐个登记，
`MapCrudEndpoints<T>()` 逐个映射并各自挂授权。没登记的实体既解析不出数据源、也映射不出端点。

```csharp
// ✅ 开放面一目了然，且每个实体的授权各自独立
builder.Services.AddKCrud<AppDbContext>()
    .AddEntity<Setting>()
    .AddEntity<EmailTemplate>();

app.MapCrudEndpoints<Setting>().RequireAuthorization(AppPolicies.AdminOnly);
app.MapCrudEndpoints<EmailTemplate>().RequireAuthorization(AppPolicies.AdminOnly);
```

---

## 二、想做 X → 必须用 Y → 禁止自写 Z

| 想做的事 | 必须用 | 禁止 |
|---|---|---|
| 实体的增删改查接口 | `MapCrudEndpoints<T>()`（Kimi.AppKit.Web.Crud） | 手写一套 CRUD 控制器 |
| 客户端调用 CRUD | `AddHttpCrudDataSource<TDto>()` + `AddPrerenderCrudDataSource<TDto>()` | 自己 `HttpClient.GetFromJsonAsync` |
| 错误响应 | `TypedResults.Problem(...)` / `ValidationProblem` | `BadRequest("字符串")`、裸 JSON、自定义错误信封 |
| 客户端呈现错误 | `ApiErrorPresenter` + `AppErrorBoundary` | 每个页面各写一个 try/catch 弹窗 |
| 解析 HTTP 失败响应 | `EnsureSuccessOrThrowAsync()`（Kimi.AppKit.Core.Http） | 自己判 `IsSuccessStatusCode` 再拼消息 |
| 分页/排序/搜索契约 | 包里的 CRUD 契约类型 | 自定义 `page`/`size`/`sort` 参数名 |
| Excel 导入导出 | `IExcelService`（CRUD 端点已自带） | 自写 OpenXML 代码 |

---

## 三、新写端点时的硬约束

### 1. 一律 minimal API，不用控制器

`MapControllers()` 仍在管线里（第三方库可能需要），但**新代码不要写控制器**。
⚠️ 有一条实测过的坑：Cookie 转发 JwtBearer 的判据是「请求带没带 Bearer 令牌」，
**不是**端点上有没有 `[ApiController]`——那个特性只存在于 MVC 控制器上，
minimal API 端点压根没有这份元数据。混用两种风格会让这类判据出现盲区。

### 2. 路由前缀固定 `/api`

管线里有两处按这个前缀分支，**改前缀会同时破坏两处，且都是静默的**：

- SPA 状态码页只对非 `/api` 路径启用。API 落进状态码页会被重写成一整页 HTML，
  调用方 `response.json()` 当场抛 SyntaxError。
- 未认证时 `/api` 返回 **401**，其余路径才重定向到登录页。

### 3. 错误一律 `application/problem+json`

五个场景都要覆盖到，缺一个就会出现「同一个 API 有两种错误格式」：

| 场景 | 期望 |
|---|---|
| 资源不存在 | 404 + problem+json（**要有 body**，无 body 的 404 会被状态码页重写成 HTML） |
| 主键类型不对（如 `/api/crud/setting/abc`） | 404，**不是** 500 |
| 请求体非法 JSON | 400 + problem+json |
| 请求体为空 | 400 + problem+json |
| 校验失败 | 400 + `ValidationProblem` |

⚠️ **任何情况下不得把异常消息、堆栈、绝对路径回给调用方。** 那些进日志。

### 4. 授权显式挂在端点上

出厂是**默认拒绝**（没标注的端点一律要求登录）。因此：

- 必须匿名的端点显式 `.AllowAnonymous()`（健康检查、登录入口、静态资产）
- 需要角色的用**策略**：`.RequireAuthorization(AppPolicies.AdminOnly)`
- ⚠️ **禁止写自定义授权特性**。策略名与角色名都来自 `KMoldApp.Shared` 的常量——
  手写字面量写错不会有编译错误，只在有人真的访问那个端点时抛
  `The AuthorizationPolicy named ... was not found`。

### 5. 表单端点必须用 `[FromForm]` 显式绑定

⚠️ 图省事去 `ReadFormAsync()` 手取的话，端点**拿不到防伪元数据**，
`UseAntiforgery` 中间件会直接放行——防伪形同虚设，而页面上那个
`<AntiforgeryToken />` 还在，看起来一切正常。

### 6. 新端点必须出现在 OpenAPI 文档里

文档是接入方唯一的契约来源。`/scalar/v1` 上看不到的端点等于没有对外承诺。
⚠️ 生产环境默认关闭文档（`OpenApi:Enabled`），要开必须显式配置且强制管理员登录——
API 形状属于内部信息。

---

## 四、客户端调用 API：一律走 Refit，不要自写 `HttpClient` 封装

旧模板的 `IStandardApi` 家族（把「调哪个端点」压缩成客户端自己拼的路由对象）已被
**决策 14（2026-09-03）废弃**，替代方案是 [Refit](https://github.com/reactiveui/refit)——
一个接口方法就是一个端点，路径/方法/入参/出参都写在方法签名里，源生成实现，不反射拼请求。

选 Refit 而非自写的三条理由：请求构建走源生成、不反射，trimming 下不会退化；接口即文档，
契约变化编译期就能发现；社区维护、无需自己踩 `HttpClient` 生命周期与序列化的坑。

⚠️ 「零反射」只覆盖请求构建，**不覆盖 JSON 序列化**——那一段仍是反射版
`System.Text.Json`，且 Refit 在序列化器内部抑制了 IL2026/IL3050，编译期没有信号。
要真上 Native AOT 得给 `JsonSerializerOptions.TypeInfoResolver` 挂源生成的
`JsonSerializerContext`。当前 Blazor WASM（裁剪但未关反射）不受影响。

```csharp
// ✅ 接口即契约，路径/方法/参数都在签名里
public interface IAdminApi
{
    [Get("/api/admin/users/{id}")]
    Task<UserDto> GetUserAsync(Guid id);

    [Post("/api/admin/users")]
    Task<UserDto> CreateUserAsync([Body] CreateUserRequest request);
}
```

- **注册一律用 `AddAppKitRefitClient<T>(baseAddress)`（`Kimi.AppKit.Http` 包）**——
  它内部才调 Refit 官方的 `AddRefitGeneratedClient<T>()`，并统一了 JSON 约定、
  `ExceptionFactory`、`CollectionFormat`。
  - 禁止用 `AddRefitClient<T>()`：走反射，在 trimmed / Native AOT 的 WASM 下会
    **静默退化或直接抛异常**，只有跑在裁剪后的发布产物上才暴露，本地 `dotnet run` 看不出来。
  - 禁止直接用 Refit 官方的 `AddRefitGeneratedClient<T>()`：请求构建是对的，但拿的是
    Refit 默认 settings——错误提示退化成一句状态码、数组查询参数被拼成逗号分隔后
    被服务端静默忽略。**这两条都不报错**，所以 grep 得到「谁没走统一封装」才是防线。
- **归属边界**：Refit 只进模板的 Shared 层与 Client 包，`Kimi.AppKit.Core` 保持零依赖不引入。
- **文件上传走 `IMultipartApi<T>`，不是 Refit 接口**——multipart 场景 Refit 语法表达不了，
  这条契约单独保留（详见 `Kimi.AppKit.Core.Contracts.IMultipartApi`）。
- **错误处理经 `RefitSettings.ExceptionFactory`**，不要在调用方各自 `try/catch` 判状态码——
  服务端回的 `ProblemDetails` 要在这一层统一转成可读异常，漏配的后果是错误提示从
  「这条记录已被他人修改」退化成一句没有信息量的「500 Internal Server Error」。

---

## 五、验证清单（每个新端点都要走一遍）

- [ ] 匿名调用 → 401（不是 302，不是一页 HTML）
- [ ] 已登录但角色不足 → 403
- [ ] 不存在的 id → 404 + `application/problem+json`
- [ ] 非法入参 → 400 + `application/problem+json`
- [ ] 响应里**没有**堆栈、绝对路径、内部类型名
- [ ] `/scalar/v1` 上能看到它，且能用页面上的 Authorize 按钮真的调通一次

⚠️ 最后一条不能省。「构建通过 + 单测绿」证明不了端点真的可调用——
本项目已经因为「跑起来看着对」漏掉过五个只有实际操作才会暴露的缺陷。
