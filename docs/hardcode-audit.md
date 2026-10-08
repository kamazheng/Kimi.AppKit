# 硬编码审计：模板通用化清单

> 审计日期 2026-09-02（P0 阶段）。范围：前身样例工程（`samples/AppTemplate`，S1 起已从仓库移出）全部四个工程 + CI/部署脚本。
> 这是把模板从「某成都工厂内部工具」改造成「任何客户可用的商业产品模板」的待办总账。

> **状态（2026-10，公开前整理）**：本文是历史审计总账，保留作设计依据。条目所指的前身样例工程已不在仓库内；
> 公司名、内部主机名已替换为 `<…>` 占位。已修的部分由 AppKit 包承接——邮件地址全配置化（`KEmailOptions.FromAddress`）、
> 遥测命名空间与过滤配置化（`KObservabilityOptions`）、`XmlDocLookup` 不再默认他项目文件名；
> 其余条目属前身工程的具体实现，新模板（`samples/AppTemplate.Next`）按 AppKit 包的配置化约定另行实现，不逐条对账。

**核心判据**：任何「换个客户就是错的」值，都不该出现在代码里。
**安全判据**：配置项的**默认值必须在客户环境下是安全的**——信任网段的默认值是空，不是放行。

---

## A. 必须配置化（换客户就是错的）

### A1 网络与地址

| # | 位置 | 当前值 | 建议配置键 / 安全默认值 |
|---|---|---|---|
| A1-1 | `Kimi.AppKit.Sample/Program.cs:168` | `KnownIPNetworks = { IPNetwork("192.0.2.0", 23) }` | `Network:TrustedProxyCidrs`（string[]），默认 **空数组**。成都工厂反代网段写死在代码里，且代码注释自己就承认 /23 过宽 |
| A1-2 | `Kimi.AppKit.Sample/appsettings.json:21` | `NacosConfig:ServerAddresses = ["http://<配置中心主机>/"]` | P3 整体删除 Nacos，换 `IKConfigSource` |
| A1-3 | `appsettings.{Development,Staging,Production}.json:9` | 三套真实 内部配置中心命名空间 GUID | 同上；顺带是内部信息泄露 |
| A1-4 | `Client/wwwroot/appsettings.*.json:3-8` | `FileService:Url` = `<内部文件服务主机>`、`Viewer:Url` = `<内部看图主机>` | 同名键，默认 `https://fileservice.example.com` / `https://viewer.example.com/...`，**启动期校验**。WASM 端配置走不了配置中心，只能靠这里 |
| A1-5 | `Infrastructure/ConfigureServices.cs:228` | `internalDomains = { "<内部认证主机-生产>", "<内部认证主机-测试>" }` | `OpenIDConnect:TrustedBackchannelHosts`，默认 **空**。⚠️ 见安全隐患 #3——这个机制本身要重做 |
| A1-6 | `Infrastructure/OpenTelemetryHelper.cs:33` | `serviceNameSpace = $"<公司缩写>-{env}"` | `Telemetry:ServiceNamespace`，默认取产品名 |
| A1-7 | `Infrastructure/OpenTelemetryHelper.cs:124` | `Host.Contains("<配置中心主机>")` 用于排除轮询噪音 | `Telemetry:ExcludedHttpHosts`，应与配置中心地址联动而非二次硬编码 |
| A1-8 | `.gitlab-ci.yml:1-3` | `include: project: '<源公司内部 GitLab 组>/CDU_CICD'` | **本审计里唯一阻断式的一条**：客户 CI 在解析阶段就失败。改为仓库自带完整 jobs |
| A1-9 | `.gitlab-ci.yml:7` | 注释示例用 `<内部应用主机>` | 换成 `app.<customer>.example.com` |

### A2 组织与品牌

