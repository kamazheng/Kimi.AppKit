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

# 先只拷还原所需的清单，让 restore 层在源码变动时仍命中缓存。
COPY NuGet.config Directory.Build.props Directory.Packages.props ./
COPY KMoldApp/KMoldApp.csproj KMoldApp/
COPY KMoldApp.Client/KMoldApp.Client.csproj KMoldApp.Client/
COPY KMoldApp.Shared/KMoldApp.Shared.csproj KMoldApp.Shared/
COPY KMoldApp.Data/KMoldApp.Data.csproj KMoldApp.Data/
RUN dotnet restore KMoldApp/KMoldApp.csproj --configfile NuGet.config

COPY . .
# ⚠️ 不要在这里插 dotnet clean：它会删掉刚还原的产物，使 publish 重新还原一次。
RUN dotnet publish KMoldApp/KMoldApp.csproj -c Release -o /app/out --no-restore

# Stage 2: 运行
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .

# 非 root（数字 UID，便于 K8s/ACA 的 runAsNonRoot 校验）；监听 8080（非特权端口）。
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER 10001
ENTRYPOINT ["dotnet", "KMoldApp.dll"]
