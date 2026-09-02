# 原模板架构评审

> 2026-09-02（P0 阶段）。对 `samples/AppTemplate`（源自 `CDU_DotNetTemplate`，约 2.6 万行）做的**批判性**评审，
> 三个维度并行、独立进行：分层与依赖 / 数据层与并发 / 安全与运维。
>
> **立场**：这个模板不是最佳实践。它是十几年演进 + 单一内网工厂特化的产物。很多设计在
> 「只服务一个客户、只跑在内网、只有一个人维护」的前提下够用，一旦变成对外销售的通用 NuGet 包就是缺陷。
> **照搬 = 把技术债做成包发出去，而且从此改不动**——包一旦发布，消费方就锁在 API 上。
>
> 本文是**每个抽包阶段开工时的对照基线**（见主计划「仪式点一点五」）。

---

## 交叉印证的头号问题

**`GeneralDbController` 的读端点没有 `[Authorize]`** —— 三个评审里有两个各自独立揪出了这条，是整份评审里最确凿的一条。

- 位置：`Controllers/V1/GeneralDbController.cs:38-66`（`GetDbRecords` / `GetDbRecordsPaged` / `GetDbRecord`）
- 对照：同一文件的 `UpsertDbRecord` / `DelDbRecord` **有** `[Authorize] + [AppRoleRequired]`
- 后果：任何人凭 `TableTypeName` + `Filter` 字符串即可**匿名读任意表任意字段**。读写鉴权不对称。
- 根因不是「漏加了一个特性」，而是**这种「一个端点读任意表」的抽象本身消灭了挂授权的自然位置**——写接口记得加、读接口就忘了。

配套的第二层根因：`ConfigureServices.cs:268` 裸调 `AddAuthorization()` 未设 `FallbackPolicy`，`MapControllers()` 也没 `RequireAuthorization()`。于是**整个应用是「默认放行」**。实测已经泄了不止一处：`AdminController` 的 `GetAllSettings`/`GetEmailTemplate`/`GetDefaultSetting`、`V1/SystemController` 的 8 个接口、`V2/SystemController` 全部。

→ **抽包时必须把「忘记标注 = 裸奔」翻转成「忘记标注 = 拒绝」。**

---

## 🔴 必须重新设计

### 认证与授权

| # | 问题 | 位置 | 后果 |
|---|---|---|---|
| R1 | **JWKS 解析器同步阻塞 + 无条件关闭证书校验** | `ConfigureServices.cs:173-185` | 每次验 JWT 都 `new HttpClientHandler{ DangerousAcceptAnyServerCertificateValidator }` + `.GetAwaiter().GetResult()` 拉 JWKS，无缓存。**这条通道可被 MITM 篡改返回的公钥集 → 攻击者可伪造任意签名并被接受**。这不是性能问题，是认证根基被击穿。另外同步阻塞异步 IO 会打满线程池 |
| R2 | **角色/权限回退查询同样无条件关闭证书校验** | `AppRoleRequiredAttribute.cs:107-116`、`PermissionRequiredAttribute.cs:80-83` | 比 R1 更糟：**没有域名限制**，对任意 Authority 成立。这条链路直接返回 true/false 决定放行，MITM 把 `false` 改成 `true` 即等价于伪造授权结果。且每次请求 `new HttpClient`，有 socket 耗尽风险 |
| R3 | **三套自定义授权 Attribute 绕开原生策略体系，且默认放行** | `AppRoleRequiredAttribute` / `PermissionRequiredAttribute` / `ClaimAuthorizeAttribute` 都自实现 `IAuthorizationFilter`、不继承 `AuthorizeAttribute` | 见上文「交叉印证」。三个 Attribute 还各自复制了一份 `bypassState.IsActive` 判断，逻辑漂移风险高 |
| R4 | **二维码登录：纸质凭据无过期、无单张吊销** | `QrLoginSupport.cs:27-28` 用的是 `CreateProtector` 而非 `ITimeLimitedDataProtector` | 密钥环里对应密钥还在（默认 30 天轮换但历史密钥保留用于解密）就能一直解出**明文账号密码**。员工离职 / 工牌丢失 / 被拍照 → 这张纸等价于永久有效的万能登录卡，**且系统没有任何方式单独失效它**（唯一手段是吊销整个密钥环，会连带打掉所有会话）。叠加 R11（密钥明文落本地磁盘），主机被攻破即可批量解密所有在外流通的二维码 |
| R5 | **ROPC 密码流无节流，且密码明文经 HttpLogging 落盘** | `/password-login`、`/qr-login`、`/qr-print` + `ConfigureServices.cs:105-109` | 无限流的密码验证 oracle。更要命的是 `SensitiveDataRedactor` **只挂在 OTel 的 LogRecord 处理器上，没有接入 HttpLogging**；而这三个端点的表单体是 `username=x&password=y`（非 JSON），也不匹配脱敏器的 `"key":"value"` 正则 → **默认配置下明文密码被原样写进日志** |
| R6 | **`ExposeDetailedErrorsSetting` 默认 `true`** | `GlobalExceptionHandler.cs`、`SettingDefaults.cs:55` | 完整异常链 + 堆栈原样返回给客户端，且**未经认证即可触达**（任意能触发 500 的匿名接口）。内部类型名、文件路径、EF 异常里的 SQL 片段全泄。且「设置项没读到就沿用默认 true」是 fail-open |