| # | 位置 | 当前值 |
|---|---|---|
| A2-1 | `Client/Layout/MainLayout.razor:70` | `<MudText><公司> <工厂></MudText>`——同文件里已有 `LocalEnvironmentData.AppName` 可用却没用 |
| A2-2 | `Client/Pages/HttpLogin.razor:16`、`QrPrint.razor:15`、`HomePages/Home.razor:39-40,73` | `<公司中文法定名>` / `<公司英文法定名>`。⚠️ **`Home.razor:4` 的注释声称「换项目只需改配置，不必动这个文件」——这句话是假的**，典型的静默陷阱 |
| A2-3 | `QrPrint.razor:14,58`、`HttpLogin.razor:15,63`、`Infrastructure/QrLoginSupport.cs:78`、`MainLayout.razor:66` | `CDU_Logo.png` / `<公司>_Logo.png` + alt 文本，**4 处独立硬编码**，其中一处是服务端字符串拼 HTML |
| A2-4 | `Shared/Constants/AppConstant.cs:23` | `EmailFrom = "<公司前缀>-<系统>@<公司域名>"`（编译期常量） |
| A2-5 | `Services/System/EmailService.cs:232-236` | `$"<公司前缀>-{appShortName}@<公司域名>"`——只有中间段可配 |
| A2-6 | `Services/System/<公司>ApproveCenter.cs` + `Program.cs:91-93` | 类名、方法名 `Use<公司>ApprovalCenter`、配置段 `<公司>ApproveCenter:*` 全带公司名，且**无条件装配** |
| A2-7 | `.vscode/settings.json:2` | `"lrm.resourcePath": "/Users/kzheng/Developer/work/..."` 个人绝对路径进了版本控制 |
| A2-8 | `.claude/settings.json` | 同上，`additionalDirectories` 里是个人机器路径 |

品牌统一走 `Branding:*`：`CompanyDisplayName` / `CompanyLegalNameZh` / `CompanyLegalNameEn` / `LogoUrl` / `LogoAltText`，默认空则不渲染。
邮件统一走 `Email:FromAddress`，默认 `noreply@example.com`。

### A3 时间与地域 ⚠️ 最系统性的一处

**同一套 UTC+8 假设有 6 份独立实现，互不复用，漏改任何一处都是静默错误：**

| # | 位置 | 写法 |
|---|---|---|
| A3-1 | `Services/System/ChengduTimeProvider.cs:12` | `FindSystemTimeZoneById("Asia/Shanghai")`，**生产环境唯一的 `TimeProvider`** |
| A3-2 | `Shared/Infrastructure/SimulatedTimeProvider.cs:13` | 同样写死 `"Asia/Shanghai"` |
| A3-3 | `Tasks/HangfireTasks.cs:15,20` | `"China Standard Time"`——**Windows 时区 ID**，与上面的 IANA 写法不一致，Linux 容器上会直接抛 |
| A3-4 | `Shared/Constants/AppConstant.cs:31-35` | `utcDateTime.AddHours(8)`（当前无调用点，死代码但风险仍在） |
| A3-5 | `Client/Helpers/CstHelper.cs:10` | `TimeSpan.FromHours(8)`，**每一个展示给用户的时间戳**都经过它 |
| A3-6 | `Shared/Extensions/CstExtensions.cs:9` | `TimeSpan.FromHours(8)`，不只用于展示——`ToCstOffset` 还用于**日期选择器写库前的换算**，非中国客户存进去的 UTC 时刻是错的；`ParameterSheetDtoValidator.cs:95` 还拿它做「不能是未来」校验，午夜附近误判 |

⚠️ 这不是疏漏：`Documents/ADR/ADR-001` 白纸黑字写着「工厂固定在成都（CST, UTC+8）」。**是架构级既定假设，要一次性替换。**

**方案**：`Kimi.AppKit.Core` 提供单一 `IAppTimeZone` 服务，读 `Localization:DefaultTimeZone`（IANA 字符串），默认 `"UTC"`。上述 6 处全部改为向它取值，删掉所有 `+8` / `Asia/Shanghai` / `China Standard Time` 字面量。Hangfire 用 `Hangfire:JobTimeZone`，默认 `UTC`。

**语言同理**：

| # | 位置 | 问题 |
|---|---|---|
| A3-7 | `Program.cs:203-208` | `SupportedCultures = [en, zh-CN]` 写死两种，加第三种要重新编译 |
| A3-8 | `Shared/Extensions/StringExtensions.cs:32-42` | 「通用」双语助手 `Bg()` 实际写死 `culture == "zh-CN" ? cn : en`，其他 culture 静默落英文 |
| A3-9 | `Client/Layout/MainLayout.razor:39-40,194` | 语言菜单硬编码两项，切换提示文案也没走资源文件 |

→ `Localization:SupportedCultures`（string[]），默认 `["en"]`。

### A4 认证与授权

| # | 位置 | 问题 |
|---|---|---|
| A4-1 | `Infrastructure/ConfigureServices.cs:91` **与** `Infrastructure/LoginLogoutEndpointRouteBuilderExtensions.cs:33` | `const string <前缀>_OIDC_SCHEME = "<公司>CduOpenId"` **在两个文件里各定义一份**。除了带品牌名，更要命的是改一处漏一处会让 `AddOpenIdConnect` / `SignOutAsync` **静默认证失败**。收成单一常量或 `Auth:OidcSchemeName` |
| A4-2 | `Program.cs:92-93` | `<公司>ApproveCenter:*` 配置段名（同 A2-6） |

