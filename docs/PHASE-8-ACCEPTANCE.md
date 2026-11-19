# Phase 8 验收与开源发布最终报告 — Context Notes Native 0.3.0

- 日期：2026-11-19（Asia/Shanghai）
- 仓库地址：<https://github.com/x549d/context-notes-native>
- 发布地址：<https://github.com/x549d/context-notes-native/releases/tag/v0.3.0>
- 总体结论：**目标 A（MIT 开源项目 + Windows 0.3.0 公开软件下载）已完成并经未登录公网 API 核验；目标 B（OpenAI Codex for Open Source 申请准备）全部前置条件与英文草稿已完成，仅待维护者本人填入个人信息并手动提交官方表单。**

---

## 1. Git 基线与阶段提交记录

- **初始实测基线 Commit**：`ca2fc2ace70f886cebe1cd58f0346d1cde9565c5`（分支 `main`，与 `origin/main` 一致，初始工作区 `CLEAN`，初始仓库状态为 Private）。
- **Phase 8 分阶段提交**：

| 阶段 | Commit | 提交说明 |
| --- | --- | --- |
| Phase 8A | `d9d0caa` | `docs(phase8a): complete ownership and license audit` |
| Phase 8B | `6496415` | `security(phase8b): complete public repository security audit` |
| Phase 8F | `1772bf1` | `docs(phase8f): prepare OpenAI OSS application` |
| Phase 8C（许可证） | `998f85c` | `license(phase8c): adopt MIT license` |
| Phase 8C（开源文档） | `7974dae` | `docs(phase8c): prepare open source documentation` |
| Phase 8D（发布包构建与门禁） | `fa2aa3c` | `release(phase8d): prepare Windows 0.3.0 public packages`（同时为 Release `v0.3.0` 附注标签指向的 CLEAN 源码提交） |
| Phase 8E / 最终报告 | 本报告所在提交（`git log -1 --format=%H -- docs/PHASE-8-ACCEPTANCE.md`） | `release(phase8e): publish public repository`（更新申请前置清单状态并记录最终公开发布验收结果） |

- **远端同步与工作区状态**：未改写任何历史提交、未强制推送、未推送本地历史存档分支 `wip/phase-2r-attempt`；最终 `main` 与 `origin/main` 完全一致，标签 `v0.3.0` 已推送至 `origin`，工作区 `CLEAN`。

---

## 2. 许可证变更、代码权属与第三方许可审查（Phase 8A & 8C）

详见 [docs/PHASE-8A-OWNERSHIP-AND-LICENSE-AUDIT.md](PHASE-8A-OWNERSHIP-AND-LICENSE-AUDIT.md) 与 [docs/THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)：

- **代码权属**：核查全部 55 个初始提交（`main` 54 + 本地 `wip/phase-2r-attempt` 1），100% 由单一维护者标识 `x549d` 提交，0 子模块、0 Git LFS、0 历史二进制大对象、0 外部源码复制。维护者已正式确认全部原创代码由本人独立拥有完整开源授权，并确认版权持有人署名为 `x549d`。
- **许可证变更**：根目录 [LICENSE](../LICENSE) 由既有 `Proprietary` 状态占位正式切换为完整 **MIT License**（`Copyright (c) 2026 x549d`），插件清单 `codex-plugin/plugins/context-notes/.codex-plugin/plugin.json` 同步更新为 `"license": "MIT"`。未登录 GitHub REST API 已核实 `license.spdx_id = MIT`。
- **第三方组件边界**：
  - 生产工程 `src/SelectionProbe/SelectionProbe.csproj`：**0 个生产 NuGet 包**、0 个第三方字体文件、0 个图标/图片包、0 个再分发 Windows/Codex DLL。
  - 测试工程 `tests/SelectionProbe.Tests/SelectionProbe.Tests.csproj`：12 个仅测试期 NuGet 包（MIT / Apache-2.0），不进入任何发布产物。
  - 安装器编译器：官方未修改的 **Inno Setup 6.7.4**，保留原始版权/官网声明；安装器外壳受 Inno Setup License 约束，项目自有代码受 MIT 许可约束，便携 ZIP 不含 Inno Setup 组件。
  - 商标声明：明确项目为独立第三方开源工具，与 OpenAI 无关联，未获官方认证、赞助或背书。
- **开源文档与隐私更新**：
  - 更新 [README.md](../README.md)、[PRIVACY.md](../PRIVACY.md)（发布者 `x549d`，公开隐私联系渠道 `https://github.com/x549d/context-notes-native/issues`，明确说明本机 Codex App Server 将选区与提问送入 OpenAI 云端处理流程，零遥测、零凭据提取）、[CHANGELOG.md](../CHANGELOG.md)、[docs/THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)、[docs/RELEASE-README.md](RELEASE-README.md)、[docs/SECURITY.md](SECURITY.md)、[docs/INSTALLATION.md](INSTALLATION.md)、[docs/SIGNING-PLAN.md](SIGNING-PLAN.md)。
  - 新增根目录 [CONTRIBUTING.md](../CONTRIBUTING.md) 与 [SECURITY.md](../SECURITY.md)。
  - 核对全部 34 处相对 Markdown 链接（含随包根目录视图），**0 断链**。

