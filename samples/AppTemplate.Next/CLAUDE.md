# CLAUDE.md — KMoldApp

`dotnet new` 模板的样例宿主。它同时是**两样东西**：
① 派生真实业务系统的起点；② `Kimi.AppKit` 九个包（8 库 + 模板）的**活体集成测试**。

---

## ⚠️ 动手之前

**第 0 步永远是：查 `Kimi.AppKit` 是不是已经有实现。**

包里有的能力**禁止在本项目里自己写一套**。强制对照表见
`.claude/rules/appkit-api-map.md`——那张表列了 8 个库包的全部公开能力，
以「想做 X → 必须用 Y → 禁止自写 Z」的形式组织。

查法有讲究（错过三次）：只 grep `public static class` 会漏掉接口+实现形态；
只查一个包会漏；按方法名统计用量会把不同类的同名方法混成一个。
正确查法见 `.claude/rules/appkit-consumption.md` 第 0 节。

---

## 刻意不含的东西（裁决落字）

- **Hangfire：模板不含业务作业**。模板只调一次 `AddAppKitHangfire`——它已含存储与后台服务器
  （内部调用 `AddHangfireServer`，**应用侧不要再调**，否则注册两个服务器）；
  不定义任何周期作业，业务作业由各应用自己注册。
- **不含数据库迁移执行脚本**：生产迁移闸门由 S2b 的迁移镜像承接，模板不再带
  `migrate-prod.*`；也不带 `generate-dockerfile.sh`——根目录 `Dockerfile` 即是唯一的镜像构建入口。

---

## 上下文文档体系

| 文件 | 内容 | 何时更新 |
|---|---|---|
| `CONTEXT.md` | 全局业务地图：与 AppKit 的分工、ER 图、主流程、模块职责 | 新增实体 / 变更流程时 |
| `.claude/rules/appkit-api-map.md` | **「想做 X 必须用 Y」强制对照表** | 包新增能力时 |
| `.claude/rules/appkit-consumption.md` | 消费 AppKit 的硬约束与踩坑记录 | 又踩一个坑时 |
| `.claude/rules/domain-constraints.md` | 领域硬约束（第 6 节是框架级，出厂自带） | 新增业务约束时 |
| `.claude/rules/code-change-protocol.md` | 改码强制流程 | 极少变更 |
| `.claude/rules/comment-conventions.md` | 分层注释规范 | 极少变更 |
| `ADR/` | 架构决策记录，**只追加** | 做出重要技术决策时 |

⚠️ `.claude/rules/*.md` 会被自动全量加载进每次会话。新增规则文件时加
`paths:` frontmatter 限定作用域，避免吃满 context window。

---

## 项目结构

```
KMoldApp/                    服务端宿主：装配、端点、预渲染
KMoldApp.Client/             WASM：页面、布局、客户端数据源
KMoldApp.Shared/             两端共用契约：角色/策略、UserInfo、DTO
KMoldApp.Data/               实体、KMoldDbContext、provider 接线
KMoldApp.Migrations.Npgsql/  PostgreSQL 迁移（与下面**互不共用**）
KMoldApp.Migrations.SqlServer/
```

---

## 常用命令

```bash
# 构建（必须 0 Warning / 0 Error）
dotnet build KMoldApp.sln -c Release

# 跑起来（本地 PG 在 5433）
dotnet run --project KMoldApp/KMoldApp.csproj --no-launch-profile --urls http://localhost:5091

# 两套迁移，各自生成
dotnet ef migrations add <名字> --project KMoldApp.Migrations.Npgsql \
  --startup-project KMoldApp.Migrations.Npgsql --context KMoldDbContext -o Migrations
dotnet ef migrations add <名字> --project KMoldApp.Migrations.SqlServer \
  --startup-project KMoldApp.Migrations.SqlServer --context KMoldDbContext -o Migrations
```

⚠️ 本机容器一律用 **podman**（非 docker）。

---

## 验收：构建绿 + 测试绿 **都不算通过**

以下事故全部发生在「构建 0 告警、测试全绿」的情况下：

| 事故 | 靠什么才发现 |
|---|---|
| WASM 起不来 | 截图 |
| 设计系统字体被覆盖 | 截图（`document.fonts` 显示 loaded） |
| 静态资源被授权拦成 HTML | 浏览器 console |
| 管理页「新增」点了没反应 | **实际点击**（页面渲染完全正常） |

**每个 UI 改动必须**：跑起来 → 亮/暗截图 → console 0 错误 → **实际操作一遍功能**。

---

## 生成项目后必做

1. 在 OIDC 服务器注册本应用，把 `Issuer` / `ClientId` / `ClientSecret` 填进 appsettings
2. 配置 `ConnectionStrings:DefaultConnection` 与 `Database:Provider`
3. 改 `KMoldApp.Shared/Constants/AppRoles.cs` 里的 `Prefix` —— 出厂值只是占位
4. 填 `CONTEXT.md` 的 ER 图与主流程，补 `domain-constraints.md` 的业务约束
