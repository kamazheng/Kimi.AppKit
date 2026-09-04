---
paths:
  - "**/*.cs"
  - "**/*.razor"
  - "**/*.csproj"
  - "**/Directory.*.props"
---

# 消费 Kimi.AppKit 的硬约束

> 本文件的每一条都对应一次**真实的返工或线上级缺陷**，不是风格偏好。
> 来源：`Kimi.KMold/docs/retro/appkit/X2-*.md` 各阶段复盘。

---

## 0. 铁律：任何编码之前，先检查 AppKit 已有的实现

**这是本文件里唯一一条元规则，违反它会让下面所有条款失效。**

写任何一行「看起来通用」的代码之前，先按顺序查三处：

| 查哪里 | 命令 | 回答什么 |
|---|---|---|
| 包 | 见下方「正确的查法」 | 已经有同名/同职责的东西了吗 |
| MES | `grep -rn "关键词" ~/Developer/work/CDU_TMES`（**只读**） | 这个能力真的被用吗、用多少处 |
| 本项目 | `grep -rn` | 模板演示需不需要它 |

### 正确的查法（错过三次的教训）

```bash
# ✅ 列出包的**全部公开类型**，不按形态过滤
cd ~/Developer/personal/Kimi.AppKit/src/<包名>
grep -rhoE 'public (sealed |abstract |static |partial )*(class|interface|record|struct|enum) [A-Za-z0-9_<>]+' \
  --include='*.cs' . | sed -E 's/public (sealed |abstract |static |partial )*//' | sort -u
```

- ❌ **只 grep `public static class` 会漏掉「接口 + 实现」形态。**
  曾因此把 `CstExtensions` 判为「需要重设计」，实际 `Kimi.AppKit.Core.Time.IAppTimeZone`
  早就存在且更完整（含 DST、IANA/Windows 双 ID 兜底）。
- ❌ **只在一个包里找会漏。** 曾在 `Components` 包里找不到可搜索下拉，
  结论是「包里没有」，实际 `Kimi.AppKit.Crud` 里有 `KSearchSelect`。
- ❌ **按方法名统计用量会把不同类的同名方法混成一个。**
  `GetDisplayLabel` 记的是 22 次，拆开后 26 次属于另一个类的表达式树重载，
  真正相关的只有 5 次。同一个名字在这套代码库里有**三个**实现。

### 判「与包重叠」之前，逐行对比两者到底做了什么

名字像、职责描述像，**不代表功能集相同**：

- `ConfigureConventions` vs `ApplyAppKitConventions`——名字几乎一样，
  实际一个管精度与长度、一个管 `DateTimeOffset` 的 UTC 归一。**直接删会静默丢能力。**
- `SearchableSelect` vs `KSearchField`——一个是可搜索下拉、一个是搜索框，根本不是一个东西。

**判「删」之前，先列出「这份有而替代品没有的能力」，再逐条查那些能力的真实使用。**

---

## 1. 不要设全局约定去套住框架自己的表

```csharp
// ❌ 绝对不要
protected override void ConfigureConventions(ModelConfigurationBuilder builder)
{
    base.ConfigureConventions(builder);
    builder.Properties<string>().HaveMaxLength(100);   // 把审计表也套住了
}
```

**后果**：`Trail.OldValues` / `NewValues` 存整行变更的 JSON，长度不可预估。
被限制成 `varchar(100)` 之后**每一次带审计的写操作都会失败**：

- PostgreSQL：`22001: value too long for type character varying(100)`
- SQL Server：「字符串或二进制数据将被截断」

⚠️ 更麻烦的是**错误指向审计表**，而不是用户正在保存的那条业务数据，排查时会往业务字段上找。

**正确做法**：字段长度由**各实体自己**用 `[StringLength]` 或 Fluent 声明。
参照 `Kimi.KMold.Auth` 与 `Kimi.KMold.Files`——两个已投产的消费方，
它们的 `ConfigureConventions` 里**都只有 `ApplyAppKitConventions()`**。

---

## 2. 渲染模式：全站统一，不要混用

本模板全局 `InteractiveWebAssembly`。**不要为了某个页面能直连 EF 就把它改成服务端渲染。**

混用的代价是一连串结构性问题，全部实测踩过：

1. `Routes.razor` 放客户端时 `AppAssembly` 是 Client 程序集，
   **服务端页面根本不会被路由发现**——表现是 NotFound，不是任何错误。
2. 把 `Routes` 移到服务端后布局变成静态 SSR，
   **MudBlazor 对话框「点了没反应且零报错」**（`IDialogService` 能解析、`ShowAsync` 也不抛）。
3. 给两个渲染模式各放一份 Provider 会抢同一个 section ID，直接抛
   `There is already a subscriber to the content with the given section ID`。

**页面要数据就走 `KHttpCrudDataSource`**（`Kimi.AppKit.Crud.Http`），对接服务端的 `MapCrudEndpoints`。

⚠️ 服务端必须**成对**注册 `AddPrerenderCrudDataSource<T>()`：
预渲染阶段也要实例化页面的注入属性，即使 `prerender: false`。漏了整页 500，
且错误信息指向组件属性注入、不指向注册。

---

## 3. 两端必须一致的三样东西