### A5 外部系统集成

| # | 位置 | 问题 |
|---|---|---|
| A5-1 | `Shared/DTOs/EmployeeInfoDto.cs:12-110` | 完全按源公司 SAP/HR 字段建模（`L1manager`..`L4manager`、`Idnumber`、`DirectLabor`），换 HR 系统要重写 DTO 而不是改配置。应抽 `IHrProvider` |
| A5-2 | `Program.cs:104-111` | HR 专用 HttpClient 的 `ServerCertificateCustomValidationCallback` **无条件 true**。见安全隐患 #4 |

---

## B. 应该配置化（合理默认值 + 可覆盖）

### B1 磁盘路径

`Shared/Constants/AppConstant.cs:38-76`（`FilePhsycalPath`）按 OS 分支写死 `D:\FileService\Files\...` / `/Users/Shared/Files/...` / `/var/www/Files/...`。
→ `FileStorage:PhysicalRootPath`，默认 `Path.Combine(ContentRootPath, "App_Data", "Files")`，删掉 OS 分支。

### B2 阈值与魔法数字

| 位置 | 当前值 | 建议 |
|---|---|---|
| `Program.cs:45-48` | SignalR 超时 60/30/15s、消息上限 10MB | `SignalR:*`，沿用现值作默认 |
| `Client/.../FileServiceUploadButton.cs:49`(120MB) / `...Single.cs:29`(**20MB**) / `...Multiple.cs:39`(120MB) | **三处不一致** | 统一 `Uploads:MaxFileSizeBytes` |
| `Shared/Services/System/FileService.cs:12`(50MB) vs `:27`(120MB) | 接口默认参数 ≠ 实现默认参数 | ⚠️ **这是真 bug**：C# 默认参数按调用点静态类型解析，走接口调用实际拿到 50MB。消除默认参数分歧 |
| `Controllers/V1/FilesController.cs` | 无 `[RequestSizeLimit]`，Kestrel 默认约 28.6MB | ⚠️ **客户端校验通过、服务端仍拒收**。要与 `Uploads:MaxFileSizeBytes` 对齐 |
| `GetDbRecordsRequest.cs:36` | `PageSize = 1000` 默认且**服务端无上限** | `GeneralDbQuery:DefaultPageSize=100` / `MaxPageSize=500`。当前状态是可被客户端拉爆的 DoS 面 |
| `DynamicLinqService.cs:91`(1000) vs `:153`(50)，`.Helper.cs:386` 无上限 | 同一服务内两处默认值不一致 | 同上 |
| `TablesSelectPage.razor:93` | `PageSizeOptions {20,50,100}` | `GeneralDbQuery:PageSizeOptions` |
| `Infrastructure/ConfigureServices.cs:99` | `MemoryCache.SizeLimit = 10000`（按 某工厂车间规模估） | `Cache:MemoryCacheSizeLimit` |
| `Client/Extensions/FileMetaCache.cs:24` | `MaxEntries = 512` | `Cache:FileMetaMaxEntries` |
| `Infrastructure/MmsRetryHelper.cs:13` | `maxAttempts=3, baseDelayMs=200` 手写重试 | `Retry:*`；或换 Polly |
| `Tasks/HangfireTasks.cs:14-20` | Cron 固定 1 点 | 可配置 + 配合 A3 的时区统一 |
| 全局 | **没有任何限流中间件** | 对外销售产品的明显缺口，建议新增 `RateLimiting:*` |

### B3 出厂默认值

- `appsettings.json:8-11` `Database:Provider = "SqlServer"` → 建议出厂默认改 `"Npgsql"`（免授权费、跨平台；双 provider 支持已由 `DatabaseProviderSetup` 收口，切换成本低）
- `scripts/DbSnapshot.cs` 100% 基于 T-SQL 系统视图与 `DBCC CHECKIDENT`，且硬编码 `dbo`（PG 是 `public`）→ 客户若选 PG，该运维工具**静默无操作**（打印「没有用户表」而非报错）。要么按 provider 门禁并注明，要么补 PG 实现
- `scripts/DbSnapshot.cs:331` `const string targetSchema = "Data"` 同理

---

## C. 可以保留

- Migrations 里的 schema 名 `Reference` / `Data`（走 `DbSchema` 常量，通用英文）
- `[MaxLength]` / `[StringLength]` 的各种长度（50/100/256/500/1000/2000，均为通用值，未发现绑定源公司工号格式的假设）
- `DefaultContext.SeedData.cs` 为空实现，无硬编码种子数据
- `DesignTimeDbContextFactory.cs:26` 的 `Host=design-time-only;...;Password=none`——刻意指向不存在实例的防呆值，不是真实凭据
- `AppRoles.cs` 的角色前缀已基于可配置的 `AppConstant.AppShortName` 生成
- OIDC claim 短名 `"role"` / `"name"`——标准约定
- `generate-dockerfile.sh` 只引用公共 `mcr.microsoft.com/dotnet/*`（但第 3 行中文注释编码损坏，顺手修）

