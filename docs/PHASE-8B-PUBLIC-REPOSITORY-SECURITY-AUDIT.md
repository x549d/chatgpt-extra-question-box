# Phase 8B：公开仓库完整敏感数据安全审查报告

- 日期：2026-11-19（Asia/Shanghai）
- 审查基线 Commit：`ca2fc2ace70f886cebe1cd58f0346d1cde9565c5`（以及包含全部 55 个提交的本地镜像，覆盖 `main` 54 个提交与本地分支 `wip/phase-2r-attempt` 1 个提交）
- 审查目标：在修改根 `LICENSE` 及公开 GitHub 仓库之前，对完整 Git 历史、所有分支、当前工作区、脚本/文档、构建与发布产物以及真实用户 SQLite 数据库边界进行全量敏感信息审查。

---

## 1. 审查结论摘要

| 检查维度 | 结果 | 详情 |
| --- | --- | --- |
| 高危凭据与密钥（API Key / Token / JWT / Cookie / 私钥 / 测试凭据） | **PASS（0 命中）** | `gitleaks 8.30.1` 与自定义全量正则/二进制扫描在全部 55 个提交（536 个 Git Blob）、105 个 HEAD 文件、24 个便携 ZIP 内文件及 Phase 7 交付目录中均为 **0 命中**。 |
| 用户数据库与聊天历史泄露检查 | **PASS（0 命中）** | Git 历史、工作区及发布包内均无 `.db`/`.sqlite`/`.db-wal`/`.db-shm` 文件或 `SQLite format 3` 二进制头；无 `auth.json`、`config.toml`、`.log`、`.pdb` 或用户对话正文。 |
| 真实生产 SQLite 数据库保护核验 | **PASS（完整且隔离）** | 只读核验 `%USERPROFILE%\.context-notes-native\data\history.db`：`schema=2`、`integrity_check=ok`、当前实时记录为 **20 条旁注 / 110 条消息**（高于 Phase 7 记录的 19/110 基线，因 Phase 7 之后仍有日常使用）。本轮未写入、未迁移、未删除任何生产数据。 |
| 中/低风险个人隐私与本机路径元数据 | **需维护者确认处置方式（见第 6 节）** | 发现 3 项非凭据类个人/环境元数据：(B1) 全部 55 个 Git 提交的 Author/Committer 元数据含个人邮箱；(B2) 历史阶段验收文档与 `Build-Release.ps1` 含本机绝对路径与 Windows 本地用户名；(B3) `SelectionProbe.dll` PE 调试目录含本地构建 PDB 路径。**未擅自改写 Git 历史或强制推送**。 |

---

## 2. 扫描工具、版本与完整性校验

1. **Gitleaks `8.30.1`**
   - 压缩包 SHA-256 核对：`D29144DEFF3A68AA93CED33DDDF84B7FDC26070ADD4AA0F4513094C8332AFC4E`（与官方 `gitleaks_8.30.1_checksums.txt` 完全匹配）。
   - 执行环境：复制到正常中完整性临时目录，对仓库完整镜像（`git clone --mirror`）和工作区副本运行，避免 `Low Mandatory Level` 或 `safe.directory` 干扰。
   - 排除规则检查：未设置任何 `.gitleaksignore` 或自定义白名单规则，使用 Gitleaks 全部默认秘密检测规则全量扫描。
2. **自定义深度正则与二进制扫描器（Python 3）**
   - 扫描全部 536 个历史 Git Blob（`git rev-list --all --objects`）、全部 105 个 HEAD 跟踪文件、解压后的全部 24 个便携 ZIP 文件，并对 `SelectionProbe.dll` / `SelectionProbe.exe` 同时执行 UTF-8 与 UTF-16LE 字符串扫描及 SQLite 魔数检测（`SQLite format 3`）。
   - 覆盖 14 类目标：API Key、Access/Refresh Token、JWT、Cookie、私钥头、SQLite 数据库文件/魔数、用户聊天历史、邮箱与手机号等个人信息、本机绝对路径、本机用户名/主机名/Windows SID、代理配置、临时日志、测试凭据、敏感扩展名（`.db`/`.sqlite`/`.log`/`.pfx`/`.p12`/`.key`/`.pem`/`.env`/`.trx`/`.pdb`/`auth.json`/`config.toml`）。

---

## 3. Git 完整历史扫描结果

### 3.1 Gitleaks 全历史扫描（`--log-opts="--all"`）

- 覆盖范围：`main`（54 个提交）+ 本地 `wip/phase-2r-attempt`（1 个提交），共 **55 个提交**，扫描约 **969.89 KB**。
- 扫描结果：**0 leaks found**（退出码 `0`）。

### 3.2 自定义全 Blob 深度扫描（536 个历史对象）

