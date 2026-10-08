#!/usr/bin/env bash
# 死选项扫描：找出 Options 类（按 `class|record XxxOptions` 声明识别，不依赖文件名）里
# 「public 可写属性（get; set/init;）」或「getter-only 且带初始化器的集合属性」，
# 在 Options 类自身之外没有任何「成员访问（.Prop）」或「初始化器（Prop = …）」用法的项。
# 用法：
#   scripts/dead-options-scan.sh [扫描根目录=src]   发现死选项 -> 退出码 1
#   scripts/dead-options-scan.sh --self-test        对夹具自检：good 必须 0，每个 bad* 必须 1 且报出 expected.txt 里的全部项
# 允许名单：scripts/dead-options-allowlist.txt（每行 `类名.属性名  # 理由`）。⚠️ 真死选项应删除，不得登记。
# 判定细节：
#   · 只统计 *.cs / *.razor；整行注释（// 或 ///）不算引用；属性所在 Options 类的行区间不算引用
#     （同文件里的其他代码算，所以嵌在 Setup 文件里的 Options 类也能正确判定）。
#   · 局限：按成员名匹配、不做语义分析——别的类型上的同名成员访问（foo.Enabled）会掩盖死选项（宁漏勿误报）。
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/.." && pwd -P)"
allowlist="$script_dir/dead-options-allowlist.txt"

# 输出：类名|属性名|文件|类起始行|类结束行
list_props() {
  awk '
    function strip(s) { gsub(/"[^"]*"/, "", s); sub(/\/\/.*/, "", s); return s }
    {
      raw = $0; code = strip(raw)
      if (!incls && match(code, /(class|record)[ \t]+(struct[ \t]+)?[A-Za-z0-9_]*Options([^A-Za-z0-9_]|$)/)) {
        t = substr(code, RSTART, RLENGTH); sub(/^(class|record)[ \t]+(struct[ \t]+)?/, "", t); sub(/[^A-Za-z0-9_]$/, "", t)
        cls = t; incls = 1; depth = 0; started = 0; start = FNR; n = 0
      }
      if (incls) {
        if (depth == 1 && code ~ /^[ \t]*public[ \t][^(=]*[ \t][A-Za-z0-9_]+[ \t]*\{[ \t]*get;/ ) {
          writable = (code ~ /get;[ \t]*(set|init);/); init = (code ~ /\}[ \t]*=([^>]|$)/)
          if (writable || init) {
            p = code; sub(/[ \t]*\{.*/, "", p); sub(/.*[ \t]/, "", p)
            props[++n] = p
          }
        }
        o = gsub(/\{/, "{", code); c = gsub(/\}/, "}", code)
        depth += o - c; if (o > 0) started = 1
        if (started && depth <= 0) {
          for (i = 1; i <= n; i++) print cls "|" props[i] "|" FILENAME "|" start "|" FNR
          incls = 0
        }
      }
    }' "$1"
}

count_uses() { # root prop def_file start end
  local root="$1" prop="$2" def="$3" s="$4" e="$5" total=0 f n
  local pat="(\\.${prop}([^A-Za-z0-9_]|\$))|((^|[^A-Za-z0-9_.])${prop}[ \t]*=([^=>]|\$))"
  while IFS= read -r f; do
    if [ "$f" = "$def" ]; then
      n="$(grep -nE "$pat" "$f" | grep -vE '^[0-9]+:[ \t]*//' | awk -F: -v s="$s" -v e="$e" '$1 < s || $1 > e' | wc -l | tr -d ' ')" || n=0
    else
      n="$(grep -E "$pat" "$f" | grep -vcE '^[ \t]*//')" || n=0
    fi
    total=$((total + n))
  done < <(find "$root" \( -name '*.cs' -o -name '*.razor' \) -not -path '*/obj/*' -not -path '*/bin/*')
  echo "$total"
}

scan() { # root -> 退出码 1 表示有死选项；死项以「类.属性」逐行写到 stdout（供自检断言），说明写 stderr
  local root="$1" dead=0 file cls prop def s e uses
  while IFS= read -r file; do
    while IFS='|' read -r cls prop def s e; do
      if [ -f "$allowlist" ] && grep -qE "^${cls}\.${prop}([[:space:]]|$)" "$allowlist"; then continue; fi
      uses="$(count_uses "$root" "$prop" "$def" "$s" "$e")"
      if [ "$uses" -eq 0 ]; then
        echo "${cls}.${prop}"
        echo "死选项：${cls}.${prop} (${def}) 在 Options 类之外没有成员访问或初始化器用法" >&2
        dead=1
      fi
    done < <(list_props "$file")
  done < <(grep -rlE --include='*.cs' '(class|record)[[:space:]]+(struct[[:space:]]+)?[A-Za-z0-9_]*Options([^A-Za-z0-9_]|$)' "$root" | grep -vE '/(obj|bin)/' | sort)
  return "$dead"
}

if [ "${1:-}" = "--self-test" ]; then
  fx="$repo_root/tests/fixtures/dead-options"
  rc=0; scan "$fx/good" >/dev/null || rc=$?
  [ "$rc" -eq 0 ] || { echo "自检失败：good 夹具应退出 0，实际 $rc" >&2; exit 2; }
  for d in "$fx"/bad*/; do
    rc=0; out="$(scan "$d" 2>/dev/null)" || rc=$?
    [ "$rc" -eq 1 ] || { echo "自检失败：$(basename "$d") 应退出 1，实际 $rc" >&2; exit 2; }
    while IFS= read -r want; do
      grep -qxF "$want" <<<"$out" || { echo "自检失败：$(basename "$d") 未报出 $want" >&2; exit 2; }
    done < "$d/expected.txt"
  done
  echo "dead-options-scan 自检通过（good=0；bad*=1 且报出 expected.txt 全部项）"
  exit 0
fi

scan "${1:-$repo_root/src}" >/dev/null
