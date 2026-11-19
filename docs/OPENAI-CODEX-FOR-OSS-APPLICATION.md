# OpenAI Codex for Open Source 申请准备文档

- 日期：2026-11-19（Asia/Shanghai）
- 目标项目：Codex for Open Source
- 官方申请入口：<https://openai.com/form/codex-for-oss/>
- 申请状态：**草稿已就绪，等待前置条件完成与维护者补充个人信息后人工确认提交（本阶段未擅自提交表单）**

---

## 1. 申请权益与用户已确认的选择

| 权益项目 | 选择 | 说明 |
| --- | --- | --- |
| ChatGPT Pro（6 个月） | **申请（Yes）** | 用于日常代码维护、Issue 排查、PR 审查与 Windows 桌面兼容性适配 |
| API Credits（项目自动化维护） | **申请（Yes）** | 用于开源仓库维护自动化工作流（见第 4 节） |
| Codex Security | **不申请（No）** | 按维护者明确决定不勾选 |

---

## 2. 官方资格与前置条件核查

根据 OpenAI Codex for Open Source 公开申请说明与表单结构核实：

- **面向人群**：活跃开源项目的主要维护者（Primary maintainer）或核心维护者（Core maintainer）。
- **评审关注点**：项目在生态中的实际价值、可验证的公开源码与发布、持续维护记录与维护计划；官方同时注明即使项目不符合传统高星标/高下载量指标，若具有独特生态价值仍可如实说明并申请。
- **表单字段与字符限制**：
  - `First Name` / `Last Name`（必填，须由维护者亲自填写）
  - `Email`（必填，须为当前活跃 ChatGPT 账号绑定的邮箱）
  - `GitHub Username`（须为公开可见）
  - `GitHub Repository URL`（须为 Public 仓库）
  - `Maintainer Role`（单选：`Primary maintainer` / `Core maintainer`）
  - `Why does this repository qualify?`（文本，上限 **500 字符**）
  - `I'm interested in...`（多选：不勾选 `Codex Security`；勾选 `API credits for my project`）
  - `OpenAI Organization ID`（申请 API Credits 时必填，来自 OpenAI API Dashboard）
  - `How will you use API credits for your project?`（文本，上限 **500 字符**）
  - `Anything else we should know?`（可选文本，上限 **500 字符**）

### 当前前置条件检查表

| 前置条件 | 当前状态 | 备注 |
| --- | --- | --- |
| GitHub 个人资料公开 (`x549d`) | **已完成** | 经公开 GitHub API 核实可见 (`login=x549d`) |
| GitHub 仓库设为 Public (`https://github.com/x549d/context-notes-native`) | **已完成** | 经未登录 GitHub API 核实 `visibility=public`、`private=false` |
| MIT 许可证生效（根 `LICENSE` 与 `plugin.json`） | **已完成** | 根 `LICENSE`（`Copyright (c) 2026 x549d`）已生效，GitHub API 识别 `license=MIT` |
| 公开版 `README.md` 与文档完整 | **已完成** | 包含完整安装、使用、隐私、安全、贡献与第三方许可文档 |
| 公开可下载的 GitHub Release（`v0.3.0` EXE/ZIP/SHA256SUMS） | **已完成** | 已正式发布 <https://github.com/x549d/context-notes-native/releases/tag/v0.3.0>，3 个资产均已验证可公开下载 |
| 维护者身份与 OpenAI 账号字段 | **待维护者本人填写** | 见第 5 节缺失个人信息清单（不猜测、不代填、不擅自提交） |

---

## 3. 核心英文定位说明（供项目介绍与扩展阅读使用）

> Context Notes Native is a native Windows companion for Codex Desktop Chat. It enables users to attach contextual notes to selected assistant responses, preserve exact quoted content, ask follow-up questions in independent annotation windows, and maintain persistent local conversation history.

### 项目事实要点（全部基于已验证实测，零虚构数据）

- **真实可运行的桌面软件**：基于 C# / .NET 10 / WPF / Windows UI Automation / Win32 API / SQLite 构建，提供当前用户免管理员权限安装器（`setup.exe`）和独立便携包（`portable.zip`）及 `SHA256SUMS`。
- **深度原生集成 Codex 生态**：仅通过官方 `codex.exe app-server`（stdio JSON-RPC 2.0）和官方插件 `SessionStart` Hook 集成，不注入 Codex 进程、不读取 Cookie、不提取 Token、不存储登录凭据。
- **核心交互体验**：选中助手回答后在选区旁弹出不抢焦点的「提问」入口，点击打开独立旁注小框、保留原文引用、自动聚焦输入框、完整支持中文输入法（IME）、实时流式输出、多轮连续追问、多旁注独立并发、取消生成与本地 SQLite 持久化历史管理（搜索、归档、软删除/永久删除与恢复）。
- **已完成的测试与安全审计**：Release 0 警告/0 错误、106 项 xUnit 单元测试、80 项内置自测、29 项标准 EXE 安装/升级/回滚/卸载端到端验证、19 项便携安装管理验证、23 项独立合成库永久删除验证、三浮窗真实并发与重启恢复验证、30 分钟连续稳定性采样（61 样本、0 重启、0 错误日志），以及 0 生产 NuGet 依赖和全历史 `gitleaks` 0 凭据泄露审计。
- **诚实说明早期阶段**：本项目为刚公开的早期开源软件（0.3.0），目前尚无外部社区 Stars、下载量或第三方 PR 数据；申请材料不虚构任何采用指标。

