# 生成应用的运行期必配项（部署文档素材）

> 面向：用 `dotnet new kimiapp` 生成的应用的部署者。S2a 部署文档可直接引用本页。

## 必须配置数据库 provider，否则启动即崩溃

生成的应用**没有默认的数据库 provider**。启动时 `Program.cs` 会调用
`DatabaseProviderSetup.Resolve(configuration["Database:Provider"])`，该配置缺失、为空或无法识别时
**直接抛 `ArgumentException`**，进程退出（容器表现为反复重启，日志首行即该异常）。

这是刻意的：旧模板「静默回退 SqlServer」，配错名字的后果是整个迁移基线走错方向，越晚发现越贵。

| 项 | 值 |
|---|---|
| 配置键 | `Database:Provider` |
| 环境变量写法 | `Database__Provider`（双下划线代替冒号） |
| 可选值（不区分大小写） | `Npgsql`（别名 `postgresql` / `postgres`）、`SqlServer`（别名 `mssql`） |
| 配套必配 | `ConnectionStrings__DefaultConnection`（须与所选 provider 一致） |

示例（PostgreSQL）：

```bash
podman run --rm -p 8080:8080 \
  -e Database__Provider=Npgsql \
  -e "ConnectionStrings__DefaultConnection=Host=db;Database=app;Username=app;Password=***" \
  my-app:1.0.0
```

## 说明

- `appsettings.Development.json` 只在开发环境带有 `"Provider": "Npgsql"`；镜像默认
  `ASPNETCORE_ENVIRONMENT=Production`，**不会**读到它，所以容器里必须显式传环境变量。
- 一个 provider 对应一套迁移程序集（`*.Migrations.Npgsql` / `*.Migrations.SqlServer`），provider 与连接串必须同时对齐。
- Hangfire 的存储同样依据该 provider 选择（`AddAppKitHangfire`），无需另配。
