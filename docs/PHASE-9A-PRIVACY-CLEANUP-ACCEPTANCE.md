# Phase 9A：GitHub 完整历史隐私清理验收报告

- 日期：2026-11-19（Asia/Shanghai）
- 仓库：<https://github.com/x549d/context-notes-native>
- 重写前基线：`main` = `1192cdd154421502e94503567588f64d3334343a`（62 个提交）、本地分支 `wip/phase-2r-attempt` = `8bd3f77d75d5e2a30561655e35f68d7109834a61`（1 个独立提交）、标签 `v0.3.0` = `f324af4f425a04c79c865f6c63ee45bf8687f0a8`（指向 `fa2aa3c0f81d77410e11b66ebf36264851bbd4dd`）
- 重写后基线：`main` = `96286e8eb34b56c25281eaf9eaa45a654a2e78db`（62 个提交）、标签 `v0.3.0` = `122a4a4b62df7dcf0e137ec4462023b748676cef`（指向 `a26c33d6157ebc62b8634142f8c0ef0203958683`）
- 维护者公开提交标识：`x549d <348141032+x549d@users.noreply.github.com>`（数字 ID `348141032` 经 GitHub API `users/x549d` 核实并由维护者明确确认）

---

## 1. 隐私清理验收总表

| 检查项目 | 验收要求 | 实测结果 | 状态 |
| --- | --- | --- | --- |
| Author 邮箱 | 不再出现个人邮箱 | 全历史 62/62 提交唯一 Author 为 `x549d <348141032+x549d@users.noreply.github.com>`（0 处个人邮箱） | **PASS** |
| Committer 邮箱 | 不再出现个人邮箱 | 全历史 62/62 提交唯一 Committer 为 `x549d <348141032+x549d@users.noreply.github.com>`（0 处个人邮箱） | **PASS** |
| Windows 用户名 | 0 命中 | 全部 567 个历史 Git Blob、62 条提交消息、1 个附注标签与发布产物均为 **0 命中** | **PASS** |
| 本机绝对路径 | 0 命中 | 全部历史 Blob 中本机用户目录与开发盘绝对路径均为 **0 命中**（仅保留合成单元测试字符串 `C:\\Users\\test` 与 Windows 标准常量） | **PASS** |
| 提交消息 | 0 隐私命中 | 全部 62 条提交消息经个人邮箱、本机用户名、主机名与绝对路径正则扫描均为 **0 命中** | **PASS** |
| Git Tag | 0 隐私命中 | `refs/tags/v0.3.0` Tagger 已更新为 `x549d <348141032+x549d@users.noreply.github.com>`，标签注释无隐私信息 | **PASS** |
| Git 完整历史 | 0 隐私命中 | 仅保留 `refs/heads/main` 与 `refs/tags/v0.3.0`；已移除废弃分支 `wip/phase-2r-attempt` 并执行 `reflog expire` 与 `gc --prune=now --aggressive` | **PASS** |
| DLL / EXE | 不包含个人构建路径 | 重建的 `SelectionProbe.dll` PE 调试目录仅含 `IMAGE_DEBUG_TYPE_REPRO` (`Type=16, SizeOfData=0`)，无 CodeView RSDS / `.pdb` 路径；UTF-8/UTF-16LE 隐私扫描 **0 命中** | **PASS** |
| ZIP | 不包含个人数据 | 重建的 `ContextNotesNative-0.3.0-portable.zip`（27 个条目）含 0 个 `.pdb`、0 个 SQLite 数据库、0 处本机路径或个人邮箱 | **PASS** |
| Gitleaks | 0 未解决泄露 | `gitleaks 8.30.1` 扫描重写后全历史（62 提交 / 1.04 MB）与重建发布包均返回 **0 leaks found**（退出码 `0`） | **PASS** |
| 正式 Release | 正常可下载 | 已基于重写后 CLEAN 提交 `a26c33d` 重新构建并通过 13/13 发布审计、29/29 安装器测试、19/19 便携测试；待仓库恢复 Public 并更新 Release 资产后进行公网 API 下载复核 | **READY FOR PUBLIC VERIFY** |

---

## 2. 执行步骤与完整性证据

### 2.1 外部完整备份（项目目录之外）

在任何历史改写之前，已将完整备份保存在项目目录之外的独立本地备份目录（绝不提交或上传至 GitHub）：

- 完整裸镜像仓库（`git clone --mirror`）与全引用打包文件（`git bundle create --all`），完整保留改写前的 `main`、`wip/phase-2r-attempt` 与 `v0.3.0`。
- 重写前的本地/远端 refs 清单、全部 63 条原始提交元数据、`v0.3.0` 原始标签对象与文件树清单。
- GitHub 仓库元数据与原 `v0.3.0` Release 元数据及全部 3 个已校验发布包文件（`setup.exe`、`portable.zip`、`SHA256SUMS`）。

### 2.2 仓库临时私有保护与历史改写

