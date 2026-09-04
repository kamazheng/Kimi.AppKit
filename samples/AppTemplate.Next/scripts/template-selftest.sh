#!/bin/sh
# 模板自检：验证「这个模板现在还能派生出一个可编译、测试通过的新项目」。
#
# 为什么需要它：模板的价值全在「拿来就能用」，而这一点极易在日常改动中悄悄失效 ——
# 改名脚本漏掉某类文件、某个 csproj 引用了被删掉的东西、测试依赖了不该依赖的环境……
# 这些在模板仓库自身 build 时都不会暴露，只有真正走一遍派生流程才发现。
#
# 用法：
#   sh scripts/template-selftest.sh            # 完整自检（含改名）
#   sh scripts/template-selftest.sh --no-rename  # 跳过改名，只验证编译与测试
#
# 退出码非 0 即自检失败。CI 与本地提交前都可直接调用。

set -eu

PROBE_NAME="${TEMPLATE_SELFTEST_NAME:-CiProbe}"
DO_RENAME=1
[ "${1:-}" = "--no-rename" ] && DO_RENAME=0

REPO_ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
WORK_DIR="$(mktemp -d)"

# 无论成功失败都清理临时目录，避免 CI runner 上堆积
cleanup() { rm -rf "$WORK_DIR"; }
trap cleanup EXIT

echo "==> 模板自检开始"
echo "    源目录：$REPO_ROOT"
echo "    工作区：$WORK_DIR"

# ── 1. 复制出一份干净副本 ────────────────────────────────────────────────
# ⚠️ 只复制 **git 跟踪的文件**，不要整目录 tar。
#    整目录复制会带上 .gitignore 掉的本地产物（docfx 生成的 wwwroot/doc/*.html、
#    DataProtection 密钥等），它们含旧项目名却又不属于模板内容，会让自检误报。
#    用 git ls-files 得到的正是「别人 clone 下来会拿到什么」——这才是要验证的对象。
#    注：未 git add 的新文件不在其中，与它们不会进入派生项目的事实一致。
echo "==> [1/4] 复制模板副本（仅 git 跟踪的文件）"
( cd "$REPO_ROOT" && git ls-files -z ) \
    | ( cd "$REPO_ROOT" && tar --null -T - -cf - ) \
    | tar -C "$WORK_DIR" -xf -

cd "$WORK_DIR"

# ── 2. 走一遍改名（模板的核心交付路径）─────────────────────────────────
if [ "$DO_RENAME" -eq 1 ]; then
    if command -v pwsh >/dev/null 2>&1; then
        echo "==> [2/4] 执行改名 → $PROBE_NAME"
        pwsh -NoProfile -File ./ChangeProjectName.ps1 -NewName "$PROBE_NAME" -Force

        # 改名后不该再有任何旧名残留（.git 已排除，故此处命中即为真残留）
        OLD_NAME="KMoldApp"
        if grep -rl "$OLD_NAME" . 2>/dev/null | grep -v '^\./\.idea/' | head -1 | grep -q .; then
            echo "!!! 改名后仍存在旧项目名残留："
            grep -rl "$OLD_NAME" . 2>/dev/null | grep -v '^\./\.idea/' | head -10
            exit 1
        fi
        SLN="${PROBE_NAME}.sln"
    else
        echo "==> [2/4] 跳过改名：未找到 pwsh"
        echo "    ⚠️ 本次未验证改名脚本。要完整自检请安装 PowerShell 后重跑。"
        SLN="KMoldApp.sln"
    fi
else
    echo "==> [2/4] 跳过改名（--no-rename）"
    SLN="KMoldApp.sln"
fi

# ── 3. 还原并编译 ───────────────────────────────────────────────────────
echo "==> [3/4] 还原并编译 $SLN"
dotnet restore "$SLN" --configfile ./NuGet.config
dotnet build "$SLN" --no-restore

# ── 4. 测试 ─────────────────────────────────────────────────────────────
# 依赖数据库的集成测试在未设连接串时自动跳过（见 EnvFactAttribute），此处不注入连接串，
# 故本步只跑纯逻辑用例 —— 自检要的是「模板结构完好」，不是「数据库连得上」。
echo "==> [4/4] 运行测试"
dotnet test "$SLN" --no-build

echo "==> 模板自检通过"
