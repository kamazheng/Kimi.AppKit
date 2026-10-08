# Kimi.AppKit

.NET 10 企业应用基座：一套 NuGet 包 + 一个项目模板，把 Blazor 业务系统里反复重写的东西一次做对。

> 1.0 之前（0.x）API 可能有破坏性变更；**0.x 期间请钉精确版本**（`Version="0.1.0"` 在 NuGet 里表示「最小版本 0.1.0」，会解析到更高的可用版本；要硬钉请写 `Version="[0.1.0]"`；不要用浮动版本 `0.*`）。
> 公开 API 面由 `PublicAPI.Shipped.txt` 快照守护；变更记录见 [CHANGELOG.md](CHANGELOG.md)，发布说明见 GitHub Release。

## 包

| 包 | 内容 |
|---|---|
| `Kimi.AppKit.Core` | 零依赖基座：抽象接口、契约 POCO、反射与字符串工具、单号生成器、四层设置契约 |
| `Kimi.AppKit.Data` | EF Core 10：审计轨迹、软删除、双 provider（PostgreSQL / SQL Server）方言收口、UTC 时间归一、动态查询引擎 |
| `Kimi.AppKit.Web` | ASP.NET Core 10：全局异常处理、健康检查、认证装配、OpenAPI、Excel 导入导出、后台任务 |
| `Kimi.AppKit.Design` | 设计系统：蓝图配色令牌、`MudTheme` 预设、`.k-*` 样式、自托管字体 |
| `Kimi.AppKit.Components` | 标准化 Blazor 组件库，**渲染模式无关** |
| `Kimi.AppKit.Crud` | 反射驱动通用 CRUD：登记一个实体即得列表、增删改与 Excel 导入导出 |
| `Kimi.AppKit.Http` | Refit 客户端注册的标准封装：源生成、零反射，统一 JSON 约定与错误处理 |
| `Kimi.AppKit.Observability` | OpenTelemetry 装配（独立包，带 10 个传递依赖，按需引用） |
| `Kimi.AppKit.Templates` | `dotnet new kimiapp` 项目模板 |

## 三条设计原则

**1. 渲染模式无关。** 组件在静态 SSR、InteractiveServer、WebAssembly 三种模式下都能用。
数据访问经 `ICrudDataSource<T>` 抽象——WASM 宿主走 HTTP，Blazor Server 宿主直连 `DbContext`。

通用 CRUD 的保存在 `EfCrudDataSource.UpsertAsync` 内先做 DataAnnotations 校验（`[Required]`、`[MaxLength]` 等）并翻译唯一约束冲突，
失败返回 `KResult.Fail`（端点转 400）；Excel 导入逐行走同一入口。审计属性（`IAuditableEntity`）不参与校验；
不要在导航属性或服务端填充字段上标 `[Required]`。

**2. 隐性知识随包走。** 那些「不这么写会静默出错」的约束写在 XML 注释里，消费方靠 IntelliSense 就能读到，
不必翻文档、也不必重新踩一遍。因此所有包强制 `GenerateDocumentationFile`。

**3. 包 ↔ commit 可验证。** 全部包启用 SourceLink 与 ReproducibleBuilds，版本号由 MinVer 从 git tag 推导。
发布只经 GitHub Release 触发，杜绝「nuget.org 上的包与仓库源码对不上」。

## 仓库结构

```
src/         九个包工程（8 库 + 模板）
samples/     AppTemplate.Next —— dotnet new 模板源，也是每个阶段的真实验收载体
tests/       打包契约与依赖方向的回归测试
docs/        路线图、各阶段复盘
```

## 开发

```bash
dotnet build Kimi.AppKit.slnx -c Release   # ⚠️ .NET 10 SDK 生成的是 .slnx，不是 .sln
dotnet test  Kimi.AppKit.slnx
```

包版本由 [MinVer](https://github.com/adamralph/minver) 从 git tag 推导。
⚠️ CI 检出必须 `fetch-depth: 0`，否则 MinVer 看不到 tag，会静默把所有包打成 `0.0.0-alpha.0`。

## 许可

MIT
