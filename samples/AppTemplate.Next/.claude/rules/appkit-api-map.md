---
paths:
  - "**/*.cs"
  - "**/*.razor"
---

# 开发范式速查表 —— 想做 X，必须用 Y

> **本表是强制的。** 表内列出的能力 `Kimi.AppKit` 已经实现，
> **禁止在本项目里自己写一套**。要改行为就改包，或在包里加可选参数——
> 不要在应用侧另起炉灶。
>
> 找不到对应项时，先按 `appkit-consumption.md` 的「正确的查法」再确认一遍，
> 确实没有再自己写，并在 `ADR/` 里记一条「为什么这个不进包」。

---

## 数据访问

| 想做 | 必须用 | 禁止 |
|---|---|---|
| 带审计与软删除的 DbContext | 继承 `AuditableDbContext` | 自己写 `SaveChanges` 拦截；自己维护 `Active` 字段 |
| 记录「谁改了什么」 | 基类自动完成，用**无参** `SaveChangesAsync()` | 手传用户名（那是审计表说谎的来源） |
| 删除实体 | `Remove()` | 手写 `Active = false`（绕过拦截，审计记成普通更新） |
| 当前用户 | 注入 `IKCurrentUser` | 直接读 `HttpContext.User` |
| 事务 | `UnitOfWork` | 裸调 `BeginTransactionAsync`（开了重试策略会抛） |
| provider 判定 | `DatabaseProviderSetup.Resolve()` / `ProviderDetection` | 自己比对 `ProviderName` 字符串 |
| provider 差异（引号、布尔字面量） | `IDbProviderDialect.Quote()` / `.BooleanLiteral()` | 在业务代码里 `if (isPg)` |
| schema 名 | `DbSchema.Reference` / `.Data` | 字面量 `"Reference"` |
| 实体映射到表 | `builder.ToSchemaTable(DbSchema.X)` | `ToTable("名字写死", "schema写死")` |
| 链式配置属性 | `builder.KProperty(x => x.Foo, dialect)` | 重复写 `builder.Property(...)` 前缀 |
| JSON 列 | `.HasJsonConversion()` | 自己写 `HasConversion` + `ValueComparer`（比较与快照两处都易错） |
| 并发令牌 / 枚举 CHECK / Name 唯一 / 软删除过滤 | `ApplyConcurrencyTokens` / `ApplyEnumStringConstraints` / `ApplyUniqueNameConstraint` / `ApplySoftDeleteFilter` | 自己实现任意一个 |
| `DateTimeOffset` 落库 | `ApplyAppKitConventions()`（基类已调） | 逐属性配转换器 |
| 设计时迁移工厂 | 继承 `DesignTimeDbContextFactoryBase<T>` | 自己实现 `IDesignTimeDbContextFactory` |
| 设置项读写 | `ISettingService` / `KSettingService<T>` | 自己查 Setting 表 |

## 契约与工具（Core）

| 想做 | 必须用 | 禁止 |
|---|---|---|
| 分页结果 | `KPage<T>` | 自写 `PagedResult` |
| 分页/排序/搜索入参 | `KQuery` | 自写查询参数类 |
| 操作结果（成功/失败+消息） | `KResult` | 自写 `OperationResult`、或用异常表达预期内失败 |
| 时区换算 | `IAppTimeZone` | 任何 `AddHours(8)` / 硬编码 `TimeSpan.FromHours(8)` |
| 字段/枚举的展示名 | `DisplayLabelExtensions.GetDisplayLabel()` | 自写 PascalCase 拆词 |
| XML 注释当界面提示 | `XmlDocLookup` | 自己读 XML 文档 |
| 实体属性反射 | `EntityPropertyInspector` | 自己遍历 `PropertyInfo` |
| 令牌响应 | `KTokenResponse` | 自定义同形状的类 |
| 文件上传契约 | `FileUploadPayload` / `IMultipartApi<T>` | 自写 multipart 契约 |
| 导入报告 | `KImportReport` / `KImportError` | 自写 |
| 本地化请求头名 | `KLocalizationHeaders.RequestLanguage` | 字面量 `"X-Request-Language"` |
| 实体基类 | `BaseAuditableEntity` / `AuditableEntityWithName` | 自己定义审计字段 |
| 跳过 Name 唯一约束 | 实现 `ISkipNameUnique` | 自己改约定 |

## Web 装配

