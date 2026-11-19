# Phase 8A：代码权属与第三方许可审查报告

- 日期：2026-11-19（Asia/Shanghai）
- 审查基线 Commit：`ca2fc2ace70f886cebe1cd58f0346d1cde9565c5`（分支 `main`，与 `origin/main` 一致，初始工作区 `CLEAN`）
- 审查目标：在修改根 `LICENSE` 为 MIT 及公开 GitHub 仓库之前，核验全部提交历史、贡献者、源码来源、第三方依赖/资源与商标边界。

---

## 1. 审查结论摘要

| 审查维度 | 结果 | 说明 |
| --- | --- | --- |
| Git 历史与贡献者一致性 | **PASS（技术核查通过；法律权属待维护者确认）** | 全部 55 个提交（`main` 54 个 + 本地未推送分支 `wip/phase-2r-attempt` 1 个）均由单一作者标识提交，无其他贡献者、无外部仓库合并、无子模块、无 Git LFS、无历史二进制大对象。Git 作者名本身不构成法定权属证明，须由维护者确认其对全部原创代码享有完整授权。 |
| 项目源码来源核查 | **PASS** | HEAD 共 105 个受跟踪文件，均为本项目原创代码、脚本、配置与文档，未发现复制自第三方仓库的源码或冲突版权头。 |
| 第三方依赖与资源许可 | **PASS（无许可证冲突）** | 生产工程 `SelectionProbe.csproj` 为 **0 个 NuGet 依赖**、**0 个第三方字体文件**、**0 个图标/图片包**、**0 个再分发 Windows/Codex 二进制**；测试工程仅含 12 个测试期 NuGet 包（MIT / Apache-2.0），均不进入发布包。Inno Setup 6.7.4 官方许可允许随 MIT 项目分发生成的安装器，且已保留其原始声明。 |
| OpenAI / ChatGPT / Codex 商标边界 | **PASS（附命名边界建议）** | 明确独立第三方定位，不宣称官方认证或背书。仓库名（`context-notes-native`）与安装器名（`Context Notes Native`）已使用中性名称；`README.md` 公开标题建议同步突出中性名称 `Context Notes Native` 并用事实性副标题说明兼容 Codex Desktop Chat。 |
| MIT 授权技术可行性 | **PASS（待维护者确认版权持有人署名）** | 本项目自有代码采用 MIT 许可证无第三方许可冲突；但在写入 `LICENSE` 前，须由维护者确认版权声明中的署名方式（使用 GitHub 标识 `x549d` 或其确认的法定姓名）及权属无第三方限制。 |

---

## 2. Git 仓库状态与贡献者统计

### 2.1 实际核验的仓库基线

- **仓库地址**：`https://github.com/x549d/context-notes-native.git`（核验时为 Private，未登录访问 GitHub API 返回 `404`）。
- **分支清单**：
  - `main`：`ca2fc2ace70f886cebe1cd58f0346d1cde9565c5`（54 个提交，与 `origin/main` 完全一致）。
  - `wip/phase-2r-attempt`：`8bd3f77d75d5e2a30561655e35f68d7109834a61`（仅存在于本地，未推送至 `origin`；包含 1 个未合并的 Phase 2R 历史存档提交）。
- **Tag**：无（0 个本地或远端 Tag）。
- **Git 子模块**：无（`git submodule status` 输出为空）。
- **Git LFS**：无（未配置、0 个 LFS 跟踪文件）。
- **历史二进制文件检查**：`git log --all --numstat` 核查全部 55 个提交，0 个二进制文件入库。

### 2.2 贡献者与提交元数据核查

执行 `git shortlog -sne --all` 及逐提交 Author/Committer/Trailer 核查结果：

