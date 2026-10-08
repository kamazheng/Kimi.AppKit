# 多阶段构建：sdk 还原+发布 → aspnet 运行。
#   podman build -t kmoldapp:dev .      （在本目录；不需要 --build-context，只走 nuget.org）
#
# ⚠️ 本文件里的 `KMoldApp` 是 dotnet new 的 sourceName：生成项目时会随项目名一起被替换，
#    所以这里**不要**改成别的占位名，也不要改成变量。
# ⚠️ 构建阶段**不跑测试**。测试是 CI 的事（需要数据库、会让镜像构建耗时翻倍），
#    且测试程序集不该进运行镜像——发布只针对主工程，Tests 不在其依赖闭包里。
# ⚠️ 基础镜像标签（10.0）必须与 Directory.Build.props 的 TargetFramework 对齐，
#    漂移时报错发生在还原阶段、指向包版本，看不出根因是镜像里的 SDK 不对。

# Stage 1: 构建
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# ⚠️ 不要拆成「先只拷 csproj/props 做 restore 层缓存、再 COPY 源码」。
#    曾这么做：构建全绿、镜像也起得来，但页面上所有交互全死——
#    `/_framework/blazor.web.js` 404，且没有任何构建告警。
#    原因：SDK 是否把 Microsoft.AspNetCore.App.Internal.Assets（blazor.web.js 的来源）
#    加进还原图，取决于它在**还原时**能否看到 .razor 文件；只有 csproj 时看不到，
#    还原出来的图里就没有这个包，发布产物里也就没有 blazor.web.js。
#    代价是源码一变就重新还原；要缓存可在 podman/buildah 上加 `RUN --mount=type=cache`。
COPY . .
# ⚠️ 不要在这里插 dotnet clean，也不要加 --no-restore（见上）。
RUN dotnet publish KMoldApp/KMoldApp.csproj -c Release -o /app/out

# Stage 2: 运行
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .

# 非 root（数字 UID，便于 K8s/ACA 的 runAsNonRoot 校验）；监听 8080（非特权端口）。
# （aspnet 基础镜像已设 ASPNETCORE_HTTP_PORTS=8080，故无需再配 ASPNETCORE_URLS。）
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER 10001
ENTRYPOINT ["dotnet", "KMoldApp.dll"]
