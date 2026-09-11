# Kimi.AppKit.Http

Refit 客户端注册的标准封装：源生成、零反射，统一 JSON 约定与错误处理。

隶属 [Kimi.AppKit](https://github.com/kamazheng/Kimi.AppKit)。

## 为什么是独立的包

Refit 是「客户端调用 API」这一类通用问题的成熟方案（决策 14：废弃自写的
`IStandardApi` 家族）。注册逻辑（JSON 约定、错误处理、URL 参数格式化）如果
留给各消费方各自拼一份 `RefitSettings`，很快就会出现「每个人抄一遍、
抄出来的版本各不一致」——`Kimi.AppKit.Core` 里的 `KHttpResponseExtensions`
早就为此预留了 `BuildException`，只是从未真正接上过 Refit。

## 用法

```csharp
builder.Services.AddAppKitRefitClient<IOrdersApi>(builder.HostEnvironment.BaseAddress);
```

⚠️ **方法名不叫 `AddRefitGeneratedClient`**，虽然那正是决策 14 里写的名字——
实测发现它会与 Refit 官方的 `Refit.HttpClientFactoryExtensions.AddRefitGeneratedClient<T>()`
撞名：调用文件只要有 `using Refit;`，`services.AddRefitGeneratedClient<T>()`（不传
`baseAddress`）就**编译通过**，静默解析到官方那个无参重载——本包的统一 JSON 约定、
`ExceptionFactory`、`CollectionFormat` 全部失效，且只在运行时才暴露（错误提示退化成
状态码、数组查询条件被静默忽略）。换成专属名字后，「有没有走统一封装」才是
grep 得出来的，而不是要盯着参数列表才能分辨的两个重载。

- 内部转调 Refit 官方的 `AddRefitGeneratedClient<T>(RefitSettings)`——
  强制走源生成实现，生成失败编译期报错，不会像 `AddRefitClient<T>()`
  那样静默回落到反射请求构建器（那条回落路径只在 trimmed/Native AOT
  发布产物上才会抛异常，本地 `dotnet run` 看不出来）。
- JSON 固定 `JsonSerializerDefaults.Web`，与 `KHttpCrudDataSource`/
  `GetFromJsonAsync` 的既有行为一致。
- 失败响应经 `KHttpResponseExtensions.BuildException` 提取
  `detail`/`message`/`title`，而不是裸状态码。
- 数组查询参数按 `CollectionFormat.Multi` 展开成重复 key
  （`tags=a&tags=b`），匹配 ASP.NET Core `[FromQuery] string[]` 的绑定方式。

⚠️ **不要给调用方开洞传自定义 `RefitSettings`**——统一配置正是这个包存在的意义。