| 提交计数 | 分支覆盖 | Author / Committer 标识 | Co-authored-by / Signed-off-by / 外部导入记录 |
| ---: | --- | --- | --- |
| 55 / 55 (100%) | `main` (54) + `wip/phase-2r-attempt` (1) | 单一维护者标识 `x549d`（详见 Phase 8B 报告中关于提交元数据邮箱的隐私核查） | 0 条 Co-author、0 条 Signed-off-by、0 条外部源码导入记录、0 个合并提交 |

> **法律权属边界说明**：Git 提交元数据中的作者名称仅反映本地 Git 配置，不能单凭作者名推断真实法定姓名、雇佣关系或职务作品归属。维护者在正式切换为 MIT 许可证前，须自行确认其拥有全部原创代码的合法版权与开源授权权利。

---

## 3. 文件来源与版权声明核查

HEAD 共跟踪 **105 个文件**，按目录核查：

- `src/SelectionProbe/`（29 个文件：`.cs`、`.xaml`、`.csproj`）：全部为本项目原创 C# / WPF / UIA / Win32 P/Invoke / SQLite P/Invoke / App Server JSON-RPC 客户端代码。未发现外部版权头、`Adapted from` / `Copied from` 声明或复制粘贴的第三方源码片段。
- `tests/`（13 个文件）：`SelectionProbe.Tests` 与 `HistoryDeletion.Acceptance` 均为本项目原创测试代码。
- `scripts/`（11 个 `.ps1` 文件）：全部为本项目原创构建、安装、卸载、诊断与测试脚本。
- `installer/ContextNotes.iss`（1 个文件）：本项目原创 Inno Setup 脚本文档，`AppName=Context Notes Native`、`AppPublisher=x549d`。
- `codex-plugin/`（8 个文件）：本项目原创 Codex 插件清单与 Hook 脚本；其中 `plugin.json` 当前声明 `"author": { "name": "x549d" }`、`"license": "Proprietary"`（待 Phase 8C 随根 `LICENSE` 同步更新为 `"MIT"`）。
- `docs/` 与根目录文档（43 个文件）：本项目原创规范、架构、用户指南与验收记录；根 `LICENSE` 当前为 7 行 `Proprietary` 状态占位文本（未包含版权持有人或 MIT 正文）。

---

## 4. 第三方依赖与资源完整清单

以下区分**项目自有代码**、**运行时/系统前置依赖（不随包分发）**、**仅测试期 NuGet 依赖（不随包分发）**、**打包编译工具**以及**外部资源**。MIT 许可证仅适用于本项目原创代码，不改变任何第三方组件的原有许可证。

### 4.1 运行时、系统库、构建工具与资源清单