### 数据层

| # | 问题 | 位置 | 后果 |
|---|---|---|---|
| R7 | **原始 SQL 拼接 + 黑名单过滤，可绕过的注入面** | `Kimi.EFExtensions/DynamicLinqs/DynamicQuery.cs:144-202` | 只检查 `whereClause` 是否含 `"DROP "` / `"DELETE "` / `"TRUNCATE "` 三个词。大小写变体、`UNION SELECT`、子查询、无空格写法全部绕过；`orderBy` 更是直接拼进 SQL 无任何过滤 |
| R8 | **Dynamic LINQ 字符串解析未做访问限制** | `DynamicLinqService.cs:112-114/167-168` | `query.Where(whereClause)` 直接吃外部输入。System.Linq.Dynamic.Core 历史上有通过字符串构造任意类型 / 反射调用的 CVE，项目没有配 `ParsingConfig.RestrictAccessToMembers` 之类限制 |
| R9 | **审计两阶段保存非原子，静默丢审计** | `Kimi.EFExtensions/AuditTrailDbContext.cs:56-104` | 实体有临时主键（自增）时会触发**第二次独立** `SaveChangesAsync` 落 `Trail`。调用方不包事务（框架约定的普通调用方式就是不包）时，业务数据落库成功而审计行插入失败 → **静默的审计缺口**。且每次带自增键的保存都多一次数据库往返 |
| R10 | **并发令牌被业务代码绕过，乐观并发形同虚设** | `GenericUpsertService.cs:60` | `ConcurrencyControl` 给每个实体配了影子 `RowVersion`/`xmin`，但 DTO 不携带影子属性，`CurrentValues.SetValues(inputEntity)` 只覆盖同名 CLR 属性 → **典型 lost update**：两个用户先后编辑，后者永远无感覆盖前者 |
| R11 | **客户端传入的非零 offset 直接落库，PG 下会炸** | `DynamicLinqService.Helper.Convert.cs:273-274` | `ModelBuilderExtensions.ConfigureConventions` **没有全局 UTC 值转换器**。SQL Server 能跑，接 PG 后首次收到 `+08:00` 就抛 `only offset 0 is supported`，**且要等生产第一次收到非 UTC 输入才暴露** |
| R12 | **单一 Migrations 目录，切 provider 靠删除重建** | `Migrations/` 只有一套 | 对**已上线客户**不可行——迁移历史被删除即无法增量升级。KMold Auth/Files 的两套独立迁移程序集才是对的 |

### 分层与抽象

| # | 问题 | 位置 | 后果 |
|---|---|---|---|
| R13 | **`AppServicesHelper.Services` 全仓从未被赋值，功能已静默失效** | `Infrastructure/AppServicesHelper.cs`，调用点 `V1/SystemController.cs:73-74` | grep `AppServicesHelper.Services\s*=` **零命中**。也就是说 `GetUserInfo()` 在模板自身里就**永远返回 `"UserId:Anonymous;Email:"`**，编译通过、运行不报错、值是错的。静态服务定位器最典型的代价。而该 Controller 本来就有构造函数注入，`User.FindFirst(...)` 直接能用——这层纯属多余 |
| R14 | **`CommonServices`/`BaseService` 是零使用的上帝对象** | `Services/System/BaseService.cs` | 用 `required` 成员把 12 个依赖打包。grep `: BaseService` **零命中**——被注册了却从未被继承的死抽象，也无任何测试。一旦真用起来，每个子类的单测都要 mock 12 个依赖才能实例化一个只用其中两三个的 Service |
| R15 | **`IStandardApi` 契约自相矛盾，为从未被替换过的抽象付出 20 个样板类** | `Shared/Interfaces/IStandardApi.cs` + `Shared/APIs/**` 约 20 个类 | ① `TOutbound? Response { get; set; }` 是**可变**字段，同一实例既是入参又是被回填的结果槽 → 不可安全复用、不适合并发；② 只有一个调用方（`HttpClientService`），测试里也没有任何替代实现 → 不是真抽象；③ **契约自己已有反例**：`UploadFileApi` 根本不走这套 JSON 路径（`FileService.UploadFile` 手工拼 `MultipartFormDataContent`），而这个例外没在类型层面表达出来 |