1. **临时 Private 保护**：经维护者在 GitHub 设置页将仓库切换为 Private，并通过未登录 GitHub REST API 核实返回 `HTTP 404` 后，才执行历史重写与受控推送。
2. **废弃分支处置**：本地未推送的历史存档分支 `wip/phase-2r-attempt`（`8bd3f77`）已在外部完整镜像和 bundle 中归档，随后从工作仓库中删除，确保公开仓库不再携带包含旧元数据的废弃分支。
3. **历史重写工具与规则**：使用 `git-filter-repo 2.47.0` 配合 `--mailmap` 与 `--replace-text`：
   - 将全部 62 个提交与附注标签 `v0.3.0` 的 Author / Committer / Tagger 邮箱替换为 `348141032+x549d@users.noreply.github.com`。
   - 将历史文档与早期构建脚本中的本机用户路径替换为 `%USERPROFILE%\`，将本机开发目录与测试临时目录路径替换为 `<repo-root>`、`<local-workspace>`、`<local-temp>`、`<local-vmware-dir>`、`<local-vm-dir>` 等通用占位符，同时完整保留 Phase 0–8 的工程开发记录。
   - 同步在本地仓库配置 `git config --local user.name x549d` 与 `git config --local user.email 348141032+x549d@users.noreply.github.com`，防止后续提交再次写入个人邮箱。
4. **本地垃圾回收与受控强推**：
   - 执行 `git reflog expire --expire=now --all` 与 `git gc --prune=now --aggressive` 清理本地不可达旧对象。
   - 仅强制推送清理后的 `refs/heads/main`（`96286e8`）与 `refs/tags/v0.3.0`（`122a4a4`）至私有远端 `origin`，通过 `git ls-remote origin` 核实远端不存在任何 `wip/` 或多余引用。

### 2.3 新旧文件树差异核验（无代码误删证明）

对比备份镜像中的改写前 HEAD（`1192cdd`）与改写后 HEAD（`96286e8`）：

- 改写前文件总数：**111**；改写后文件总数：**111**（新增 0，删除 0）。
- 发生变化的文件共 **16 个**，全部为 `docs/` 目录下的历史阶段文档与安全审计文档（脱敏本机路径）。
- `src/`、`tests/`、`scripts/`、`installer/`、`codex-plugin/`、`LICENSE`、`README.md`、`PRIVACY.md`、`SECURITY.md`、`CHANGELOG.md` 均 **100% 字节一致**，MIT 许可证完整保留。

### 2.4 隐私清理后重构建发布产物与二进制深度审计

基于清理后的 `v0.3.0` 源码提交 `a26c33d6157ebc62b8634142f8c0ef0203958683`（使用 `-p:DebugType=none` 与中完整性官方 Inno Setup 6.7.4）重新构建并通过全部自动化门禁：

| 产物文件 | 大小（字节） | SHA-256 |
| --- | ---: | --- |
| `ContextNotesNative-0.3.0-setup.exe` | 2,292,674 | `62DC20CB3005F52533F3656FDEB199DEF775F31DE66B9A1080DB1BC2B37B0E15` |
| `ContextNotesNative-0.3.0-portable.zip` | 244,302 | `91805AC7A61E5D5A02C906F95C90323D0593BC0A6227820C08717D4B12CA9427` |

- **PE 调试目录核查**：
  - `SelectionProbe.dll` 调试目录仅包含 1 个 `Type=16`（`IMAGE_DEBUG_TYPE_REPRO` 确定性构建标记，`SizeOfData=0`），**0 个** CodeView RSDS / `.pdb` 路径项。
  - `SelectionProbe.exe` 为 .NET 10 官方标准 `apphost` 模板，仅含 Microsoft Azure Pipelines 标准构建路径，不含本机用户名或工作区路径。
- **发布包完整性与安装门禁**：
  - `dotnet test -c Release`：**106 / 106 PASS**
  - `SelectionProbe.dll --self-test`：**80 / 80 PASS**
  - `scripts/Test-Release.ps1`：**13 / 13 PASS**
  - `scripts/Test-StandardInstaller.ps1`：**29 / 29 PASS**（隔离 `/TESTROOT`）
  - `scripts/Test-Installation.ps1`：**19 / 19 PASS**（隔离临时目录）
- **生产用户数据库保护核验**：只读检查 `%USERPROFILE%\.context-notes-native\data\history.db`，清理前后均为 `schema=2`、`annotations=20`、`messages=110`、`integrity_check=ok`，全程未写入、未迁移、未删除。

---

## 3. 已知局限与 GitHub 缓存清理建议

由于仓库在 Phase 9A 清理之前曾短暂处于 Public 状态，必须如实说明以下技术现实：

1. **GitHub 服务端对象缓存与视图引用**：强制推送会更新公开分支与标签指向，但若有人持有旧 Commit SHA 的直链，GitHub 服务端在自动 GC 完成之前可能仍暂时保留未引用的旧对象缓存。
2. **第三方 Clone / Fork / 外部镜像**：若在私有化之前已有外部用户执行了 `git clone`、创建了 Fork 或被第三方索引抓取，本地重写历史无法远程删除已存在于第三方机器上的副本。
3. **如确需请求 GitHub Support 立即清理未引用旧对象缓存**，维护者可在 <https://support.github.com/> 提交工单（选择 *Remove sensitive data from a repository* / *Clear cached Git objects*），参考模板：

```text
Subject: Request to purge unreachable cached Git commits after history rewrite (x549d/context-notes-native)

Hello GitHub Support,

I am the owner of the repository https://github.com/x549d/context-notes-native.
I have rewritten the Git history using git-filter-repo to remove my personal email address and local workstation paths from historical commits and tags, and force-pushed the cleaned `main` branch and `v0.3.0` tag.

Could you please purge any unreachable/dangling cached Git objects and commit views referencing the old history (prior HEAD 1192cdd154421502e94503567588f64d3334343a and prior tag object f324af4f425a04c79c865f6c63ee45bf8687f0a8) from GitHub's server-side cache?

Thank you!
```
