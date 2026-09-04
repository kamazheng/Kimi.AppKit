#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# 把 EF Core 未决迁移应用到目标环境数据库(默认 Production)。直接运行、无需传参。
# 【默认】用 Windows/AD 集成认证(kinit 身份):应用照常从 Nacos 解析连接(已鉴权),再由应用把该连接
# 【改写为 Integrated Security】(去掉 Nacos 的 SQL 账密),故 DDL 以你的域账号执行(需有 DDL 权限)。
#
# 为什么:Nacos SQL 账号通常无 DDL 权限(Error 1088);且 Nacos 配置 API 需鉴权、脚本自己拉不到。
# 故复用应用自身的 Nacos 解析:设 MIGRATION_INTEGRATED_AUTH=1,Program.cs 构造 DbContext 时改写为集成认证
# (仅此变量存在时;正常运行不受影响)。
# ⚠️ 先取 Kerberos 票:  kinit <你的AD账号>@<REALM>
# ⚠️ 执行前请务必【备份目标数据库】。幂等,可重复运行。
#
# 用法:
#   ./scripts/migrate-prod.sh                 # 生产,集成认证(需 YES 确认)
#   ./scripts/migrate-prod.sh Staging         # 预发
#   NACOS_SQL_AUTH=1 ./scripts/migrate-prod.sh  # 改用 Nacos SQL 账号(仅当其有 DDL 权限)
#   MIGRATION_CONNECTION='...' ./scripts/migrate-prod.sh  # 完全手动指定连接串
#   SKIP_CONFIRM=1 ./scripts/migrate-prod.sh  # 跳过确认(自动化,慎用)
#
# 适用平台:macOS/Linux(bash)。Windows 用 scripts\migrate-prod.ps1。两脚本行为一致。
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

TARGET_ENV="${1:-Production}"
case "$TARGET_ENV" in Development|Staging|Production) ;; *) echo "无效环境: $TARGET_ENV"; exit 1 ;; esac

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO_ROOT/KMoldApp/KMoldApp.csproj"

step() { printf "\n[%s] %s\n" "$1" "$2"; }
ok()   { printf "    \xE2\x9C\x93 %s\n" "$1"; }
info() { printf "    · %s\n" "$1"; }
die()  { printf "    \xE2\x9C\x97 %s\n" "$1"; exit 1; }

echo "=== EF Core 数据库迁移 · 目标环境: $TARGET_ENV ==="

# ── [1] 工具检查 ──
step '1/7' '检查 dotnet / dotnet-ef'
command -v dotnet >/dev/null 2>&1 || die '未找到 dotnet,请装 .NET SDK。'
dotnet ef --version >/dev/null 2>&1 || die '未装 dotnet-ef:dotnet tool install --global dotnet-ef'
ok 'dotnet-ef 可用'

# ── [2] 决定认证方式 ──
step '2/7' '认证方式'
CONN_ARGS=()
USE_INTEGRATED=0
if [ -n "${MIGRATION_CONNECTION:-}" ]; then
  CONN_ARGS=(--connection "$MIGRATION_CONNECTION"); ok '手动 MIGRATION_CONNECTION'
elif [ "${NACOS_SQL_AUTH:-}" = "1" ]; then
  ok 'Nacos 下发的 SQL 账号(需其自身有 DDL 权限)'
else
  USE_INTEGRATED=1
  ok 'Windows/AD 集成认证(应用把 Nacos 连接改写为 Integrated Security,用当前 kinit 身份)'
fi

# ── [3] Kerberos 票据(集成认证需要)──
step '3/7' 'Kerberos 票据'
if [ "$USE_INTEGRATED" = "1" ]; then
  if klist >/dev/null 2>&1; then ok '已有 Kerberos 票据'
  else printf "    \xE2\x9A\xA0 未检测到 Kerberos 票据,请先: kinit <你的AD账号>@<REALM>\n"; fi
else
  info '非集成认证路径,跳过'
fi

# ── [4] 备份确认 ──
step '4/7' '备份确认'
if [ "${SKIP_CONFIRM:-}" != "1" ]; then
  printf "    \xE2\x9A\xA0 即将把未决迁移应用到【%s】数据库,请确认已【备份】。\n" "$TARGET_ENV"
  read -r -p "    已备份并确认继续? 键入 YES: " c
  [ "$c" = "YES" ] || die '已取消(未输入 YES)。'
fi
ok '已确认'

# MIGRATION_INTEGRATED_AUTH:仅集成认证路径设置,触发 Program.cs 改写连接。用行内前缀,只作用于该命令子进程。
MIG_ENV=()
[ "$USE_INTEGRATED" = "1" ] && MIG_ENV=(MIGRATION_INTEGRATED_AUTH=1)

# ── [5] restore + obj 路径探测 ──
# ⚠️ dotnet ef 的元数据构建【不吃】Directory.Build.props 里的 BaseIntermediateOutputPath(如把 obj 重定向到
#    /Volumes/RAMDisk 的开发机),会去默认 KMoldApp/obj/ 找 assets 而报 MSB4057「GetEFProjectMetadata does
#    not exist」。故先按 Nexus 配置 restore,再用 msbuild 查出【真实】obj 路径显式传给 ef,兼容默认与自定义路径。
step '5/7' 'restore + 探测 obj 路径'
dotnet restore "$REPO_ROOT/KMoldApp.sln" --configfile "$REPO_ROOT/NuGet.config" >/dev/null 2>&1 \
  && ok 'restore 完成' || info 'restore 跳过/失败(若已 restore 可忽略)'
EXT_ARGS=()
EXT_PATH="$(dotnet msbuild "$PROJECT" -getProperty:MSBuildProjectExtensionsPath 2>/dev/null | tr -d '\r' | tail -1)"
if [ -n "$EXT_PATH" ]; then EXT_ARGS=(--msbuildprojectextensionspath "$EXT_PATH"); ok "obj 路径: $EXT_PATH"
else info 'obj 路径:用默认(未探测到自定义 BaseIntermediateOutputPath)'; fi

# ── [6] 迁移清单 + 应用 ──
step '6/7' '迁移清单 + 应用(list → database update)'
env ASPNETCORE_ENVIRONMENT="$TARGET_ENV" ${MIG_ENV[@]+"${MIG_ENV[@]}"} \
  dotnet ef migrations list --project "$PROJECT" --startup-project "$PROJECT" \
    ${EXT_ARGS[@]+"${EXT_ARGS[@]}"} ${CONN_ARGS[@]+"${CONN_ARGS[@]}"} \
  || die 'migrations list 失败:多为【连不上库 / Kerberos 认证失败 / SPN 解析不到】。检查 kinit 票、账号能否登录该库、Server 是否 FQDN。'
ok 'migrations list 成功(连接 + 认证 OK)'

env ASPNETCORE_ENVIRONMENT="$TARGET_ENV" ${MIG_ENV[@]+"${MIG_ENV[@]}"} \
  dotnet ef database update --project "$PROJECT" --startup-project "$PROJECT" \
    ${EXT_ARGS[@]+"${EXT_ARGS[@]}"} ${CONN_ARGS[@]+"${CONN_ARGS[@]}"} \
  || die 'database update 失败:若报 Error 1088(对象不存在/无权限),多为【该账号无 DDL 权限】。请用有 db_ddladmin/db_owner 的域账号。修复后可重跑(幂等);必要时还原备份。'

# ── [7] 完成 ──
step '7/7' '完成'
ok "迁移完成($TARGET_ENV)。"
