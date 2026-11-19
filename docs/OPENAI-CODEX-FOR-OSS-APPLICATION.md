# OpenAI Codex for Open Source 申请准备文档（Phase 9F 更新版）

- 日期：2026-11-19（Asia/Shanghai）
- 目标计划：OpenAI **Codex for Open Source**
- 官方申请入口：<https://openai.com/form/codex-for-oss/>
- 申请状态：**完整英文草稿与全部开源/隐私/CI 工程条件已就绪；未经维护者最终审核并填入个人信息，绝不代为提交表单**

---

## 1. 申请权益与维护者已确认选择

| 申请项目 | 选择 | 说明 |
| --- | --- | --- |
| **ChatGPT Pro** | **申请（6 个月）** | 用于日常代码维护、Windows UIA/Codex 兼容性适配、Issue 排查与 PR 审查 |
| **API Credits** | **申请（Yes）** | 用于开源仓库自动化维护工作流（PR 审查、Issue 分类、回归分析、代码质量与安全审查） |
| **Codex Security** | **不申请（No）** | 按维护者明确决定不勾选 |

---

## 2. 官方要求重新核实（2026-11-19 联网复核）

根据 OpenAI 官方网站（`openai.com/form/codex-for-oss` 及官方计划说明）重新核实当前资格与审核规则：

- **维护者身份要求**：申请人须为公开 GitHub 仓库的 **Primary maintainer** 或 **Core maintainer**（拥有仓库写入/管理权限）；支持维护者本人申请或提名核心维护者。
- **仓库与活跃度要求**：目标项目必须托管在 **Public GitHub 仓库**，具备清晰公开源码、持续维护行为（PR 审查、Issue 分类、版本发布管理）与生态价值。
- **生态指标与早期项目政策**：OpenAI 会综合考察项目生态重要性、实际用途与维护记录，同时官方明确说明：**即使项目暂不符合传统高星标或高下载量指标，只要具备明确生态价值，仍鼓励如实说明并申请**。
- **权益与审核流程**：
  - **ChatGPT Pro**：常规支持周期为 **6 个月**（含 Codex 权限）；
  - **API Credits**：需提供有效 `OpenAI Organization ID` 并说明具体自动化维护用途；
  - **Codex Security**：按项目影响与需求有条件审批（本次明确不申请）；
  - **审核方式**：滚动审核（rolling basis），由 OpenAI 酌情决定并通过邮件通知结果。
- **表单核心字段与字符上限**：
  - `First Name` / `Last Name`（维护者本人必填）
  - `Email`（活跃 ChatGPT 账号绑定邮箱，维护者本人必填）
  - `GitHub Username`（公开可见：`x549d`）
  - `GitHub Repository URL`（公开仓库：`https://github.com/x549d/context-notes-native`）
  - `Maintainer Role`（`Primary maintainer`）
  - `Why does this repository qualify?`（≤ 500 字符）
  - `I'm interested in...`（勾选 `API credits for my project`；**不勾选** `Codex Security`）
  - `OpenAI Organization ID`（`org-...`，维护者本人从 OpenAI API 后台获取并填写）
  - `How will you use API credits for your project?`（≤ 500 字符）
  - `Anything else we should know?`（≤ 500 字符）

### 申请前置条件核查清单

| 前置条件 | 状态 | 说明 |
| --- | --- | --- |
| GitHub 个人资料公开 (`x549d`) | **已完成** | 经公开 GitHub API 核实可见 |
| 全 Git 历史隐私清理与 Gitleaks 0 泄露 | **已完成** | 个人邮箱已全面替换为 `348141032+x549d@users.noreply.github.com`，历史路径/用户名/PDB 均 0 命中 |
| MIT 许可证生效（根 `LICENSE` + `plugin.json`） | **已完成** | `Copyright (c) 2026 x549d`，0 生产第三方 NuGet 依赖 |
| GitHub Actions Windows CI 与贡献模板 | **已完成** | 已配置 `.github/workflows/ci.yml`、手动打包工作流、4 类 Issue 模板与 PR 模板 |
| 公开仓库、准确 About 简介与公开 `v0.3.0` Release | **已完成** | 经未登录公网 GitHub API 核实 `visibility=public`、About 简介与 Topics 已更新、`v0.3.0` 全部 3 个隐私洁净重建资产可公开下载且 SHA-256 匹配 |
| 维护者个人姓名、ChatGPT 邮箱与 `OpenAI Organization ID` | **待维护者本人填写** | 不猜测、不代填、不擅自提交 |

---

## 3. 项目英文核心介绍（供扩展介绍使用）

> Context Notes Native is a native Windows companion for Codex Desktop Chat. It allows users to select assistant responses, create contextual annotations, preserve exact quoted content, ask multi-turn follow-up questions in independent annotation windows, and maintain persistent local conversation history.

### 核实无虚构的核心亮点