---

## 4. 英文表单逐项填写草稿（严格控制在 500 字符以内）

### 基础字段

| 表单字段 | 建议填写值 |
| --- | --- |
| First Name | `[待维护者填写：本人名]` |
| Last Name | `[待维护者填写：本人姓]` |
| Email (ChatGPT account email) | `[待维护者填写：活跃 ChatGPT 账号邮箱]` |
| GitHub Username | `x549d` |
| GitHub Repository URL | `https://github.com/x549d/context-notes-native` |
| Maintainer Role | `Primary maintainer` |
| Feature Selection — API credits for my project | **Checked (Yes)** |
| Feature Selection — Codex Security | **Unchecked (No)** |
| OpenAI Organization ID | `[待维护者填写：org-...，从 platform.openai.com 设置页获取]` |

### 文本字段 1：`Why does this repository qualify?`（实测 470 / 500 字符）

```text
Context Notes Native is a native Windows companion for Codex Desktop Chat built with .NET 10, WPF, UIA, and the official Codex App Server (stdio JSON-RPC). It lets users attach contextual notes to selected answers, keep exact quotes, ask multi-turn follow-ups in independent windows, and store local SQLite history. Newly open-sourced under MIT (early public stage), it ships runnable 0.3.0 Windows packages backed by 106 xUnit tests, 80 self-tests, and security audits.
```

### 文本字段 2：`How will you use API credits for your project?`（实测 361 / 500 字符）

```text
API credits will support maintainer automation for this open-source repository: automated GitHub PR reviews (WPF, UIA, Win32, SQLite, and installer safety), regression test failure analysis, issue triage and categorization, pre-release security and dependency/privacy checks, and drafting release notes and documentation updates for ongoing Windows maintenance.
```

### 文本字段 3：`Anything else we should know?`（实测 440 / 500 字符）

```text
Integrates strictly via official Codex App Server stdio JSON-RPC and SessionStart hooks—no process injection, cookie access, or credential storage. Ships 0.3.0 Windows installer and portable ZIP with SHA256SUMS, backed by concurrency, restart-recovery, deletion, and 30-min stability checks. Planned maintenance tracks Codex Desktop UIA updates and community issues. Requesting 6-month ChatGPT Pro and API credits only (not Codex Security).
```

---

## 5. 缺失个人信息清单（须由维护者本人核对填写）

严禁代理猜测或代填以下敏感/个人信息：

1. **First Name / Last Name**：维护者真实姓名（与申请表要求一致）。
2. **ChatGPT 账号邮箱**：须与计划接收 6 个月 ChatGPT Pro 权益的活跃 ChatGPT 账号一致。
3. **OpenAI Organization ID**（`org-...`）：从 OpenAI API Platform（Settings → Organization → General）查看并亲自填入，不要提交任何 API Key 或密码。
4. **GitHub 公开资料核对**：建议在提交申请前于 GitHub 个人资料页确认简介与已公开的 `x549d/context-notes-native` 仓库可正常访问。

---

## 6. 最终提交前人工确认清单

在打开 <https://openai.com/form/codex-for-oss/> 提交前，请逐项确认：

- [x] `https://github.com/x549d/context-notes-native` 已为 **Public**，未登录浏览器/API 核实可公开访问。
- [x] 根目录 `LICENSE` 已更新为正式 **MIT** 许可证且 GitHub 已识别为 `MIT`。
- [x] 已发布公开 **GitHub Release `v0.3.0`**（<https://github.com/x549d/context-notes-native/releases/tag/v0.3.0>），附带可下载的 `ContextNotesNative-0.3.0-setup.exe`、`ContextNotesNative-0.3.0-portable.zip` 与 `SHA256SUMS`。
- [ ] 表单中已勾选 **API credits**，且确认 **未勾选 Codex Security**。
- [ ] 已由维护者本人核实上述英文文本无夸大或虚构数据，并填入真实姓名、ChatGPT 邮箱与 OpenAI Organization ID 后手动点击提交。