WASM 与服务端是**两个进程、两个 DI 容器**。以下三样各写一份必然漂移：

| 项 | 后果 |
|---|---|
| **授权策略** | 客户端页面抛 `The AuthorizationPolicy named ... was not found` 整页白屏；或前端看得见的菜单后端 403 |
| **角色常量** | role claim 匹配不上，页面一律 403，**且失败是静默的** |
| **MudBlazor / 对话框服务** | 服务端**预渲染**含这些组件的页面时也要能解析，少了直接 500：`Cannot provide a value for property 'PopoverService'` |

**做法**：策略定义与角色常量放 `KMoldApp.Shared`，两端各调一次同一个方法。

---

## 4. 授权：安全默认值 + 显式放宽

- `AddDefaultDenyAuthorization()` 已内置 `/_framework` 放行，
  **不要在 `configure` 回调里重设 `FallbackPolicy`**——覆盖掉会让 WASM 起不动，
  且症状（integrity 校验失败）完全指不到授权。要加策略用 `options.AddPolicy(...)`。
- `app.MapStaticAssets()` 必须 `.AllowAnonymous()`：它是**端点路由**受 `FallbackPolicy` 管，
  而它取代的 `UseStaticFiles` 是**中间件**不受管。漏了的表现是每个 css/js 都被 302 成 HTML，
  控制台只有一句 `Unexpected token '<'`。
- Blazor 端点整体 `.AllowAnonymous()`，页面各自靠 `[Authorize]` 把关。
  端点级一刀切会把**登录页自己**也要求登录，形成重定向死循环。
- 开发期权限绕过（`Auth:RoleBypass:Enabled`）**默认关闭**。
  ⚠️ 不要改成「非生产默认开启」——那不只是安全问题，更是**可测性**问题：
  在 Staging 验证「普通用户看不到管理菜单」时所有人都是 Root，测试通过了，上生产才发现权限没配对。

---

## 5. 双 provider：两套迁移，各自生成

- 模型改动后**两套迁移都要重新生成**，两个 provider 都要跑测试。
- 手写 SQL 片段（`HasCheckConstraint` / `HasFilter` / `HasComputedColumnSql`）里的标识符
  必须经 `IDbProviderDialect.Quote()`。PG 把裸标识符折叠成小写，
  `Status` 会变 `status` → **建表当场失败**；SQL Server 不折叠，所以这个坑
  **只在切到 PG 时才暴露**。布尔字面量同理走 `BooleanLiteral()`。
- provider 判定用包的 `DatabaseProviderSetup.Resolve()`——它在无法识别时**抛异常**。
  ⚠️ 不要自己写一个「静默回退 SqlServer」的版本：配错 provider 会让整个迁移基线走错方向，
  静默兜底只会让它更晚被发现。

---

## 6. 静态 SSR 页面（如密码登录）的三个静默坑

若确实需要静态 SSR 页面（要在响应里种 cookie 的场景）：

1. **MudBlazor 输入控件在静态 SSR 表单里不生成 `name` 属性**（它们不继承 `InputBase`）——
   提交后所有字段为空、校验却通过。认证页一律用原生 `<input>` + 自有样式。
2. **`MudDrawer` 的开合是组件状态**，静态渲染下汉堡按钮点了没反应。
3. **Blazor 并发渲染同一棵树里的组件**，布局与页面会抢同一个 scoped `DbContext`，
   抛 `A second operation was started on this context instance`。布局里的数据读取
   必须用 `IDbContextFactory` 或另开作用域。

---

## 7. 认证态：不要把长期凭据交给浏览器

`UserInfo` **只携带 id token，不带 refresh token**。

- refresh token 是长期凭据，XSS 拿到就能长期冒充；id token 至少会过期。
- 而且它是多余的：包的 `ConfigureCookieOidcRefresh` 已在**服务端** Cookie 校验时自动续期。

📌 **一般化**：把凭据/敏感数据交给客户端之前，先问一句
「服务端是不是已经在做这件事了」。

⚠️ `UserInfo.FromClaimsPrincipal` **不要因缺 claim 抛异常**。
`displayname` 是不少 IdP 根本不发的可选 claim，而这个异常发生在
`PersistentComponentState` 的持久化回调里——后果是客户端认证态整个丢失、
用户被弹回登录页，日志里却只有一句「找不到某 claim」，看不出它会导致登不上。

---

## 8. 验收：构建绿 + 测试绿 + curl 200 **都不算通过**

以下三次事故全部发生在「构建 0 告警、测试全绿」的情况下：

| 事故 | 靠什么发现的 |
|---|---|
| `MapStaticAssets` 迁移后 WASM 起不来 | 截图（curl 打静态资源全部 200） |
| 设计系统字体被 `app.css` 覆盖 | 截图（`document.fonts` 显示 loaded） |
| 静态资源被授权拦成 HTML | 浏览器 console |
| 管理页「新增」点了没反应 | **实际点击**（页面渲染完全正常） |

**每个改动都要**：跑起来 → 亮/暗截图 → 浏览器 console 0 错误 → **实际操作一遍功能**。

⚠️ 最后一条是用最惨痛的方式学到的：管理页截图看着完美，点新增什么都不发生、零报错。
