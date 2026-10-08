# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。
0.x 期间次版本号可含破坏性变更，请钉精确版本。

## [Unreleased]

## [0.1.0]

首个公开发布。9 个包同版本发布：

- `Kimi.AppKit.Core`
- `Kimi.AppKit.Data`
- `Kimi.AppKit.Web`
- `Kimi.AppKit.Design`
- `Kimi.AppKit.Components`
- `Kimi.AppKit.Crud`
- `Kimi.AppKit.Http`
- `Kimi.AppKit.Observability`
- `Kimi.AppKit.Templates`（`dotnet new kimiapp`）

### Changed

- **Excel：NPOI 换为 ClosedXML（MIT）。** `AddAppKitExcel()` 的行为契约不变；依赖图中不再有 NPOI 与 SixLabors.ImageSharp
  （前者 2.7.x 依赖带未修复公告的 ImageSharp 2.x，2.8+ 许可不适合商业分发）。需要复杂带样式报表的应用请直接引用 ClosedXML，
  版本与 AppKit 保持一致（经 CPM）。
- **`KUserInfo.DisplayName` 的 claim 名由 `displayname` 改为 `display_name`**，与 Kimi.KMold.Auth 签发的 claim 一致。
  不做双收；自行签发 `displayname` 的 IdP 需改名。
- **行为：`EfCrudDataSource.UpsertAsync` 保存前做 DataAnnotations 校验。** 此前端点手工读 body、自动校验不跑，
  `[Required]`/`[MaxLength]` 形同虚设（名称留空也能保存）。现在校验失败返回 `KResult.Fail`（端点转 400），不落库；
  Excel 导入逐行调用同一方法，沿用既有「逐行报错」语义。`IAuditableEntity` 的四个审计属性不参与校验。
  校验消息为中文（字段名取 `[Display(Name)]`，显式 `ErrorMessage` 优先）。
  唯一约束冲突（PostgreSQL 23505 / SQL Server 2601、2627 / SQLite 2067、1555）同样返回可读的 `KResult.Fail`，不再冒成 500。
  实体上若给导航属性或服务端填充字段标了 `[Required]`，升级后会开始被拒，请移除该特性。

### Added

- 8 个库工程提供 `PublicAPI.Shipped.txt`（Microsoft.CodeAnalysis.PublicApiAnalyzers），此后公开 API 的变化必须显式登记。
- 项目模板内置多阶段 `Dockerfile` 与 `.dockerignore`（非 root 运行、不含测试程序集）。
- `THIRD-PARTY-NOTICES.md`：第三方依赖许可清单，CI 校验其与依赖图同步且许可在白名单内。

### Removed

- 模板中的 `generate-dockerfile.sh`、`scripts/migrate-prod.*`（迁移由部署侧的迁移镜像承担）。

[Unreleased]: https://github.com/kamazheng/Kimi.AppKit/compare/0.1.0...HEAD
[0.1.0]: https://github.com/kamazheng/Kimi.AppKit/releases/tag/0.1.0