| 依赖或资源 | 类别 | 来源 | 许可证 | 是否随软件分发 | 处理方式 |
| --- | --- | --- | --- | --- | --- |
| 本项目源码、脚本、文档、插件清单 | 项目自有代码 | 本仓库 (`src/`, `tests/`, `scripts/`, `installer/`, `codex-plugin/`, `docs/`) | 拟采用 **MIT**（当前为 Proprietary 状态占位） | 是（源码公开 + 编译后的 `SelectionProbe.exe/.dll` 随发布包分发） | Phase 8A/8B 通过且维护者确认版权署名后更新根 `LICENSE` 与 `plugin.json` |
| .NET 10 / WPF Runtime | 运行时前置依赖 | Microsoft (`dotnet/runtime`, `dotnet/wpf`) | MIT | **否**（`--no-self-contained`，由用户系统单独安装） | 在 `README.md`、`INSTALLATION.md` 和 `THIRD-PARTY-NOTICES.md` 列为前置依赖并保留许可链接 |
| Windows API (`user32.dll`, `kernel32.dll`, `advapi32.dll`, UI Automation, `winsqlite3.dll`) | 操作系统系统库 | Microsoft Windows 10/11 | Windows 操作系统许可条款 | **否**（仅通过 P/Invoke / 系统组件调用，不随包附带任何 DLL） | 在 `THIRD-PARTY-NOTICES.md` 说明不重新分发系统 DLL |
| 系统字体名称 (`Microsoft YaHei UI`, `Segoe UI`, `Cascadia Mono`, `Consolas`) | UI 字体引用 | Windows 系统自带字体 | Windows 操作系统许可条款 | **否**（仅在 XAML/C# 中按名称引用，0 个字体文件随包分发） | 无需再分发授权；不打包字体文件 |
| 第三方图标、图片、音频或 UI 资产包 | 静态资源 | 无 | 不适用 | **否**（0 个第三方图标/图片文件） | 保持零外部静态资产 |
| Codex Desktop / `codex.exe app-server` | 外部宿主与 AI 服务 | OpenAI | 适用用户的 OpenAI 服务条款与隐私政策 | **否**（不随包附带任何 Codex 程序或凭据） | 在 `THIRD-PARTY-NOTICES.md`、`PRIVACY.md` 与 `README.md` 明确独立第三方边界 |
| Inno Setup 6.7.4 | 安装包编译器与安装程序存根 | Jordan Russell / Martijn Laan (`jrsoftware.org`) | **Inno Setup License**（已直接核对本地 `license.txt`：允许用于任何目的包括商业用途，要求保留版权与网站声明、不歪曲来源、修改版需标明） | **仅安装器存根**（`ContextNotesNative-0.3.0-setup.exe` 包含未修改的官方 Inno Setup 安装存根；便携 ZIP 不含 Inno Setup） | 未修改 Inno Setup 源码，未移除其版权/官网声明；在 `THIRD-PARTY-NOTICES.md` 明确安装器外壳受 Inno Setup License 约束，与项目 MIT 源码边界清晰区分 |

### 4.2 NuGet 依赖清单（基于 `obj/project.assets.json` 与本地 `.nuspec` 实测）

- **生产工程 `src/SelectionProbe/SelectionProbe.csproj`**：**0 个 NuGet 包**（无直接或传递 `PackageReference`）。
- **验收工具工程 `tests/HistoryDeletion.Acceptance/HistoryDeletion.Acceptance.csproj`**：**0 个 NuGet 包**。
- **单元测试工程 `tests/SelectionProbe.Tests/SelectionProbe.Tests.csproj`**：共 **12 个仅测试期 NuGet 包**（3 个直接引用 + 9 个传递依赖），均不进入 `dotnet publish` 产物、EXE 安装包或便携 ZIP：