- **Codex Desktop 原生生态集成**：严格通过官方 `codex.exe app-server`（stdio JSON-RPC 2.0）与官方插件 `SessionStart` Hook 集成，不注入进程、不修改 Codex 二进制、不读取 Cookie 或 OAuth Token。
- **Windows 原生交互体验**：基于 .NET 10 / WPF / UI Automation / Win32 实现，无剪贴板污染、不抢焦点浮动入口（`WS_EX_NOACTIVATE`）、多显示器/混合 DPI 适配、完整中文 IME 支持与独立旁注多轮流式对话。
- **本地持久化与用户数据安全**：基于 Windows 系统自带 `winsqlite3.dll`（SQLite WAL 模式）持久化历史、支持 `Ctrl+Alt+H` 检索/归档/软删除/二次确认永久删除与在线备份，默认卸载保留用户数据。
- **隐私保护与可审计性**：0 生产 NuGet 依赖、0 遥测、0 凭据读取；完整 Git 历史与 `SelectionProbe.dll` 构建产物（`-p:DebugType=none`）均已完成个人邮箱、本地路径与 Gitleaks 零泄露清理。
- **自动化测试与持续维护体系**：配置 GitHub Actions Windows CI（Release 0 警告/0 错误、106 项 xUnit、80 项内置自测、23 项合成库删除验收、权限预检与 PowerShell 语法校验），辅以 29 项标准 EXE 安装器验证、19 项便携安装验证、结构化 Issue/PR 模板与明确版本发布流程。
- **如实披露早期阶段**：本项目刚完成 `v0.3.0` 公开开源与隐私整理，仍处于早期公开阶段，不虚构任何 Stars、Forks、下载量、PR 或社区用户数。

---

## 4. 英文申请表逐项草稿（严格控制在 500 字符以内）

### 基础字段

| 表单字段 | 建议填写值 |
| --- | --- |
| First Name | `[待维护者填写：本人名]` |
| Last Name | `[待维护者填写：本人姓]` |
| Email | `[待维护者填写：接收 6 个月 ChatGPT Pro 的活跃 ChatGPT 账号邮箱]` |
| GitHub Username | `x549d` |
| GitHub Repository URL | `https://github.com/x549d/context-notes-native` |
| Maintainer Role | `Primary maintainer` |
| API credits for my project | **Checked（Yes）** |
| Codex Security | **Unchecked（No）** |
| OpenAI Organization ID | `[待维护者填写：org-...，从 platform.openai.com 获取]` |

### 文本字段 1：`Why does this repository qualify?`（487 / 500 字符）

```text
Context Notes Native is a native Windows companion for Codex Desktop Chat. It allows users to select assistant responses, create contextual annotations, preserve exact quoted content, ask multi-turn follow-up questions in independent annotation windows, and maintain persistent local conversation history. Built with .NET 10, WPF, UIA, and official Codex App Server (stdio JSON-RPC), it is early-stage MIT open source with Windows CI, 106 xUnit tests, 80 self-tests, and v0.3.0 packages.
```

### 文本字段 2：`How will you use API credits for your project?`（398 / 500 字符）

```text
API credits will be used solely for practical open-source maintenance of this repository: AI-assisted pull request reviews (WPF, Windows UIA, Win32, SQLite WAL, and installer safety), automated issue triage and categorization, regression test failure analysis, code quality checks, pre-release security and privacy reviews, and maintaining release documentation as Codex Desktop evolves on Windows.
```

### 文本字段 3：`Anything else we should know?`（486 / 500 字符）

```text
Integrates strictly via official codex.exe app-server stdio JSON-RPC and SessionStart hooks—zero process injection, cookie access, telemetry, or production NuGet deps. Full Git history and v0.3.0 binaries passed privacy and Gitleaks audits. Early public stage (no fabricated adoption metrics yet); backed by Windows CI, installer/upgrade/rollback tests, and structured issue templates for Codex Desktop compatibility. Requesting 6-month ChatGPT Pro and API credits (not Codex Security).
```

---

## 5. 提交前人工确认清单

在维护者亲自打开 <https://openai.com/form/codex-for-oss/> 提交前，请逐项核对：

- [x] GitHub 仓库 `https://github.com/x549d/context-notes-native` 已恢复为 **Public**，About 简介与 Topics 已更新为公开项目说明。
- [x] GitHub Release `v0.3.0` 已更新为隐私洁净重建版产物（`setup.exe`、`portable.zip`、`SHA256SUMS`）且经公网 API 核验可下载、SHA-256 一致。
- [x] GitHub Actions `Windows CI` 在公开 `main` 分支上运行通过（Run #3 `416062a` 全部 11 个步骤 `success`）。
- [ ] 表单勾选了 **API credits**，且确认 **未勾选 Codex Security**。
- [ ] 已由维护者本人核实上述英文文本完全真实、无任何虚构数据，并亲手填入姓名、ChatGPT 邮箱与 `OpenAI Organization ID` 后点击提交。