| 想做 | 必须用 | 禁止 |
|---|---|---|
| 认证 | `AddAppKitAuthentication(config, o => ...)` | 自己拼 `AddAuthentication().AddCookie().AddOpenIdConnect()` |
| 默认拒绝授权 | `AddDefaultDenyAuthorization()` | 裸 `AddAuthorization()`；⚠️ 也不要重设它的 `FallbackPolicy` |
| 角色策略 | `AuthorizationSetup.RequireAnyRole(...)` | 自定义授权特性（只对 MVC 生效，minimal API 不走） |
| 令牌续期 | 包内 `ConfigureCookieOidcRefresh`（认证装配已挂） | 客户端自己刷新 |
| ROPC 换令牌 | `IKOidcTokenService` | 自己 `HttpClient` 打令牌端点 |
| 网络准入（按网段） | `AddAppKitNetworkGate` | 自己解析 `X-Forwarded-For` |
| 扫码登录 | `AddAppKitQrLogin` | 自己做二维码票据 |
| 回跳地址防开放重定向 | `KReturnUrl` | 自己校验 returnUrl |
| CRUD 端点 | `MapCrudEndpoints<T>()` | 手写 controller 做增删改查 |
| WASM 端取数 | `AddHttpCrudDataSource<T>()` | 自己 `HttpClient` 调 CRUD 端点 |
| 服务端预渲染占位数据源 | `AddPrerenderCrudDataSource<T>()` | 让预渲染真去取数 |
| 健康检查 | `AddAppHealthChecks<TContext>()` + `MapAppHealthChecks()` | 自写 `/health` |
| 全局异常 → ProblemDetails | `AddAppKitErrorHandling()` | 自写 `IExceptionHandler` |
| 业务异常（带状态码） | `BaseException` | 自定义同名/同形状异常 |
| 「详情该不该暴露」策略 | `IErrorDetailPolicy` | 在异常处理器里写死 |
| CORS | `AddAppCors(origins)` | `AllowAnyOrigin()` |
| Excel 导入导出 | `IExcelService` | 自己用 NPOI |
| 邮件 | `IKEmailSender` / `AddAppKitEmail` | `System.Net.Mail.SmtpClient`（微软已不推荐） |
| 后台任务 | `AddAppKitHangfire(...)` | 自己配 Hangfire 存储 |
| 请求本地化 | `UseAppKitRequestLocalization(default, supported)` | 自己拼 `RequestLocalizationOptions` |
| 可观测性 | `AddAppKitObservability()` | 自己配 OTel |
| 敏感数据脱敏 | `KSensitiveDataRedactor` | 自写正则 |

## UI 组件

⚠️ **动手写任何组件前先查这张表。** 曾有三个组件与包内重复
（`RoleGate`/`ReasonConfirmDialog`/`SearchableSelect`）。

| 想做 | 必须用 |
|---|---|
| 表格（反射列 + 分页 + 排序） | `KDataTable` / `KEntityTable` |
| 整页 CRUD（列表+增删改） | `KEntityCrudPage` |
| 实体表单 / 单字段 | `KEntityForm` / `KEntityField` |
| 可搜索下拉（从数据源取候选） | `KSearchSelect` |
| 搜索框 / 筛选栏 | `KSearchField` / `KFilterBar` |
| 角色门禁（无权限禁用+提示） | `KRoleGate` |
| 确认对话框 | `IKConfirm`（`MudConfirm` 实现） |
| 需填理由的确认 | `KReasonConfirmDialog` |
| 提示条 / 通知 | `IKNotify` |
| 无权限页 | `KAccessDeniedDialog` |
| 异常详情弹窗 | `KErrorDetailDialog` |
| 按钮（自动捕获异常并提示） | `ErrorCatchButton` / `ErrorCatchIconButton` / `ErrorCatchFab` |
| 卡片 / 指标卡 / 空状态 | `KCard` / `KMetricCard` / `KEmpty` |
| 状态徽章 / 枚举徽章 / 环境徽章 | `KStatusChip` / `KEnumChip` / `KEnvChip` |
| 标签值对 / 字段包装 | `KLabeledValue` / `KField` / `KMudField` |
| 品牌标识 / 头像 | `KBrand` / `KAvatar` |
| 代码块 | `KCodeBlock` |
| 错误面板 | `KErrorPanel` |
| 断线重连提示 | `KReconnectModal` |
| MudBlazor 三件套 Provider | `KMudProviders` |

## 主题与配色

| 想做 | 必须用 | 禁止 |
|---|---|---|
| 应用主题 | `AppKitTheme.Instance` | 在 `MainLayout` 的 `@code` 里写调色板 |
| 品牌色覆盖 | `AppKitTheme.Build(new BrandColors(...))` | 改包里的常量 |
| 配色令牌 | `BlueprintPalette` + `_content/Kimi.AppKit.Design/css/kmold-tokens.css` | 自己定义颜色变量 |

⚠️ **不要在 `app.css` 里设 `html, body { font-family }`**——
它加载在 `MudBlazor.min.css` 之后，会把设计系统字体整个覆盖，
而 `document.fonts` 仍显示 `loaded`，**零报错**。

---

## 例外流程

确实需要自己实现时（包里没有、或包的实现不适用），走这三步：

1. 在 `ADR/` 记一条决策：**为什么不用包里的 / 为什么不进包**。
2. 判断它是否该**进包**——通用性、是否核心差异化、复杂度与风险三条判据。
3. 若判定留在应用，在类型的 XML 注释里写明「刻意不进包，理由是……」，
   避免下一个人再纠结一次。
