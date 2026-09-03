# Kimi.AppKit.Components

标准化 Blazor 组件库，渲染模式无关（静态 SSR / InteractiveServer / WebAssembly 三种模式通用）。

隶属 [Kimi.AppKit](https://github.com/kamazheng/Kimi.AppKit)。

## 组件速查表

> 目标：AI 与人在写业务页面时，看这张表就知道该用哪个组件，不必读实现。
> 「包里有但没人知道」正是这张表要解决的问题——前身私有包里 `DeleteConfirmation`
> 组件全库 0 使用，而 `ShowMessageBox` 被手写了 65 处。

| 组件 | 一行用途 | 最小示例 |
|---|---|---|
| `KMudProviders` | MudBlazor 弹出层 Provider（Popover/Dialog/Snackbar）。静态 SSR/Server 宿主须显式加 `@rendermode` | `<KMudProviders @rendermode="InteractiveServer" />` |
| `KReconnectModal` | Blazor Server 断线重连遮罩（中文文案，取代默认的英文遮罩） | `<KReconnectModal />`（放在布局最外层，全站一份） |
| `KEnvChip` | 非生产环境警示条，生产环境不渲染任何内容 | `<KEnvChip />`（需注入 `IKEnvironment`） |
| `KBrand` | 侧栏/顶栏品牌区：Logo 或首字母标记 + 公司名/产品名 | `<KBrand CompanyName="Acme" ProductName="MES" LogoUrl="@logoUrl" />` |
| `KAvatar` | 首字母头像，统一"取首字母、空值兜底"逻辑 | `<KAvatar Name="@user.Name" />` |
| `KCard` | 内容卡片：标题 + 说明 + 内容，用于表单分组/详情分节 | `<KCard Title="恢复码" Lede="每个只能用一次。">...</KCard>` |
| `KAlert` | 语义化提示条，薄封装 `MudAlert` | `<KAlert Severity="Severity.Warning">库存不足</KAlert>` |
| `KEmpty` | 空态：列表/详情没有数据时展示，也可直接用作 `MudTable` 的 `NoRecordsContent` | `<KEmpty Text="没有匹配的记录" />` |
| `KLabeledValue` | 详情页"字段名: 字段值"只读展示 | `<KLabeledValue Label="客户端 ID" Value="@client.Id" />` |
| `KRoleGate` | 角色门禁：有权限渲染内容，无权限**禁用并提示缺哪个角色**（不隐藏） | `<KRoleGate Roles="Admin,Root">...</KRoleGate>` |
| `ErrorCatchButton` / `ErrorCatchIconButton` / `ErrorCatchFab` | 统一"点击→请求→loading→错误处理"的按钮三种形态，取代各页各写一遍的 try/catch | `<ErrorCatchButton Label="保存" OnClick="SaveAsync" />` |
| `KEnumChip<TEnum>` | 枚举 → 颜色芯片：贴 `[EnumChipColor]` 声明颜色，不贴则按声明顺序轮转 | `<KEnumChip Value="@order.Status" />` |
| `KStatusChip` | 语义状态芯片：颜色词汇与 `KAlert` 共用同一套 `Severity` | `<KStatusChip Severity="Severity.Warning" Text="待审核" />` |
| `KMetricCard` | 仪表盘指标卡片：标题 + 大字号数值 + 可选趋势 | `<KMetricCard Title="今日入库" Value="128" />` |
| `KCodeBlock` | 等宽代码/日志块，带一键复制 | `<KCodeBlock Code="@json" />` |
| `KDataTable<T>` | 服务端分页表格：接 `ICrudDataSource<T>` + `KQuery`/`KPage<T>`，内部处理 0/1-based 换算 | `<KDataTable DataSource="@source" HeaderContent="..." RowTemplate="row => ..." />` |
| `KFilterBar` / `KSearchField` | 筛选条：搜索框（防抖+Enter 触发）+ 自定义筛选项插槽 | `<KFilterBar Search="@s" SearchChanged="OnSearch">...</KFilterBar>` |
| `KField` / `KMudField` | 表单字段双变体：静态 SSR 用 `KField`（原生 input），交互式用 `KMudField`（MudBlazor）。当前只覆盖字符串字段 | `<KField Label="邮箱" Id="Email" @bind-Value="m.Email" For="() => m.Email" />` |

## 服务注册

```csharp
builder.Services.AddAppKitDialogs(); // 注册 IKConfirm/IKNotify 的 MudBlazor 实现
```

## 已知限制（本批次未覆盖，见 P6b）

- `KSearchSelect`（可搜索下拉，依赖 `ICrudDataSource<T>` 做异步选项加载，随 Crud 包一起做）
- `KField`/`KMudField` 只覆盖字符串字段；数值/日期/布尔变体待真实需求出现再抽象
- 反射驱动的 `KEntityTable`/`KEntityForm`/`KEntityCrudPage<T>` 见 `Kimi.AppKit.Crud` 包