---

## 🟡 应该改进

| # | 问题 | 位置 | 要点 |
|---|---|---|---|
| Y1 | **`ValidateAudience = false` + 用 id_token 当 Bearer** | `ConfigureServices.cs:166`、`IdTokenMessageHandler.cs` | 同一 IdP 上**任意其它客户端**签发的令牌都会被接受 → confused deputy。KMold 的架构正是「Auth 作为共享 IdP 服务多个应用」，这是真实攻击面。且用 id_token（身份断言）当访问令牌本身是语义误用，没有 scope/aud 保证 |
| Y2 | **`ConfigureServices.cs` 431 行 God Method + 两次 `BuildServiceProvider()`** | `:377` 与 `:400` | 从尚未 `Build()` 的 builder 提前建容器是公认反模式（ASP0000）：这个临时容器与应用根容器是**两个不同的 DI 图**，其中解析出的 Singleton 是多造的一份且从不 Dispose。同时这个 God Method 不给消费者任何「只要 A 不要 B」的组合点 |
| Y3 | **`AuthorizationBypassState` 默认全开** | `AuthorizationBypassState.cs:13,22` | 生产硬阻断做对了，但「非生产默认全开权限」放到不受控客户环境就是风险：`ASPNETCORE_ENVIRONMENT` 被设成 `Staging`/`UAT`/没设对 → 任何已登录用户自动拿到全部角色，**且默认值生效时是沉默的**（只有主动调 `SetAuthBypass` 才写审计） |
| Y4 | **`CookieOidcRefresher` 无并发保护** | `CookieOidcRefresher.cs:46-123` | 临近过期 5 分钟窗口内并发请求各自发起 refresh。IdP 若做 refresh_token 一次性轮换（现代 IdP 默认行为，含 OpenIddict），第二个并发请求拿到已失效的旧 token 被拒 → `RejectPrincipal()` → **活跃用户被无故登出**，表现为「随机掉线」，极难定位 |
| Y5 | **事务/重试两处实现不一致** | `EfCoreTransactionRunner.cs:163-178` 对 vs `BatchExecutionRunner.cs:42` 裸事务 | 后者注释坦言「项目未开 `EnableRetryOnFailure` 故安全」。一旦为生产可靠性开启重试，`BatchExecutionRunner` 直接抛 `InvalidOperationException` |
| Y6 | **查询无硬上限、Include 路径无限制** | `DynamicLinqService.Helper.cs:384-389`、`:219-230` | 只在 `topQty<=0` 时兜底 1000，客户端传 `topQty=10000000` 直接绕过；`ApplyIncludesByReflection` 对导航路径数量/深度无限制，可拼出巨量 join |
| Y7 | **Server → Client 反向引用的代价藏在注释里** | `Kimi.AppKit.Sample.csproj` 的 ProjectReference | Server 因此传递依赖 MudBlazor、Blazored.*、FormCraft、CkEditor 一整套浏览器专属包。而「这个类型是不是客户端专属」只能靠读 Program.cs 的中文注释判断，类型系统不体现 |
| Y8 | **`Shared` 里塞了平台专属类型** | `UploadFileApi.cs` 的 `UploadFileRequest`（`IBrowserFile` + `IFormFile` 同一个类） | 导致 `Shared.csproj` 不得不引 `Components.WebAssembly` 与 `AspNetCore.Http`。把 UI 框架类型焊进了本该平台无关的传输契约 |
| Y9 | **仪式性接口** | `IAdminService` / `IGeneralDbQueryService` | 终生单实现、测试里零替身。对照组：`IUserService`/`IEmployeeService` 在 Server/Client 各有真实现，是**有理由的真抽象**——两者形成很好的对照 |
| Y10 | **审计追责链路吞异常** | `EfCoreTransactionRunner.cs:172-173` | `try { byUser = await userService.Name; } catch { byUser = null; }` → 退化为 "System"。审计的价值就在「谁干的」，这里把异常伪装成正常操作 |
| Y11 | **DataProtection 密钥仅本地磁盘、明文存储** | `ConfigureServices.cs:418-425` | 容器重建即所有 Cookie/防伪令牌/QR 密文失效；多副本各自生成密钥环 → 会话随机失效。密钥未加密落盘（叠加 R4） |
| Y12 | **全局 `@inject` 7-9 个服务到每个组件** | `Client/_Imports.razor` 等三处 | 读组件代码看不出它真实依赖什么；bUnit 测试要无差别为所有全局注入项提供假实现 |

