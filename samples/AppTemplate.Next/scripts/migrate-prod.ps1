#!/usr/bin/env pwsh
<#
.SYNOPSIS
  把 EF Core 未决迁移应用到目标环境数据库(默认 Production)。直接运行、无需传参。
  【默认】用 Windows/AD 集成认证(kinit 身份)执行:应用照常从 Nacos 解析连接(已鉴权),再由应用把该连接
  【改写为 Integrated Security】(去掉 Nacos 的 SQL 账密),故 DDL 以你的域账号执行(该账号需有 DDL 权限)。

.DESCRIPTION
  为什么:Nacos 下发的是应用 SQL 账号(通常无 DDL 权限,跑迁移报 Error 1088);且 Nacos 配置 REST API 需鉴权,
  脚本无法自行拉取。故复用【应用自身】的 Nacos 解析:脚本设环境变量 MIGRATION_INTEGRATED_AUTH=1,
  Program.cs 在构造 DbContext 时把连接改写为集成认证(仅此变量存在时;正常运行不受影响)。
  ⚠️ 先取 Kerberos 票:  kinit <你的AD账号>@<REALM>
  ⚠️ 执行前请务必【备份目标数据库】(脚本只做确认闸门,不代做备份)。幂等,可重复运行。

.PARAMETER Environment
  Development / Staging / Production(默认 Production)。决定 Nacos 命名空间。

.PARAMETER NacosSqlAuth
  改用 Nacos 下发的 SQL 账号(不换集成认证)——仅当该账号确有 DDL 权限时用。

.PARAMETER ConnectionString
  完全手动指定连接串(跳过上面两种)。

.PARAMETER SkipConfirm
  跳过"已备份"确认(自动化用,慎用)。

.EXAMPLE
  kinit kzheng@example.com
  pwsh scripts/migrate-prod.ps1
  # 直接跑:集成认证迁移生产库
#>
[CmdletBinding()]
param(
    [ValidateSet('Development', 'Staging', 'Production')]
    [Alias('Env')]
    [string]$Environment = 'Production',
    [switch]$NacosSqlAuth,
    [string]$ConnectionString,
    [switch]$SkipConfirm
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = [IO.Path]::Combine($RepoRoot, 'KMoldApp', 'KMoldApp.csproj')

function Step($n, $msg) { Write-Host "`n[$n] $msg" -ForegroundColor Cyan }
function Ok($msg) { Write-Host "    ✓ $msg" -ForegroundColor Green }
function Info($msg) { Write-Host "    · $msg" -ForegroundColor DarkGray }
function Die($msg) { Write-Host "    ✗ $msg" -ForegroundColor Red; exit 1 }

Write-Host "=== EF Core 数据库迁移 · 目标环境: $Environment ===" -ForegroundColor White

# ── [1] 工具检查 ──
Step '1/6' '检查 dotnet / dotnet-ef'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Die '未找到 dotnet,请装 .NET SDK。' }
$null = dotnet ef --version 2>&1
if ($LASTEXITCODE -ne 0) { Die '未装 dotnet-ef:dotnet tool install --global dotnet-ef' }
Ok 'dotnet-ef 可用'

# ── [2] 决定认证方式 ──
Step '2/6' '认证方式'
$connArgs = @()
$useIntegrated = $false
if ($ConnectionString) {
    $connArgs = @('--connection', $ConnectionString)
    Ok '手动 -ConnectionString'
}
elseif ($NacosSqlAuth) {
    Ok 'Nacos 下发的 SQL 账号(需其自身有 DDL 权限)'
}
else {
    $useIntegrated = $true
    Ok 'Windows/AD 集成认证(应用把 Nacos 连接改写为 Integrated Security,用当前 kinit 身份)'
}

# ── [3] Kerberos 票据(集成认证需要)──
Step '3/6' 'Kerberos 票据'
if ($useIntegrated) {
    $klist = (klist 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($klist)) {
        Write-Host '    ⚠ 未检测到 Kerberos 票据。请先: kinit <你的AD账号>@<REALM>' -ForegroundColor Yellow
    }
    else {
        Ok '已有 Kerberos 票据'
        ($klist -split "`n" | Where-Object { $_ -match '(?i)principal|@' } | Select-Object -First 2) | ForEach-Object { Info $_.Trim() }
    }
}
else { Info '非集成认证路径,跳过' }

# ── [4] 备份确认 ──
Step '4/6' '备份确认'
if (-not $SkipConfirm) {
    Write-Host "    ⚠️  即将把未决迁移应用到【$Environment】数据库,请确认已【备份】。" -ForegroundColor Yellow
    $c = Read-Host '    已备份并确认继续? 键入 YES'
    if ($c -ne 'YES') { Die '已取消(未输入 YES)。' }
}
Ok '已确认'

# ── 环境变量仅本进程临时设置;try/finally 恢复(不污染你的 shell)──
$oldEnv = $env:ASPNETCORE_ENVIRONMENT
$oldMig = $env:MIGRATION_INTEGRATED_AUTH
try {
    $env:ASPNETCORE_ENVIRONMENT = $Environment
    if ($useIntegrated) { $env:MIGRATION_INTEGRATED_AUTH = '1' }  # 触发 Program.cs 把连接改写为集成认证

    Step '5/6' '迁移清单 + 应用(list → database update)'
    dotnet ef migrations list --project $Project --startup-project $Project @connArgs
    if ($LASTEXITCODE -ne 0) { Die 'migrations list 失败:多为【连不上库 / Kerberos 认证失败 / SPN 解析不到】。检查 kinit 票、账号能否登录该库、Server 是否 FQDN。' }
    Ok 'migrations list 成功(连接 + 认证 OK)'

    dotnet ef database update --project $Project --startup-project $Project @connArgs
    if ($LASTEXITCODE -ne 0) { Die 'database update 失败:若报 Error 1088(对象不存在/无权限),多为【该账号无 DDL 权限】。请用有 db_ddladmin/db_owner 的域账号。修复后可重跑(幂等);必要时还原备份。' }

    Step '6/6' '完成'
    Ok "迁移完成($Environment)。"
}
finally {
    if ($null -eq $oldEnv) { Remove-Item Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue } else { $env:ASPNETCORE_ENVIRONMENT = $oldEnv }
    if ($null -eq $oldMig) { Remove-Item Env:\MIGRATION_INTEGRATED_AUTH -ErrorAction SilentlyContinue } else { $env:MIGRATION_INTEGRATED_AUTH = $oldMig }
}