---

## 3. 完整敏感数据安全审查（Phase 8B & 8E 复扫）

详见 [docs/PHASE-8B-PUBLIC-REPOSITORY-SECURITY-AUDIT.md](PHASE-8B-PUBLIC-REPOSITORY-SECURITY-AUDIT.md)：

- **扫描工具**：`gitleaks 8.30.1`（安装包 SHA-256 与官方校验文件核对一致）+ 自定义全量 Python 正则/二进制深度扫描器（覆盖 UTF-8、UTF-16LE 与 SQLite 魔数）。
- **Phase 8B 初始全量扫描**：全部 55 个提交（536 个历史 Blob）、105 个 HEAD 文件及 Phase 7 交付包中，API Key / Token / JWT / Cookie / 私钥 / SQLite 数据库 / 用户聊天历史 / 代理配置 / 日志均为 **0 命中**。
- **元数据处置（经维护者明确确认）**：
  - 保留现有 Git 历史不改写、不强制推送；
  - 已将 HEAD 5 份历史文档中的本机用户路径脱敏为 `%USERPROFILE%\`（验证当前 HEAD 跟踪文件中本机 Windows 用户名命中数为 **0**）；
  - 已从 `scripts/Build-Release.ps1` 移除开发者私有 Inno Setup 路径硬编码；
  - 已在 `dotnet publish` 增加 `-p:DebugType=none`，验证 MIT 0.3.0 `SelectionProbe.dll` 内 `<local-workspace>`、`.pdb` 与本机用户名均为 **0 命中**。
- **Phase 8E 公开前终检复扫**：
  - `gitleaks 8.30.1` 覆盖全部 61 个提交（`--log-opts="--all"`，约 1.05 MB）：**0 leaks found**（退出码 `0`）。
  - `gitleaks 8.30.1` 覆盖 MIT 0.3.0 解压便携载荷（66.15 KB）：**0 leaks found**（退出码 `0`）。
- **生产用户数据库保护核验**：全程严格只读检查 `%USERPROFILE%\.context-notes-native\data\history.db`，Phase 8 前后均为 `schema=2`、`annotations=20`、`messages=110`、`integrity_check=ok`，未删除、未覆盖、未进入 Git 或任何发布包。

---

## 4. Windows 0.3.0 公开包构建、哈希与安装验收（Phase 8D）

基于 CLEAN 源码提交 `fa2aa3c0f81d77410e11b66ebf36264851bbd4dd`（中完整性 Inno Setup 6.7.4 编译器，输出至中完整性交付目录 `%USERPROFILE%\Downloads\ContextNotesNative-0.3.0-MIT-20261119`）：

### 4.1 发布物清单与 SHA-256 校验值

| 文件名 | 大小（字节） | SHA-256 | Authenticode 签名状态 |
| --- | ---: | --- | --- |
| `ContextNotesNative-0.3.0-setup.exe` | 2,292,783 | `59FA04E36EA0D53D2B7DE389E300D2428429CB23EE7A212B24017B6C83CC0DD2` | `NotSigned` |
| `ContextNotesNative-0.3.0-portable.zip` | 244,171 | `41F7EC6E03A4E8A21C0D3B596259D39E154A345269319B353907A260B6E5E075` | N/A（内含运行文件均为 `NotSigned`） |
| `SHA256SUMS` | 207 | `FBFE1B6C03656B0A7ADEF5F9EE3B5DAC040249785DBDAFD6F6FF6BB9E9F2841C` | 文本校验清单 |

### 4.2 本轮实测回归与安装验收矩阵

| 验收项目 | 本轮实测结果 | 说明 |
| --- | --- | --- |
| Release 构建 (`dotnet build -c Release`) | **PASS（0 警告 / 0 错误）** | 零生产 NuGet 依赖 |
| xUnit 单元测试 (`dotnet test`) | **PASS（106 / 106）** | 失败 0、跳过 0 |
| 内置自测 (`SelectionProbe.dll --self-test`) | **PASS（80 / 80）** | 退出码 0 |
| 发布包审计 (`scripts/Test-Release.ps1`) | **PASS（13 / 13）** | 核验 MIT 许可、`source_dirty=false`、`NotSigned`、逐文件 `SHA256.json` 与 `SHA256SUMS` |
| 标准 EXE 安装器端到端 (`scripts/Test-StandardInstaller.ps1`) | **PASS（29 / 29）** | 隔离 `/TESTROOT`、无管理员权限；覆盖全新安装、Temp/HKCU64 预检、覆盖升级、失败自动回滚与非零退出码、v1→v2 迁移、默认保留历史卸载、显式 `/REMOVEHISTORY=1` 卸载 |
| 便携 ZIP 安装管理 (`scripts/Test-Installation.ps1`) | **PASS（19 / 19）** | 独立解压、插件注册、幂等重装、篡改载荷拒绝、升级回滚、默认保留历史卸载与显式删除 |
| 历史稳定性与并发/删除专项 | **沿用 Phase 6 & 7 既有证据** | 未重复执行 30/60 分钟采样（Phase 6：30.005 分钟 / 61 样本 PASS；Phase 7：三浮窗并发重启恢复 PASS、独立库永久删除 23/23 PASS） |
| 独立干净 Windows OS（全新虚拟机/云主机） | **`NOT TESTED`** | 未购买云 Windows、未启用需管理员权限的虚拟化功能、未虚构结果 |

---

## 5. 代码签名状态

- Phase 8 只读复核 `Cert:\CurrentUser\My` 与 `Cert:\LocalMachine\My`：有效期内、含私钥的 Code Signing 证书数量为 **0**。
- 未生成自签名证书冒充可信签名，未购买付费证书。
- 已在 GitHub Release 说明、`README.md`、`docs/RELEASE-README.md`、`docs/INSTALLATION.md` 和 [docs/SIGNING-PLAN.md](SIGNING-PLAN.md) 中明确告知 Windows SmartScreen 未知发布者提示与 `SHA256SUMS` 校验方法。

---

## 6. GitHub 公开仓库与 Release 核验（Phase 8E）

经未登录公网 GitHub REST API 独立核验：

- **仓库可见性**：`https://api.github.com/repos/x549d/context-notes-native` 返回 `visibility=public`、`private=false`、`license.spdx_id=MIT`、`default_branch=main`。
- **原始 LICENSE 公网访问**：`https://raw.githubusercontent.com/x549d/context-notes-native/main/LICENSE` 返回 `MIT License` / `Copyright (c) 2026 x549d`。
- **正式 GitHub Release `v0.3.0`**：`https://api.github.com/repos/x549d/context-notes-native/releases/latest` 返回 `tag_name=v0.3.0`、`name=Context Notes Native 0.3.0`、`draft=false`、`prerelease=false`，且全部 3 个上传文件状态均为 `uploaded`，字节数与 GitHub 计算的 SHA-256 Digest 与本地构建完全一致：
  - `https://github.com/x549d/context-notes-native/releases/download/v0.3.0/ContextNotesNative-0.3.0-setup.exe`
  - `https://github.com/x549d/context-notes-native/releases/download/v0.3.0/ContextNotesNative-0.3.0-portable.zip`
  - `https://github.com/x549d/context-notes-native/releases/download/v0.3.0/SHA256SUMS`