---

## 🟢 可以照搬（明确记下来，免得后人重新怀疑一遍）

| 项 | 位置 | 理由 |
|---|---|---|
| **`DatabaseProviderSetup`** | `Infrastructure/DatabaseProviderSetup.cs`（约 35 行） | 职责单一，把「用哪个 provider」收口成运行时/设计时共用的唯一真相源，注释还说明了为什么要抽（避免两处 `UseSqlServer`/`UseNpgsql` 漂移）。这正是 KMold `IDbProviderDialect` 想要的收口思路的雏形 |
| **按 provider 分支的模型配置** | `DefaultContext.BasicSetting.cs` 的 `SetNameFieldUnique` / `SetEnumConstrain` | 已对 PG/SqlServer/MySQL/SQLite 四态分别处理标识符加引号、NULL 语义、CHECK 约束——覆盖了 KMold 已知三处分歧里的两个，**比预期扎实** |
| **影子并发令牌的类型设计** | `ConcurrencyControl`（`DefaultContext.BasicSetting.cs:137-181`） | 对四种 provider 分别配 `RowVersion`/`xmin` 的机制是对的。问题只在业务代码（R10）绕过了它，**机制本身不用改** |
| **健康检查 live/ready 分离** | `Infrastructure/HealthCheckSetup.cs` | `/health/live` 用 `Predicate=_=>false` 避免探针自身触发级联重启风暴；`/health/ready` 才跑真实依赖；两者匿名（探针无凭据是合理的）；**刻意不输出异常详情**避免连接串经匿名端点泄露。经过深思熟虑的取舍 |
| **Hangfire Dashboard 授权** | `HangFireMiddleWare.cs` | `HangfireAuthorizationFilter(AppRoles.Admin)`。很多模板照抄官方示例挂成「开发环境放行」，这里角色限定是正确实践 |
| **`CookieOidcRefresher` 的生命周期** | 注册为 Singleton，只依赖 `IOptionsMonitor<OpenIdConnectOptions>` | Singleton-safe，不捕获任何 Scoped 服务，无 captive dependency。是官方样例的正确移植（并发问题见 Y4，与生命周期无关） |

---

## 测试覆盖的旁证

593 行测试**全部只覆盖工具类**（JSON 转换、命名构建器、Scheme URL 解析、登录路由）。
没有任何测试触达 R13/R14/R15 提到的 DI 组合根、`GeneralDb` 的授权边界、`CommonServices`/`BaseService`。

这与「这些设计从未被验证过是否真的可用」的判断互为佐证——**死抽象之所以能长期存活，正是因为没有测试逼它面对现实。**

---

## 对各阶段的映射

| 阶段 | 必须处理的条目 |
|---|---|
| **P1 Core** | R14（不要带 `CommonServices`/`BaseService` 进包）、R15（`IStandardApi` 重新设计或换 Refit）、R13（删 `AppServicesHelper`）、Y8（`Shared` 去平台类型）、Y9（按「是否真有第二个实现」决定要不要接口） |
| **P2 Data** | R9（审计两阶段并入同一事务）、R10（Upsert 必须显式携带并比对版本号）、R11（全局 UTC 值转换器作为强制约定）、R12（两套独立迁移程序集）、Y5（只暴露一种事务入口，内部强制走 ExecutionStrategy）、Y10（审计取用户名不许吞异常）。🟢 照搬 `DatabaseProviderSetup` + 按 provider 分支的模型配置 + `ConcurrencyControl` |
| **P3 Web** | R1、R2、R3（默认拒绝 + 原生 policy）、R5、R6、Y1、Y2（拆 God Method、消灭 `BuildServiceProvider`）、Y3、Y4、Y11。🟢 照搬健康检查与 Hangfire Dashboard 授权 |
| **P5/P6 Components/Crud** | 头号问题（通用 CRUD 的授权模型：显式白名单 `IExposedEntityRegistry` + 授权烘焙进基类）、R7、R8（不做「表名 + 原始 SQL 拼接」这层）、Y6（硬上限）、Y7、Y12 |
| **待定夺** | **R4 二维码登录该不该进通用包。** 评审意见是默认移除——「把明文密码用可逆加密印在纸上」本质是把安全边界外包给「纸不会丢」这个假设，在不受控的客户环境里不成立。若客户确有需求，应改为短时 `ITimeLimitedDataProtector` + 服务端 `jti` 吊销名单 + 签发/核销审计 |