| 检查项 | 命中数 | 判定 |
| --- | ---: | --- |
| API Key / Access Token / Refresh Token / Bearer / JWT | 0 | PASS |
| 私钥 (`-----BEGIN ... PRIVATE KEY-----`) / Cookie / Session Secret | 0 | PASS |
| 敏感文件名或扩展名（`.db`/`.sqlite`/`.log`/`.pfx`/`.key`/`.pem`/`.env`/`auth.json`/`config.toml`/`.pdb`） | 0 | PASS |
| SQLite 二进制内容（`SQLite format 3`） | 0 | PASS |
| 代理配置（`http_proxy`/`socks5`/`127.0.0.1:<port>`） / Windows SID / 手机号 | 0 | PASS |
| 主机名 (`DESKTOP-*`) | 0 | PASS |
| Git 提交元数据中的个人邮箱（非文件内容） | 55/55 提交 | 见第 6 节 B1 |
| 历史文档与构建脚本中的本机绝对路径与本机用户名 | 32 处唯一 `(路径, 匹配)` | 见第 6 节 B2 |

---

## 4. 当前工作区与发布包扫描结果

### 4.1 当前工作区（105 个 HEAD 跟踪文件）

- `gitleaks dir`：扫描 **867.70 KB**，**0 leaks found**（退出码 `0`）。
- 生产源码静态核查（`src/SelectionProbe/`）：0 处 `HttpClient` / `WebRequest` / `WebClient` / `Socket` / 遥测 / Cookie / 凭据文件读取逻辑；账号隔离仅计算内存 SHA-256 指纹。
- 历史阶段验收文档中的本机路径和本地用户名记录见第 6 节 B2（全部集中在仓库内 `docs/PHASE-*.md` 与 `docs/PROJECT-SPEC.md` 及 `scripts/Build-Release.ps1`，**不随软件发布包分发**）。

### 4.2 发布包扫描（Phase 7 既有交付包）

