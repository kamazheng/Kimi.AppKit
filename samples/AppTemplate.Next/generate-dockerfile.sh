#!/bin/bash
# 按当前解决方案名生成一份 Dockerfile。
#
# ⚠️ 本文件必须存为 **UTF-8**。原先这里的中文注释是 GBK 编码，在 UTF-8 终端与编辑器里
#    显示为乱码；而 .editorconfig 与 ChangeProjectName.ps1 的策略是「保留原有编码状态」，
#    不会替你转码，于是乱码会一路带进客户的项目里。
#
# 前提：解决方案名与主项目名相同（`dotnet new kimiapp` 生成的结构满足这一点）。

set -euo pipefail

solution_name=$(ls ./*.sln 2>/dev/null | head -1 | sed 's|.*/||; s|\.sln$||')

if [ -z "$solution_name" ]; then
    echo "当前目录下没有 .sln，无法推断项目名。" >&2
    exit 1
fi

# ⚠️ 基础镜像标签必须与 Directory.Build.props 里的 TargetFramework 对齐。
#    两者一旦漂移，本地 dotnet build 一切正常、只有镜像构建才报错，
#    而报错发生在还原阶段、指向的是包版本，看不出根因是镜像里的 SDK 版本不对。
cat <<EOF > Dockerfile
# Stage 1: 构建
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

COPY . .

RUN dotnet restore "$solution_name.sln" --configfile ./NuGet.config
RUN dotnet test "$solution_name.sln" --no-restore
# ⚠️ 不要在这里插 dotnet clean：它会删掉上一步刚还原/编译的产物，
#    使 publish 不得不重新还原一次，镜像构建时间翻倍。
RUN dotnet publish "$solution_name/$solution_name.csproj" -c Release -o /app/out --no-restore

# Stage 2: 运行
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "$solution_name.dll"]
EOF

echo "已为解决方案 $solution_name 生成 Dockerfile。"
