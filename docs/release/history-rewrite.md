# 公开前历史重写：演练报告（非破坏）

> 2026-10-08，线 A4。**只在 `/tmp` 的镜像副本上演练，未 push 任何产物，未对本仓或其 origin 执行 filter-repo。**
> 真正执行是闸门 G1（主会话触发，见 `kmold-hub/.s1/A4.md` 的命令块）。

## 为什么重写、为什么推新仓

- 旧样例 `samples/AppTemplate`（329 文件）含内部主机名、内部命名空间配置、DataProtection 密钥 XML；历史里另有文档/注释中的内部主机名。
- 只删当前树没用：旧提交仍可达。直接改可见性也不行：GitHub 对象存储与 Actions 记录仍可按 SHA 访问旧提交。
  所以做法是重写后推到**新建**仓，旧仓改名归档并保持 private。

## 工具与版本

| 工具 | 版本 |
|---|---|
| git-filter-repo | `a40bce548d2c`（`git filter-repo --version`） |
| gitleaks | `docker.io/zricethezav/gitleaks:v8.24.3`（podman，`--tls-verify=false` 拉取） |
| 规则文件 | `docs/release/history-rewrite-rules.txt`（同目录入库） |
| gitleaks 配置 | `docs/release/gitleaks.toml`（默认规则 + 1 条已确认误报的行级放行） |
| 计数脚本 | `docs/release/history-counts.sh <镜像仓路径>` |

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
  [--mailmap <mailmap 文件>]        # 见「待用户拍板」

# 2) 计数
<仓>/docs/release/history-counts.sh /tmp/appkit-rewrite/after.git

# 3) gitleaks（注意 macOS 上 podman 只共享 /private/tmp，不是 /tmp 的别名路径之外的目录）
podman run --rm -v /private/tmp/appkit-rewrite/after.git:/repo:ro -v /private/tmp/appkit-rewrite:/out \
  docker.io/zricethezav/gitleaks:v8.24.3 git /repo --no-banner --redact \
  --config /out/gitleaks.toml --report-format json --report-path /out/gitleaks-after.json
```

## 结果（演练对象：本分支 + 其余本地分支与 tag，共 6 个 ref）

| 指标 | 重写前 | 重写后 | 验收 |
|---|---|---|---|
| 提交数（所有 ref） | 102 | 90 | 12 个提交只改了旧样例路径，被 filter-repo 当作空提交剪掉 |
| 1e-1 `git log --all -p -- 'samples/AppTemplate/*'` 行数 | 1,086,559 | **0** | 0 ✅ |
| 1e-2 `git log --all -p \| grep -ciE '[a-z0-9-]+\.Company\.com'` | 71 | **0** | 0 ✅ |
| 1e-3 gitleaks 全历史发现数（默认规则） | 2 | 1（误报） | 见下 |
| 1e-3 gitleaks 全历史发现数（`gitleaks.toml`） | 2 | **0** | 0 ✅ |
| 辅助：`*.cdu(qa).` 主机行数 | 51 | 0 | |
| 辅助：含 `Company`（不分大小写）的行数 | 5027 | 74 | 74 行全是提交作者邮箱，见下 |
| 重写后 `dotnet test`（分支 `s1/a4-public-hygiene` 检出） | — | 44 + 33 + 289 通过 | 重写不破坏构建 |

gitleaks 说明：
- 重写前 2 条：旧样例里的 DataProtection 密钥 XML（`generic-api-key`，路径移除后消失）与 `docs/hardcode-audit.md:147`。
- 后者是误报：文档里描述配置键 `EmployeeApi:BypassCertificateValidation=true` 被当成 key=value。
  `gitleaks.toml` 用**行级正则**精确放行这一行，不放行整个文件，不依赖提交哈希（重写后哈希会变，`.gitleaksignore` 指纹不可用）。
- 注意 gitleaks 默认规则**并不认** DataProtection 密钥 XML 的结构（只是被 `key id=` 触发）。
  这类文件的清除靠路径移除保证，而不是靠扫描器；1e-1 的路径计数才是权威判据。

## 待用户拍板：提交作者邮箱

历史中 74 个提交（新旧提交合计）的作者/提交者是 `kzheng <kai.zheng@example.com>`——公司邮箱，会随新仓公开。
`--replace-text` 只改内容，不改作者；要处理必须用 `--mailmap`。演练变体（`after-mailmap.git`）：
`kzheng <kamazheng@users.noreply.github.com> <kai.zheng@example.com>`，结果「含 Company 的行数」= 0，提交数同为 90。
是否改写作者、改成什么是署名决定，本线未拍板，G1 前请定。

## 规则的取舍

- `*.example.com` → `internal.example.com`（保留「这是个主机名」的形状，历史 diff 仍可读）；`@example.com`、裸 `example.com` → `example.com`。
- `ApproveCenter` 等带公司名的标识符改为中性名；最后一条 `(?i)Company` → `Company` 兜底。
  兜底会改写历史里「说明性文字」中的公司名（例如旧复盘文档），这是有意的：目标是公开历史里不再出现公司名。
- 规则按顺序执行，先具体后宽泛；改顺序会让兜底吞掉具体规则的匹配。

## G1 正式执行与本演练的差别

- 对象是 A1-A5 全部合入后的 main 的**新克隆**（`git clone --no-local`），不是本演练的镜像；
  提交数与哈希会不同，三项计数必须重跑。
- 重写前先做 bundle 备份并 `git bundle verify`；旧仓改名归档（保持 private），推到新建空仓。
- 演练目录 `/tmp/appkit-rewrite/` 含重写前的完整历史，用完即删。
