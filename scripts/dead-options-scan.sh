#!/usr/bin/env bash
# 死选项扫描：找出 *Options.cs 中「public 可写属性（get; set/init;）」且除定义行外全仓 0 引用的项。
# 用法：
#   scripts/dead-options-scan.sh [扫描根目录=src]   发现死选项 -> 退出码 1
#   scripts/dead-options-scan.sh --self-test        对夹具自检：bad 必须报错(1)，good 必须通过(0)
# 允许名单：scripts/dead-options-allowlist.txt（每行 `类名.属性名  # 理由`，# 开头为注释）。
# 局限：按「属性名整词」匹配，通用名（如 Enabled）可能被他处同名词掩盖——宁可漏报不误报。
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/.." && pwd -P)"
allowlist="$script_dir/dead-options-allowlist.txt"

scan() {
  local root="$1" dead=0 file cls prop line count
  while IFS= read -r file; do
    cls="$(basename "$file" .cs)"
    while IFS= read -r line; do
      prop="$(sed -E 's/^.*[[:space:]]([A-Za-z_][A-Za-z0-9_]*)[[:space:]]*\{[[:space:]]*get;[[:space:]]*(set|init);.*$/\1/' <<<"$line")"
      if [ -f "$allowlist" ] && grep -qE "^${cls}\.${prop}([[:space:]]|$)" "$allowlist"; then continue; fi
      # 全部命中数 - 定义行（每个属性在其文件里恰好定义一次）
      count="$(grep -rwE --include='*.cs' --include='*.razor' "$prop" "$root" | wc -l | tr -d ' ')"
      if [ "$count" -le 1 ]; then
        echo "死选项：${cls}.${prop} (${file}) 除定义处外无引用" >&2
        dead=1
      fi
    done < <(grep -E '^[[:space:]]*public[[:space:]].*\{[[:space:]]*get;[[:space:]]*(set|init);' "$file" || true)
  done < <(find "$root" -name '*Options.cs' -not -path '*/obj/*' -not -path '*/bin/*' | sort)
  return "$dead"
}

if [ "${1:-}" = "--self-test" ]; then
  fx="$repo_root/tests/fixtures/dead-options"
  rc=0; scan "$fx/bad" 2>/dev/null || rc=$?
  [ "$rc" -eq 1 ] || { echo "自检失败：bad 夹具应退出 1，实际 $rc" >&2; exit 2; }
  rc=0; scan "$fx/good" || rc=$?
  [ "$rc" -eq 0 ] || { echo "自检失败：good 夹具应退出 0，实际 $rc" >&2; exit 2; }
  echo "dead-options-scan 自检通过（bad=1, good=0）"
  exit 0
fi

scan "${1:-$repo_root/src}"
