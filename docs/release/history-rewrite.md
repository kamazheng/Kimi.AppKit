# 公开前历史重写：演练报告（非破坏）

> 2026-10-08，线 A4。**只在 `/tmp` 的镜像副本上演练，未 push 任何产物，未对本仓或其 origin 执行 filter-repo。**
> 真正执行是闸门 G1（主会话触发，见 `kmold-hub/.s1/A4.md` 的命令块）。

## 为什么重写、为什么推新仓

- 旧样例 `samples/AppTemplate`（329 文件）含内部主机名、内部配置中心命名空间、DataProtection 密钥 XML；历史里另有文档/注释中的内部主机名。
- 只删当前树没用：旧提交仍可达。直接改可见性也不行：GitHub 对象存储与 Actions 记录仍可按 SHA 访问旧提交。
  所以做法是重写后推到**新建**仓，旧仓改名归档并保持 private。

## 工具与版本

| 工具 | 版本 |
|---|---|
| git-filter-repo | `a40bce548d2c`（`git filter-repo --version`） |
| gitleaks | `docker.io/zricethezav/gitleaks:v8.24.3`（podman，`--tls-verify=false` 拉取） |
| 规则文件 | `docs/release/history-rewrite-rules.txt`（同目录入库；**不得写注释**，见「规则的取舍」） |
| mailmap 生成 | `docs/release/history-mailmap.sh <镜像仓>`（运行时生成，不入库静态表） |
| gitleaks 配置 | `docs/release/gitleaks.toml`（默认规则 + 1 条已确认误报的行级放行） |
| 计数脚本 | `docs/release/history-counts.sh <镜像仓路径>`（任一计数非 0 则退出 1） |

## 演练步骤（可原样重放）

```bash
# 0) 镜像副本（不动原仓）；filter-repo 拒绝非「全新克隆」，所以第二步必须 --no-local
git clone --mirror <本仓路径> /tmp/appkit-rewrite/before.git
git clone --mirror --no-local /tmp/appkit-rewrite/before.git /tmp/appkit-rewrite/after.git

# 1) 重写：旧样例路径整体移除 + 文本规则同时作用于文件内容与提交说明
cd /tmp/appkit-rewrite/after.git
git filter-repo --path samples/AppTemplate --invert-paths \
  --replace-text  <仓>/docs/release/history-rewrite-rules.txt \
  --replace-message <仓>/docs/release/history-rewrite-rules.txt \
  --mailmap <history-mailmap.sh 的输出>

# 2) 计数
<仓>/docs/release/history-counts.sh /tmp/appkit-rewrite/after.git

# 3) gitleaks（macOS 上 podman 只共享 /private/tmp，挂载路径要写 /private/tmp/...）
podman run --rm -v /private/tmp/appkit-rewrite/after.git:/repo:ro -v /private/tmp/appkit-rewrite:/out \
  docker.io/zricethezav/gitleaks:v8.24.3 git /repo --no-banner --redact \
  --config /out/gitleaks.toml --report-format json --report-path /out/gitleaks-after.json
```

## 结果（演练对象：本分支所在本地副本，含 4 个本地分支与 1 个 tag；94/106 为含本线新提交后的数）

| 指标 | 重写前 | 重写后 | 验收 |
|---|---|---|---|
| 提交数（所有 ref） | 106 | 94 | 纯旧样例路径的提交被当作空提交剪掉 |
| 1e-1 路径 `-p` 行数 | 1,086,559 | **0** | 0 ✅ |
| 1e-2 `*.<源公司域名>` 行数 | 71 | **0** | 0 ✅ |
| 1e-3 gitleaks 全历史（`gitleaks.toml`） | 2 | **0** | 0 ✅（默认规则下剩 1 误报，见下） |
| 辅助：含源公司名的行数（不分大小写） | 5069 | 0 | |
| 辅助：作者/提交者含源公司名 | 89 | 0 | mailmap 生效 |
| 辅助：中文全称/内部 GitLab 组/内网网段残留 | 4016 | 0 | |
| 辅助：`*.cdu(qa).` 主机行数 | 51 | 0 | |
| 重写后检出 `dotnet test` | — | 通过（前一轮演练实测：44+33+289） | |

gitleaks：重写前 2 条为旧样例 DataProtection 密钥 XML（路径移除后消失）与 `docs/hardcode-audit.md:147` 的误报
（配置键 `EmployeeApi:BypassCertificateValidation=true` 被当 key=value）。`gitleaks.toml` 用行级正则精确放行，
不依赖提交哈希。gitleaks 并不识别 DataProtection 密钥 XML 的结构，该文件的清除靠路径移除保证，1e-1 才是权威判据。

## 作者邮箱（已裁决）

历史中作者/提交者含源公司邮箱的提交一律用 `--mailmap` 映射为 `kzheng <kamazheng@users.noreply.github.com>`
（与较新提交一致）。映射表由 `history-mailmap.sh` 在重写时从历史里生成，不入库静态文件，
否则旧邮箱字面量会让 `git grep` 自命中。**原始身份保留在私有归档仓（旧仓改名）与 bundle 备份中。**

## 规则的取舍

- `--replace-text` / `--replace-message` 的规则文件**不支持 `#` 注释**：每个非空行都是一条规则，
  注释行会被当成字面替换（把文字换成 `***REMOVED***`）。所以规则文件里没有任何注释，解释都在本节。
- 规则按顺序执行，先具体后宽泛；兜底规则必须在最后（否则吞掉具体规则的匹配）：
  1. 源公司域名的任意层级子域 → `internal.example.com`；`@域名`、裸域名 → `example.com`。
  2. 带公司名的标识符（审批中心类名、OIDC scheme 名等）→ 中性名。
  3. 内部 GitLab 组名、`Company-` 前缀、公司中文名（全称与简称）、「公司 Connector (Chengdu)」类英文全称、内网网段 → 中性/占位值（网段用 RFC 5737 的 `192.0.2.0`）。
  4. 兜底：不分大小写的公司名 → `Company`（有意改写历史说明性文字里的公司名）。
  5. 其余 `*.cdu(qa).*` 内部主机 → `internal.example.com`。
- 规则文件自身的模式写成字符类（如 `[m]olex`、`莫[仕]`），避免规则文件被自己的规则改写、也避免当前树 `git grep` 自命中。

## G1 正式执行与本演练的差别

- 对象是 A1-A5 全部合入后、**从 GitHub origin 新克隆**的仓（不从本地仓克隆：本地仓含 `s1/*` 本地分支，`push --mirror` 会全推）。
  克隆后只保留 main 与 tag 再推；提交数与哈希会不同，计数必须重跑，`history-counts.sh` 必须退出 0。
- 重写前先做 bundle 备份并 `git bundle verify`；旧仓改名归档（保持 private），推到新建空仓。
- G1 之后：用新仓重新克隆本地仓与各 worktree，旧本地仓改名保留，防止旧历史被推进新仓。
- 演练目录 `/tmp/appkit-rewrite/` 含重写前的完整历史，用完即删。
