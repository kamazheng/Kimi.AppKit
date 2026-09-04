# ADR-0001 · AppKit 优先：包里有的一律不自己写

- **状态**：已采纳
- **日期**：2026-09-04
- **相关**：`.claude/rules/appkit-api-map.md`、`.claude/rules/appkit-consumption.md`

## 背景

本模板从 `dotnet new blazor` 官方基座重铺，逐块审入旧模板（CDU_TMES 血统）的能力。
审计过程中反复出现同一种情况：**应用里写了一份，包里其实早就有，且包里那份更好。**

已确认的重复（每一处都是真实返工）：

| 应用里的 | 包里早就有的 | 包那份好在哪 |
|---|---|---|
| `CstExtensions`（189 次调用） | `IAppTimeZone` | 时区可配、处理 DST、IANA/Windows 双 ID 兜底 |
| `ChengduTimeProvider` / `CstHelper` | 同上 | 硬编码 UTC+8 是产品级缺陷 |
| `GetDisplayLabel`（`Type` 版） | `DisplayLabelExtensions`（`MemberInfo` 版） | 覆盖属性/枚举字段/类型 |
| `PagedResult<T>` | `KPage<T>` | record + 只读集合 + 主构造 |
| `ISkipNameUnique` | 同名接口 | —— |
| `BaseException` | 同名类 | 默认 400 而非 500 |
| `DatabaseProviderSetup` | 同名类 | 无法识别时**抛异常**而非静默回退 |
| `ExcelService`（静态类） | `IExcelService` | 泛型、可 mock、不耦合 `FileContentResult` |
| `RoleGate` / `ReasonConfirmDialog` / `SearchableSelect` | `KRoleGate` / `KReasonConfirmDialog` / `KSearchSelect` | —— |

共 **9 处**，其中 7 处是**同名撞名**。

## 决策

1. **包里有的能力，一律用包的，禁止在应用里自己写一套。**
   强制清单见 `.claude/rules/appkit-api-map.md`（「想做 X → 必须用 Y」对照表）。
2. **任何编码之前先查 AppKit**，查法见 `.claude/rules/appkit-consumption.md` 第 0 节。
   这一步写进了改码协议的「第 0 步」。
3. 要改包里的行为，**改包或给包加可选参数**，不要在应用侧另起炉灶。
4. 确实需要自己实现时，在本目录追加一条 ADR，写明**为什么不用包的 / 为什么不进包**。

## 后果

**正面**

- 消除「两个像的东西不知道该用哪个」——那是撞名与行为漂移的根源。
- 包里的坑修一次，所有消费方受益。反例：`EnumStringConvention` 早就正确处理了
  PG 标识符折叠，而应用侧的 `HasRange` 没有，同一个坑在应用里又踩一次。

**代价**

- 想要的能力包里没有时，多一步「判断该不该进包」的决策成本。
- 包的 API 变更会波及所有消费方——但那正是「单一实现」的应有之义。

**已知例外**（各自有理由，不构成对本决策的削弱）

- `Setting` / `EmailTemplate` 实体**刻意留在应用**：进包意味着两套迁移跟包版本走，
  且客户不能给设置表加自己的列。包只认 `IKSettingEntity` 接口。
- 「允许自签证书的 IdP」逃生舱**刻意不进包**：框架包不该出厂就带绕过证书校验的开关。
- `Apply(DbContextOptionsBuilder, provider, conn)` 留在应用：它必须引用具体驱动，
  而包刻意不依赖任何 provider。