| NuGet 包 | 版本 | 引用类型 | 本地 `.nuspec` 声明许可证 | 版权/作者 | 是否允许分发 / 是否随包分发 | 处理方式 |
| --- | --- | --- | --- | --- | --- | --- |
| `Microsoft.NET.Test.Sdk` | 18.11.0 | 直接（仅测试） | MIT | Microsoft Corporation | 允许；**不随发布包分发** | 记入 `THIRD-PARTY-NOTICES.md` |
| `Microsoft.CodeCoverage` | 18.11.0 | 传递（仅测试） | MIT | Microsoft Corporation | 允许；**不随发布包分发** | 记入 `THIRD-PARTY-NOTICES.md` |
| `Microsoft.TestPlatform.ObjectModel` | 18.11.0 | 传递（仅测试） | MIT | Microsoft Corporation | 允许；**不随发布包分发** | 记入 `THIRD-PARTY-NOTICES.md` |
| `Microsoft.TestPlatform.TestHost` | 18.11.0 | 传递（仅测试） | MIT | Microsoft Corporation | 允许；**不随发布包分发** | 记入 `THIRD-PARTY-NOTICES.md` |
| `xunit` | 2.9.3 | 直接（仅测试） | Apache-2.0 | .NET Foundation (`jnewkirk, bradwilson`) | 允许；**不随发布包分发** | 记入 `THIRD-PARTY-NOTICES.md` |
| `xunit.runner.visualstudio` | 3.1.5 | 直接（仅测试） | Apache-2.0 | .NET Foundation (`jnewkirk, bradwilson`) | 允许；**不随发布包分发** | 记入 `THIRD-PARTY-NOTICES.md` |
| `xunit.abstractions` | 2.0.3 | 传递（仅测试） | Apache-2.0（`licenseUrl` 指向 `xunit/master/license.txt`） | James Newkirk, Brad Wilson | 允许；**不随发布包分发** | 记入 `THIRD-PARTY-NOTICES.md` |
| `xunit.analyzers` | 1.18.0 | 传递（仅测试） | Apache-2.0 | .NET Foundation | 允许；**不随发布包分发** | 记入 `THIRD-PARTY-NOTICES.md` |
| `xunit.assert` | 2.9.3 | 传递（仅测试） | Apache-2.0 | .NET Foundation | 允许；**不随发布包分发** | 记入 `THIRD-PARTY-NOTICES.md` |
| `xunit.core` | 2.9.3 | 传递（仅测试） | Apache-2.0 | .NET Foundation | 允许；**不随发布包分发** | 记入 `THIRD-PARTY-NOTICES.md` |
| `xunit.extensibility.core` | 2.9.3 | 传递（仅测试） | Apache-2.0 | .NET Foundation | 允许；**不随发布包分发** | 记入 `THIRD-PARTY-NOTICES.md` |
| `xunit.extensibility.execution` | 2.9.3 | 传递（仅测试） | Apache-2.0 | .NET Foundation | 允许；**不随发布包分发** | 记入 `THIRD-PARTY-NOTICES.md` |

---

## 5. OpenAI 商标与产品命名边界审查

- `"OpenAI"`、`"ChatGPT"`、`"Codex"` 均为 OpenAI 的商标。本项目为独立开发的第三方 Windows 工具，**未与 OpenAI 关联，未获 OpenAI 赞助、背书或官方认证**。
- 现状核查：
  - GitHub 仓库名已采用中性名称：`x549d/context-notes-native`。
  - 安装包与插件名已采用中性名称：`Context Notes Native` / `context-notes`。
  - 历史文档标题与解决方案文件仍沿用 `ChatGPT Context Notes Native` / `ChatGptContextNotesNative.sln`。
- 处理建议：在公开版 `README.md`、`RELEASE-README.md` 和 OpenAI 申请材料中，将对外产品名称突出为中性名称 **Context Notes Native**（并可附注“a native Windows companion for Codex Desktop Chat”），同时保留完整独立性与商标免责声明，不擅自重命名 `.sln` 以免破坏既有构建脚本和历史引用。

---

## 6. MIT 授权可行性结论与待维护者确认事项

### 6.1 可行性判定

- **第三方许可冲突**：**无**（0 个 GPL/LGPL/AGPL 或其他不兼容依赖；0 个生产第三方包；Inno Setup License 允许自由分发生成的安装器）。
- **技术条件**：具备采用 MIT 许可证的全部技术与依赖前提。

### 6.2 正式修改 `LICENSE` 与 `PRIVACY.md` 前须由维护者确认的事项

1. **MIT `LICENSE` 版权持有人署名**：请确认 `Copyright (c) 2026 <版权持有人>` 应填写 `x549d` 还是维护者指定的真实法定姓名/组织名称（审计阶段不猜测维护者法定姓名）。
2. **权属声明确认**：确认全部 55 个由 `x549d` 提交的原创代码均由维护者独立拥有完整版权与开源授权权利，不存在雇主或第三方知识产权限制。
3. **`PRIVACY.md` 发布者与隐私联系方式**：请确认 `PRIVACY.md` 中的发布者展示名称（如 `x549d` 或真实姓名）与公开隐私联系渠道（如 GitHub Issues 链接或维护者指定公开邮箱），以替换当前 `[MAINTAINER CONFIRMATION REQUIRED]` 占位符。
