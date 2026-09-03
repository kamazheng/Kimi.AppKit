# Kimi.AppKit.Crud

反射驱动通用 CRUD：登记一个实体即得列表、增删改，数据访问经 `ICrudDataSource<T>` 抽象，
Blazor Server/WASM 两种宿主通用。

隶属 [Kimi.AppKit](https://github.com/kamazheng/Kimi.AppKit)。

## 组件速查表

| 组件 | 一行用途 | 最小示例 |
|---|---|---|
| `KEntityCrudPage<T>` | 登记一个实体即得列表 + 新增 + 编辑 + 删除的完整页面 | `<KEntityCrudPage T="Product" DataSource="@dataSource" />` |
| `KEntityTable<T>` | 只要表格（列从 `T` 反射得到），不要增删改壳子时单独用 | `<KEntityTable T="Product" DataSource="@dataSource" />` |
| `KEntityForm<T>` | 只要表单（字段从 `T` 反射得到） | `<KEntityForm T="Product" Model="@product" OnValidSubmit="SaveAsync" />` |
| `KSearchSelect<T>` | 可搜索下拉，异步分页取候选而不是一次性拉全表 | `<KSearchSelect T="Product" DataSource="@dataSource" DisplayText="@(p => p.Name)" @bind-Value="selected" />` |

## 最小接入示例

```csharp
// Program.cs（Blazor Server / 静态 SSR 宿主，直连 DbContext）
builder.Services.AddScoped<ICrudDataSource<Product>>(sp =>
    new EfCrudDataSource<AppDbContext, Product>(sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));

// 需要通用搜索/筛选（KFilterBar 的 Search、按字段精确筛选）时用反射增强版：
builder.Services.AddScoped<ICrudDataSource<Product>>(sp =>
    new ReflectiveCrudDataSource<AppDbContext, Product>(sp.GetRequiredService<IDbContextFactory<AppDbContext>>()));
```

```razor
@* Pages/Products.razor *@
@inject ICrudDataSource<Product> DataSource
<KEntityCrudPage T="Product" DataSource="DataSource" />
```

字段级定制：
- 不想出现在表格/表单里的属性贴 `[HideFromTable]`（`Kimi.AppKit.Core.Entities`）
- 想排掉某个接口自带的属性（如 `ISoftDeleteEntity.Active`）传 `ExcludeInterfaces`
- 展示名优先取 `[Display(Name = "...")]`，否则把属性名拆成空格分隔的单词

## 已知限制

- `KEntityField`（表单字段的类型分派）只覆盖字符串/布尔/枚举/数值/`DateOnly`；
  其余类型（含嵌套对象、集合）一律退化为只读展示，不强行拼凑语义不对的控件
- 主键（`Id`）字段永远只读，即使整个表单不是只读模式
- 通用搜索/筛选（`ReflectiveCrudDataSource`）的大小写语义因 provider 而异，
  不做跨 provider 归一化，见类型上的 XML doc
