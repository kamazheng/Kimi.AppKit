#!/usr/bin/env bash
# 重新生成 THIRD-PARTY-NOTICES.md，并校验所有依赖许可都在 .config/allowed-licenses.json 白名单内。
# 名单外的许可 -> nuget-license 非 0 退出（CI 失败）。⚠️ 不要为了让 CI 变绿而往白名单里加条目：
# 出现名单外许可必须先停下评估（能否商业分发、是否传染），再由人决定。
# license-overrides.json 只用于「包把许可写成文件/过时 URL、工具识别不出 SPDX」的包，
# 每条都已人工核对包内 LICENSE 文本（Hangfire 为 LGPL-3.0-or-later / 商业多许可，我们按 LGPL 使用并以 NuGet 动态链接）。
#
# 白名单里的非 SPDX 条目 `MS-.NET-Library`（主会话裁定，理由写在这里，因为 JSON 不能带注释）：
#   Microsoft.NETCore.Platforms 1.1.0 / NETStandard.Library 1.6.1 随包带的是 Microsoft .NET Library License
#   （dotnet_library_license.txt，http://go.microsoft.com/fwlink/?LinkId=329770），SPDX 没有标准 ID，故用自定义标识。
#   准入理由：Microsoft 自有；许可明确允许随应用再分发 Distributable Code；仅为经 Hangfire.Console 1.4.3 传入的
#   构建期/运行期传递依赖；不含 copyleft。
set -euo pipefail
cd "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"

dotnet tool restore >/dev/null
dotnet restore Kimi.AppKit.slnx >/dev/null
body="$(mktemp)"
trap 'rm -f "$body"' EXIT
dotnet nuget-license -i Kimi.AppKit.slnx -t -exclude-projects '*Tests*' \
  -a .config/allowed-licenses.json -override .config/license-overrides.json \
  -o Markdown -fo "$body"

{
  echo "# Third-Party Notices"
  echo
  echo "Kimi.AppKit 各包（含传递依赖与构建期工具）使用的第三方组件及其许可。"
  echo "由 \`scripts/generate-third-party.sh\` 生成（nuget-license 4.0.18），**请勿手工编辑**；CI 会重新生成并要求无 diff。"
  echo
  echo "**自定义许可标识**：表中 \`MS-.NET-Library\` = *Microsoft .NET Library License*（SPDX 无标准 ID），"
  echo "许可文本：<http://go.microsoft.com/fwlink/?LinkId=329770>。适用于 Microsoft.NETCore.Platforms 1.1.0 与 NETStandard.Library 1.6.1"
  echo "（经 Hangfire.Console 1.4.3 传入）；Microsoft 自有，允许随应用再分发 Distributable Code，不含 copyleft。"
  echo "Hangfire 系列为 LGPL-3.0-or-later / 商业多许可，本项目按 LGPL 经 NuGet 动态链接使用。"
  echo
  cat "$body"
} > THIRD-PARTY-NOTICES.md
echo "THIRD-PARTY-NOTICES.md 已生成"
