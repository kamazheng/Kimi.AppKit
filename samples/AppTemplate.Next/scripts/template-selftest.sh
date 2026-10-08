#!/bin/sh
# 模板自检：验证「这个模板现在还能派生出一个可编译、测试通过的新项目」。
#
# 为什么需要它：模板的价值全在「拿来就能用」，而这一点极易在日常改动中悄悄失效 ——
# 某个 csproj 引用了被删掉的东西、CPM 漏了包、测试依赖了不该依赖的环境……
# 这些在模板仓库自身 build 时都不会暴露（仓库内走 ProjectReference），
# 只有真正走一遍「打包 → dotnet new 生成 → 只靠包还原」才发现。
#
# 流程（全部在临时目录，不污染全局 dotnet new 模板列表）：
#   1. dotnet pack Kimi.AppKit.slnx → 临时本地 feed（含 9 个 nupkg）
#   2. 用 --debug:custom-hive 隔离安装模板包，dotnet new kimiapp -n CiProbe 生成
#   3. 断言无旧项目名 KMoldApp 残留
#   4. NuGet.config 只留「临时 feed + nuget.org」，restore / build / test 生成项目
#
# 用法（在 Kimi.AppKit 仓库内任意位置）：  sh samples/AppTemplate.Next/scripts/template-selftest.sh
# 退出码非 0 即自检失败。CI 与本地提交前都可直接调用。

set -eu

PROBE_NAME="${TEMPLATE_SELFTEST_NAME:-CiProbe}"
SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH= cd -- "$SCRIPT_DIR/../../.." && pwd -P)"

if [ ! -f "$REPO_ROOT/Kimi.AppKit.slnx" ]; then
    echo "!!! 找不到 $REPO_ROOT/Kimi.AppKit.slnx：本脚本只能在 Kimi.AppKit 仓库内运行" >&2
    exit 2
fi

WORK_DIR="$(mktemp -d)"
FEED="$WORK_DIR/feed"
HIVE="$WORK_DIR/hive"
OUT="$WORK_DIR/$PROBE_NAME"

# 无论成功失败都清理临时目录，避免 CI runner 上堆积
cleanup() { rm -rf "$WORK_DIR"; }
trap cleanup EXIT

echo "==> 模板自检开始"
echo "    仓库：$REPO_ROOT"
echo "    工作区：$WORK_DIR"

echo "==> [1/4] 打包全部 nupkg → $FEED"
mkdir -p "$FEED"
dotnet pack "$REPO_ROOT/Kimi.AppKit.slnx" -c Release -o "$FEED" --nologo -v q

TEMPLATE_PKG="$(ls "$FEED"/Kimi.AppKit.Templates.*.nupkg | head -1)"
echo "    模板包：$TEMPLATE_PKG"

echo "==> [2/4] 安装模板包（隔离 hive）并生成 $PROBE_NAME"
dotnet new install "$TEMPLATE_PKG" --debug:custom-hive "$HIVE" >/dev/null
dotnet new kimiapp -n "$PROBE_NAME" -o "$OUT" --skipRestore true --debug:custom-hive "$HIVE"

echo "==> [3/4] 断言无旧项目名残留"
if grep -rl "KMoldApp" "$OUT" 2>/dev/null | head -1 | grep -q .; then
    echo "!!! 生成项目里仍存在旧项目名 KMoldApp："
    grep -rl "KMoldApp" "$OUT" 2>/dev/null | head -10
    exit 1
fi

echo "==> [4/4] 只靠包还原、编译、测试"
cat > "$OUT/NuGet.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="selftest-feed" value="$FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
XML
cd "$OUT"
SLN="${PROBE_NAME}.sln"
dotnet restore "$SLN" --configfile ./NuGet.config
dotnet build "$SLN" --no-restore
# 依赖数据库的集成测试在未设连接串时自动跳过（见 EnvFactAttribute），此处不注入连接串，
# 故本步只跑纯逻辑用例 —— 自检要的是「模板结构完好」，不是「数据库连得上」。
dotnet test "$SLN" --no-build

echo "==> 模板自检通过"
