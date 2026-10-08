#!/usr/bin/env bash
# 重新生成 THIRD-PARTY-NOTICES.md，并校验所有依赖许可都在 .config/allowed-licenses.json 白名单内。
# 名单外的许可 -> nuget-license 非 0 退出（CI 失败）。⚠️ 不要为了让 CI 变绿而往白名单里加条目：
# 出现名单外许可必须先停下评估（能否商业分发、是否传染），再由人决定。
# license-overrides.json 只用于「包把许可写成文件/过时 URL、工具识别不出 SPDX」的包，
# 每条都已人工核对包内 LICENSE 文本（Hangfire 为 LGPL-3.0 / 商业双许可，我们按 LGPL 使用并以 NuGet 动态链接）。
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
  cat "$body"
} > THIRD-PARTY-NOTICES.md
echo "THIRD-PARTY-NOTICES.md 已生成"