---

## 7. OpenAI Codex for Open Source 申请准备状态（Phase 8F）

详见 [docs/OPENAI-CODEX-FOR-OSS-APPLICATION.md](OPENAI-CODEX-FOR-OSS-APPLICATION.md)：

- **目标入口**：<https://openai.com/form/codex-for-oss/>
- **申请权益**：6 个月 **ChatGPT Pro**（Yes）、**API Credits**（Yes）、**Codex Security**（No）。
- **前置条件**：GitHub 个人资料公开、公开仓库、MIT 许可证生效、完整 README、公开 `v0.3.0` Release 均已满足。
- **英文申请草稿**：3 个文本字段严格符合 500 字符上限（实测 470 / 361 / 440 字符），完全真实反映项目早期公开阶段与实测证据，无任何虚构 Stars/下载量/用户数。
- **未代为提交**：遵守安全约束，未猜测姓名/邮箱/Organization ID，未代提交表单。

---

## 8. 尚未完成或待维护者亲自处理的事项

1. **手动提交 OpenAI Codex for Open Source 申请表**：请打开 <https://openai.com/form/codex-for-oss/>，参照 [docs/OPENAI-CODEX-FOR-OSS-APPLICATION.md](OPENAI-CODEX-FOR-OSS-APPLICATION.md) 填入您的真实姓名（First/Last Name）、活跃 ChatGPT 账号邮箱与 OpenAI Organization ID（`org-...`），核对勾选 **API credits** 且**不勾选 Codex Security** 后手动提交。
2. **独立干净 Windows OS 全流程验收（当前 `NOT TESTED`）**：若后续具备全新 Windows 10/11 虚拟机或独立测试机条件，可在干净环境补充端到端安装、划词、真实 AI 问答、升级与卸载实测。
3. **可信 Authenticode 代码签名（当前 `NotSigned`）**：如需消除 Windows SmartScreen 未知发布者提示，可在后续按 [docs/SIGNING-PLAN.md](SIGNING-PLAN.md) 评估并接入受信任 CA 证书或云代码签名服务。