---

## 安全隐患（优先级最高，对外销售前必须处理）

**1. CORS 完全开放且无开关**
`Infrastructure/ConfigureServices.cs:117-125` 的 `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()`，`Program.cs:211` 无条件应用。
→ `Cors:AllowedOrigins`，**未配置时默认拒绝跨域**。

**2. 密码登录网段闸门「未配置 = 放行」**
`Infrastructure/PasswordLoginNetworkGate.cs:32-36`：`if (cidrs.Count == 0) return null; /* 放行 */`。
`Auth:FloorSubnets` 未配置时任意 IP 都能走 HTTP 明文密码登录。代码注释自己承认是「安全欠账」。
→ 反转默认：未配置时**拒绝**，并在启动日志里明确说明该功能因未配置而关闭。

**3. 对硬编码域名无条件禁用证书校验**
`Infrastructure/ConfigureServices.cs:18-34, 228`，`CustomHttpClientHandler` 对列表内域名 `ServerCertificateCustomValidationCallback => true`。
当前因为域名是 源公司专属而「暂时无害」，但**「往列表里加个域名就能全局关掉证书校验」这个机制本身**要重做，不能只是换成客户域名。

**4. HR 集成 HttpClient 无条件绕过证书校验**
`Program.cs:104-111`，无任何开关。
→ 默认 `false`，仅在显式配置 `EmployeeApi:BypassCertificateValidation=true` 时允许，且文档中标注为不安全选项。

**5. CI 引用私有 GitLab 组**
`<源公司内部 GitLab 组>/CDU_CICD` 暴露源公司内部组织架构。

**6. 运维文档示例用真实域名**
`scripts/migrate-prod.ps1:28` 与《数据库迁移操作手册》里的 `kinit <user>@<REALM>`。
→ 换 `youruser@CONTOSO.COM`。

---

## 补充教训：新写的包代码也会犯同一类错

P3a 写 `ConfigureAppKitJwtBearer` 时，把「无条件关闭证书校验」换成了「无条件用
`RequireHttpsMetadata=true` / `RoleClaimType="role"` / `ClockSkew=TimeSpan.Zero`」——
**同一类硬编码问题换了个位置**，只是从"不安全的硬编码"变成"安全但不可覆盖的硬编码"。

判据：一个字面量该不该开放成参数，看**"不同部署/环境下这个值是否真的需要不同"**：
- 需要（本地开发用 http、对接非 `role` 命名的 IdP、多副本时钟未对齐）→ 开放成带安全默认值的可选参数
- 不需要（Hangfire 的 `CompatibilityLevel`、序列化设置——这是框架推荐的兼容性基线，
  改错会破坏任务持久化格式）→ 保持硬编码，但仍给一个 `Action<T>` 逃生舱，
  不要因为"这是安全默认值"就完全不留扩展点

修复的三处：`JwtBearerSetup`（`requireHttpsMetadata`/`roleClaimType`/`clockSkew` 三个可选参数
+ `configureValidation` 逃生舱）、`HealthCheckSetup`（`checkName` 参数，默认 `"database"`）、
`CorsSetup`（`allowedMethods`/`allowedHeaders` 可选参数）、`HangfireSetup`（`configureAdditional` 逃生舱）。

## 处理排期

| 优先级 | 内容 | 落在哪个阶段 |
|---|---|---|
| **最高** | 安全隐患 1/2/3/4；A1-1 信任网段；A1-8 CI include | P3 |
| **高** | A3 时区统一（6 处→1 个 `IAppTimeZone`）；A3-7~9 语言可配 | **P1**（服务放 Core）+ P3（消费方改造） |
| **高** | A1-2~4 配置中心与外部服务地址；A4-1 OIDC scheme 常量收口 | P3 |
| **中** | A2 全部品牌项（含 A2-2 那处假注释）；A5-1 HR DTO 抽象 | P3 / P7 |
| **中** | B2 上传大小三处不一致 + 接口/实现默认参数分歧 + 服务端缺 `RequestSizeLimit` | P3 |
| **中** | B2 分页无上限（DoS 面） | P6 |
| **低** | A2-7/A2-8 个人绝对路径；B1 磁盘路径；B3 出厂默认值；编码损坏 | P7 |
