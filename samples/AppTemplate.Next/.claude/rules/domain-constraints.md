# 领域硬约束(Domain Constraints)

> 本文件是**跨模块、不可违反**规则的权威清单。AI 修改任何业务代码前必须先查此处(见 `code-change-protocol.md`)。
> 只记"约束"与"为什么",不记实现。新增/变更约束时同步更新。

> ⚠️ 本模板出厂只带**框架级**约束（见第 6 节）。业务约束由派生项目填写。
> 消费 Kimi.AppKit 的约束单独成文：`appkit-consumption.md` 与 `appkit-api-map.md`。

<!-- TODO（派生项目填）：下面 1-5 节是按类别的引导问题，逐项补全后删除不适用的小节。 -->

## 1. 数据完整性约束

<!-- 引导:哪些实体间有外键?删除/创建必须遵守什么顺序?哪些字段唯一?哪些不可为空且无默认值? -->

- 示例:`⚠️ 必须先删 子表 再删 父表,FK 不允许级联删除。`
- 示例:`某编码全局唯一,生成规则见 [模块/服务]。`

## 2. 状态机约束

<!-- 引导:有哪些带状态的实体?合法的状态迁移路径是什么?哪些迁移不可逆?哪些操作只在特定状态允许? -->

- 示例:`订单状态 待支付 → 已支付 → 已发货 → 完成;已支付后不可回退到待支付。`

## 3. 业务规则约束

<!-- 引导:有哪些"必须同时成立/互斥"的业务规则?哪些操作有前置条件?哪些是不可逆操作? -->

- 示例:`一个用户同一时刻只能有一个进行中的工单。`

## 4. 权限与边界约束

<!-- 引导:哪些操作有角色/权限限制?哪些数据有租户/组织隔离?跨边界访问的规则? -->

## 5. 并发与一致性约束

<!-- 引导:哪些操作需要加锁/幂等?哪些有事务边界?哪些最终一致而非强一致? -->

---

> 大型项目可把本文件按子域(bounded context)拆成 `domain-constraints/<子域>.md`,此处保留索引。

<!--
init-context: 拆子域文件时,每个 <子域>.md 顶部必须加 paths frontmatter 限定作用域,
否则 .claude/rules/ 下所有 .md 会被无条件全量加载进每次会话,子域一多就吃满 context
(真实案例:7 个子域文件、约 12 万 token,新会话开局即 98% 占用)。示例:

---
description: 域约束 · <子域一句话说明>
paths:
  - "src/services/<子域相关服务前缀>*"
  - "src/pages/<子域目录>/**"
  - "src/dtos/<子域目录>/**"
---

## 域约束 · <子域名>
...

paths 按该子域实际涉及的目录/文件名前缀列,宁可写宽一点(多个子域重叠加载没问题),
也不要漏掉真正相关的路径。本索引文件与 code-change-protocol.md 等体量小的跨模块
通用文件不加 paths,保持恒常可见。
-->

---

## 6. 框架级约束（模板出厂自带，**不要违反**）

这些来自 `Kimi.AppKit` 与双 provider 部署，与具体业务无关。

### 6.1 删除一律用 `Remove()`

⚠️ 禁止手写 `Active = false`。软删除由 `AuditableDbContext` 拦截 `Remove()` 完成，
手写赋值会绕过拦截——**审计里记成一次普通更新而不是删除**，且那一行仍会出现在所有查询里。

### 6.2 EF Core 查询枚举字段禁止用 `<` `>` `<=` `>=`

枚举以**字符串**存库，`<`/`>` 会被翻译成 SQL 字母序比较，**静默**过滤错误。
用 `==` / `!=` 显式匹配。

### 6.3 改枚举成员后必须生成两套迁移

字符串枚举列上有 CHECK 约束（`ApplyEnumStringConstraints` 生成）。
不迁移则库里仍是旧约束，写入新值撞约束错误，而那个错误完全指不到枚举定义。

### 6.4 字符串比较必须显式声明大小写语义

PostgreSQL **区分**大小写，SQL Server **不**区分。同一句 LINQ 在两边语义不同，
且**静默**给出不同结果，编译与单测全过。走持久化的规范化列，不要依赖数据库 collation。

### 6.5 时间统一 `DateTimeOffset`，落库归一 UTC

Npgsql 的 `timestamptz` **只接受 `Offset == 0`**，传 `+08:00` 直接抛；
SQL Server 的 `datetimeoffset` 照单全收。**同一段代码在 SQL Server 上跑得好好的，
换到 PG 就在写入时崩**，且通常要等生产第一次收到带本地偏移的输入才暴露。

归一由 `ApplyAppKitConventions()` 全局完成（`AuditableDbContext` 已调）。
⚠️ 展示用的时区换算走 `IAppTimeZone`，**不要出现任何 `AddHours(8)`**。

### 6.6 手写 SQL 片段里的标识符必须加引号

PG 把裸标识符折叠成小写，PascalCase 列名（`Status`）写成裸 `Status` 会解析成
`status` → **建表当场失败**；SQL Server 不折叠，所以这个坑**只在切到 PG 时暴露**。
一律经 `IDbProviderDialect.Quote()`；布尔字面量走 `BooleanLiteral()`。
涉及 `HasFilter()` / `HasCheckConstraint()` / `HasComputedColumnSql()` 一切手写 SQL 处。

### 6.7 唯一索引中的 NULL 语义在两个 provider 相反

SQL Server 视多个 NULL 为相同（只允许一行），PG 视为不同（允许多行）。
参与唯一索引的列一律 `NOT NULL` + 空串哨兵。

### 6.8 自引用外键禁止 `SET NULL` / `CASCADE`

SQL Server 会报 Msg 1785。必须用 `NoAction` 并由应用层处理。

### 6.9 不要引入多租户抽象

隔离靠**一客户一容器组 + 独立数据库实例**，不是靠代码。
⚠️ 禁止 `TenantId`、`ICurrentTenant`、全局租户过滤器——那是被明确否决的方案。
租户级差异一律走配置/环境变量。

### 6.10 禁止新增进程内共享可变状态

`static ConcurrentDictionary` / 进程内 `IMemoryCache` 会在多副本部署时行为不一致。
容器隔离下不会跨客户泄露，但会让「同一个操作在不同副本上结果不同」。

### 6.11 审计的失效边界

审计轨迹、软删除改写、`Updated`/`UpdatedBy` 三样**全部挂在 `SaveChanges` 上**。
**任何绕过变更追踪器的写入，这三样一起失效，且不报错**：

- `ExecuteUpdate` / `ExecuteDelete` —— EF Core 官方原话「completely unaware of EF's change tracker」。
  其中 `ExecuteDelete` 是**物理 DELETE**，把软删除彻底绕过。两者**各自不开事务**。
- `ExecuteSqlRaw` 等原生 SQL
- `AsNoTracking` 查出的实体 —— 不在追踪器里，改了既不保存也不审计
- `CurrentValues.SetValues(dto)` 且值完全相同 —— 实体保持 `Unchanged`，一条审计都不产生

这不是能修掉的缺陷（要审计就必须知道旧值，而批量操作的意义恰恰是不把行读进内存）。
**危害在于它静默** —— 用之前要明确知道自己放弃了什么。