检查对象：`%USERPROFILE%\Downloads\ContextNotesNative-0.3.0-Phase7-20261119\` 及其中的 `ContextNotesNative-0.3.0-portable.zip`（247,968 字节，24 个文件）与 `ContextNotesNative-0.3.0-setup.exe`（2,291,711 字节）。

- `gitleaks dir`（解压后的便携 ZIP，61.26 KB）：**0 leaks found**（退出码 `0`）。
- `gitleaks dir`（整个 Phase 7 交付目录，含 EXE/ZIP/SHA256SUMS/文档）：**0 leaks found**（退出码 `0`）。
- 载荷文件清单核验（24 个文件）：仅包含 4 个框架依赖运行文件（`SelectionProbe.exe/.dll/.deps.json/.runtimeconfig.json`）、插件清单与 Hook、部署脚本 `Manage-Installation.ps1`、元数据（`release.json`、`SHA256.json`）与用户文档（`README.md`、`LICENSE`、`PRIVACY.md`、`CHANGELOG.md`、`docs/INSTALLATION.md`、`USER-GUIDE.md`、`TROUBLESHOOTING.md`、`SECURITY.md`、`SIGNING-PLAN.md`、`THIRD-PARTY-NOTICES.md`）。
- 随包文档检查：0 处本机用户名、0 处个人邮箱、0 处 本机开发目录绝对路径、0 处用户数据库或聊天记录。
- 二进制字符串检查：`SelectionProbe.dll` 内部存在 1 处 .NET 编译器自动写入的 CodeView RSDS PDB 本地构建路径（见第 6 节 B3，不含用户名或凭据）。

---

## 5. 误报逐项核查记录

| 扫描命中 | 所在位置 | 核查结论 |
| --- | --- | --- |
| `alice@example.com`, `bob@example.com`, `dev@example.com`, `a@example.com`, `b@example.com` | `src/SelectionProbe/Program.cs`、`tests/SelectionProbe.Tests/Phase4AppServerTests.cs`（及编译进 `SelectionProbe.dll` 自测方法） | **确认为误报**：RFC 2606 保留域名的合成单元测试样例，非真实个人邮箱。 |
| `0.3.0.0`, `4.0.0.0` | `docs/PHASE-5G-DEPLOYMENT.md`、`docs/PHASE-6A-DESKTOP.md`、`SelectionProbe.dll` | **确认为误报**：.NET 程序集版本号，非 IPv4 地址。 |
| `desktop-startup` | `src/SelectionProbe/DesktopStartup.cs`、`README.md` | **确认为误报**：日志文件名 `desktop-startup.log` 触发大小写不敏感 `DESKTOP-*` 规则，非本机主机名。 |
| `C:\Users`, `C:\Program Files\dotnet\dotnet.exe`, `C:\Program Files\WindowsApps\OpenAI.Codex_...\app\ChatGPT.exe` | `src/SelectionProbe/CodexAppServerClient.cs`、`src/SelectionProbe/Program.cs` | **确认为误报**：Windows 标准路径常量与内置自测夹具字符串，不含任何本机用户个人信息。 |

---

## 6. 识别到的非凭据类隐私/环境元数据风险及整改方案

尽管高危密钥、凭据、用户聊天记录和数据库泄露风险均为 **0**，本轮深度审查发现以下 4 项在公开仓库前必须如实汇报并妥善处理的元数据事项：

### B1. Git 提交元数据中的个人邮箱（中/低隐私敏感项，影响 55/55 提交）

- **现状**：全部 55 个提交（`main` 54 个 + `wip/phase-2r-attempt` 1 个）的 Git Author/Committer 均记录了维护者的个人 163 邮箱（位于 Git 提交头中，未出现在源码或文档正文）。
- **影响**：公开仓库后，任何克隆或查看 `git log` 的人均可看到该邮箱（不涉及密码或令牌泄露，但可能增加垃圾邮件或个人邮箱暴露风险）。
- **可选处置**：
  - **方案 1（保留现有历史，不改写）**：若维护者可接受该邮箱在公开 Git 提交记录中可见，则无需重写历史；后续提交可继续使用原配置或切换为 GitHub noreply 邮箱。
  - **方案 2（需维护者明确授权历史改写）**：使用历史重写工具将提交邮箱替换为 GitHub noreply 地址并强制推送至仍为 Private 的 `origin/main`，然后再设为 Public。
- **当前执行状态**：严格遵守约束，**未擅自改写 Git 历史或强制推送**，待维护者决定。

### B2. 历史文档与构建脚本中的本机绝对路径与本地 Windows 用户名（低/中隐私敏感项）

- **现状**：
  - 5 个历史验收/断点文档（`docs/PHASE-4-ACCEPTANCE.md:23`、`docs/PHASE-5-ACCEPTANCE.md:24`、`docs/PHASE-6B-RUNTIME.md:77`、`docs/PHASE-7-ACCEPTANCE.md:62`、`docs/PHASE-7-CHECKPOINT.md:18`）包含 `C:\Users\<local-user>\...` 路径，暴露本机 Windows 账户名。
  - 多份历史阶段文档（`docs/PHASE-1`~`7`、`docs/PROJECT-SPEC.md`）及 `scripts/Build-Release.ps1:41` 包含本机开发路径（如 `<local-workspace>\...`、`<local-vmware-dir>`、`<local-vm-dir>\...`）。
  - 这些文件**均未进入** EXE/ZIP 软件发布包，但若仓库公开，访问仓库历史或 HEAD 文件可看到这些路径。
- **整改建议**：
  - **必做（Phase 8C/8D，无需改写历史）**：将 HEAD 版本的 `scripts/Build-Release.ps1` 移除开发者私有路径硬编码（改为仅查标准安装路径或 `-Compiler` 参数），并将当前 HEAD 历史文档中的 `C:\Users\<local-user>\` 脱敏替换为 `%USERPROFILE%\`。
  - **可选（需维护者确认）**：是否需要同步改写旧 Git 历史中的这 5 份文档（通常可与 B1 一并决定）。

### B3. 编译产物 `SelectionProbe.dll` 嵌入本地构建路径（低风险）

- **现状**：Phase 7 构建的 `SelectionProbe.dll` PE 调试头（RSDS）嵌入了本地构建目录 `...\src\SelectionProbe\obj\Release\net10.0-windows\win-x64\SelectionProbe.pdb`（不含本机用户名、邮箱或凭据，仅含工作区目录名）。
- **整改建议**：在 Phase 8D 构建公开版 MIT 0.3.0 安装包与便携包时，向 `dotnet publish` 传入 `-p:DebugType=none`（同时保持 0 警告/0 错误与行为一致），彻底消除二进制中的本机构建路径。

### B4. 本地未推送分支 `wip/phase-2r-attempt`（低风险）

- **现状**：本地存在 1 个未合并的历史存档分支 `wip/phase-2r-attempt`（`8bd3f77`），当前未推送至 `origin`。
- **整改建议**：保持不推送该分支至公开远端，仅公开主分支 `main`。

---

## 7. 门禁状态

- **高危安全门禁（凭据 / 密钥 / 数据库 / 聊天历史）**：**PASS（0 泄露）**。
- **公开前最终确认项**：由于存在 B1（Git 历史提交邮箱）与 B2（历史文档中的本地用户名路径），在修改 `LICENSE` 并公开 GitHub 仓库前，需维护者明确选择：
  1. **直接保留现有 Git 历史**（仅脱敏 HEAD 文件并在 Phase 8D 去除 DLL 构建路径），或
  2. **授权清理 Git 历史**（替换邮箱与本地用户名路径后强推至私有远端再公开）。
