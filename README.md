# Context Notes Native

> A native Windows companion for **Codex Desktop Chat** — attach contextual notes to selected assistant responses, preserve exact quoted content, ask multi-turn follow-up questions in independent annotation windows, and maintain persistent local SQLite conversation history.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows 10/11 x64](https://img.shields.io/badge/Platform-Windows%2010%2F11%20x64-0078D6.svg)](#系统要求--system-requirements)
[![.NET 10 WPF](https://img.shields.io/badge/.NET-10%20WPF-512BD4.svg)](#系统要求--system-requirements)

> **独立第三方声明 / Independence Notice**：本项目（原开发代号 `ChatGPT Context Notes Native`）是采用 **MIT 许可证**的独立开源 Windows 桌面工具，**与 OpenAI 无关联，未获 OpenAI 官方认证、赞助或背书**。"OpenAI"、"ChatGPT" 与 "Codex" 为 OpenAI 的注册商标。

---

## 项目介绍 / Overview

**Context Notes Native** 专为 Windows 版 **Codex Desktop** 的 **Chat 模式**设计，实现原生、低侵入的“划词旁注追问”体验：

- 在助手回答中选中任意一段文本或代码块；
- 选区旁立即出现不抢焦点的「提问」浮动按钮；
- 点击后在选区旁弹出独立旁注小框，自动原样引用选中文本并聚焦输入框；
- 输入追问并按 `Enter`，直接复用本机官方 Codex App Server 实时流式获取 AI 回答，无需复制粘贴、切换标签页或离开当前上下文。

## 核心功能 / Features

1. **精准原生选区捕获**：基于 Windows UI Automation（UIA）直接读取 Codex Chat 助手回答的选区文本与屏幕坐标，完整保留连续空格、缩进与换行，**不占用系统剪贴板**。
2. **不抢焦点入口按钮**：选区旁浮动显示「提问」按钮（`WS_EX_NOACTIVATE`），不打断阅读与滚动。
3. **选区旁独立旁注小框**：点击按钮打开贴近选区的旁注窗口，自动引用选中原文、自动聚焦输入框，完整支持中文输入法（IME）、拖动定位、折叠与关闭。
4. **官方 App Server 流式回答**：通过本机官方 `codex.exe app-server`（stdio JSON-RPC 2.0）获取真实 AI 回答，实时流式渲染 Markdown 与代码块（支持代码原样复制、折叠与安全 HTTP(S) 链接）。
5. **连续多轮追问与多旁注并发**：每个旁注拥有独立的 `ThreadId`，支持同一旁注多轮上下文追问与多个旁注同时独立流式生成。
6. **生成取消与异常恢复**：支持点击「停止」实时中断服务端生成（`turn/interrupt`），支持 App Server 异常重启后的自动恢复与上下文重锚定。
7. **本地 SQLite 持久化历史**：按 `Ctrl+Alt+H` 打开历史管理窗口，支持按账号隔离、关键词搜索、日期筛选、重命名、归档、软删除回收站、恢复原 Thread、确认永久删除与一致性在线备份。
8. **模型目录与用量查看**：直接读取 App Server 提供的模型目录与限额快照，可为当前账号设置默认模型或为单个旁注选择模型。
9. **双分发形态**：提供免管理员权限的 Windows 标准安装包（`setup.exe`）与绿色便携压缩包（`portable.zip`），附带 `SHA256SUMS`。

## 工作流程示意 / Workflow Demo

```text
[Codex Desktop Chat 助手回答]
  │  1. 鼠标划选任意文字/代码块（不占用剪贴板）
  ▼
[选区旁浮动「提问」按钮]  (不抢焦点 WS_EX_NOACTIVATE)
  │  2. 点击「提问」
  ▼
[独立旁注小框 AnnotationWindow]
  ├── 原文引用区（原样保留空格/缩进/换行，可折叠）
  ├── 多轮流式问答区（Markdown / 代码复制 / 独立 ThreadId）
  └── 输入框（自动聚焦 / 中文 IME / Enter 发送 / Shift+Enter 换行 / 停止生成）
  │  3. 自动持久化至本地 SQLite；随时 Ctrl+Alt+H 搜索、恢复或归档
  ▼
[%USERPROFILE%\.context-notes-native\data\history.db]
```

> 注：本项目当前以纯代码与文档发布，未在仓库中打包体积较大的演示图片或录屏二进制文件；后续 Release 说明与文档将按需补充轻量截图演示。

## 系统要求 / System Requirements

- **操作系统**：Windows 10（2004 / Build 19041 及以上）或 Windows 11（64 位 `x64`）
- **运行库**：[.NET 10 Windows Desktop Runtime x64](https://dotnet.microsoft.com/)（框架依赖部署，需单独安装）
- **宿主客户端**：已安装并登录的 Windows 版 **Codex Desktop**（提供官方 `codex.exe app-server`）
- **权限要求**：仅需当前普通 Windows 用户权限（**无需管理员权限**，不写 `HKLM`）

## 安装方式 / Installation

前往本仓库的 [GitHub Releases](https://github.com/x549d/context-notes-native/releases) 下载对应版本的安装包与 `SHA256SUMS`：

### 方式一：Windows 标准安装包（推荐）

1. 下载 `ContextNotesNative-0.3.0-setup.exe` 和 `SHA256SUMS`。
2. 校验文件完整性（PowerShell）：
   ```powershell
   Get-FileHash .\ContextNotesNative-0.3.0-setup.exe -Algorithm SHA256
   ```
   核对输出结果与 `SHA256SUMS` 中对应行一致。
3. 将安装包保存在普通用户目录（如「下载」或「桌面」），双击运行按向导安装即可。
   - 默认安装目录：`%LOCALAPPDATA%\Programs\ContextNotesNative\versions\0.3.0`
   - 自动通过官方 Codex CLI 注册本地插件 marketplace 与 `SessionStart` Hook，并创建当前用户启动监视器快捷方式。
   - 卸载：在 Windows「设置 → 应用 → 已安装的应用」中卸载，**默认保留本地历史数据库**。

> **关于 Windows SmartScreen / “未知发布者”警告**：`0.3.0` 安装器与可执行文件目前为**未签名发布（`NotSigned`）**，运行 `setup.exe` 时 Windows 可能弹出未知发布者安全提示。运行前请务必对照 `SHA256SUMS` 校验完整性，亦可阅读全部开源代码并自行从源码编译。签名计划见 [docs/SIGNING-PLAN.md](docs/SIGNING-PLAN.md)。

### 方式二：Windows 绿色便携包

1. 下载 `ContextNotesNative-0.3.0-portable.zip` 并对照 `SHA256SUMS` 核对哈希。
2. 将压缩包解压到将长期保留的用户目录。
3. 在该目录打开交互式 PowerShell，执行：
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Manage-Installation.ps1 -Action Install
   ```
4. 如需取消注册并移除便携版（默认保留历史）：
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Manage-Installation.ps1 -Action Uninstall
   ```

### 方式三：从源码自行构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/)：

```powershell
# Release 构建（0 警告、0 错误）
dotnet build ChatGptContextNotesNative.sln -c Release -p:NuGetAudit=false

# 运行全部 106 项 xUnit 单元测试
dotnet test ChatGptContextNotesNative.sln -c Release --no-build

# 运行内置 80 项自测
dotnet src/SelectionProbe/bin/Release/net10.0-windows/SelectionProbe.dll --self-test

# 安装本地插件并热重启后台
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-Plugin.ps1 -SkipMarketplace
```

详细安装、升级、回滚与卸载说明见 [docs/INSTALLATION.md](docs/INSTALLATION.md)。

## 使用方法 / Usage

1. 打开 Windows 版 **Codex Desktop** 并切换到 **Chat 模式**。
2. 用鼠标选中助手回答中的任意片段，选区旁出现「提问」按钮。
3. 点击「提问」打开旁注小框，直接输入中文或英文问题：
   - `Enter`：发送问题
   - `Shift+Enter`：输入框换行
   - 「停止」按钮：取消正在进行的流式生成
   - 「模型与用量」：查看/切换 App Server 提供的模型目录与限额信息
4. 按 **`Ctrl+Alt+H`**（或点击旁注窗口的「历史」按钮）打开本地历史窗口，可搜索、归档、恢复历史旁注或管理回收站。
5. 查看后台状态或完整停止：
   ```powershell
   SelectionProbe.exe --status
   SelectionProbe.exe --watcher-status
   SelectionProbe.exe --stop-watcher
   SelectionProbe.exe --stop
   ```

完整使用指南见 [docs/USER-GUIDE.md](docs/USER-GUIDE.md)。

## 常见问题 / FAQ

- **为什么运行 `setup.exe` 时 Windows 提示“未知发布者”？**
  本版本尚未配备商业可信代码签名证书（`NotSigned`）。请先使用 `SHA256SUMS` 校验下载文件哈希，或直接审阅源码从本地构建。详见 [docs/SIGNING-PLAN.md](docs/SIGNING-PLAN.md)。
- **为什么在某些沙箱/低完整性目录运行安装器会报 Error 5？**
  若文件继承了 Windows `Low Mandatory Level` SACL，Windows MIC 会阻止其写入 `%TEMP%` 与 `HKCU`。请将安装器复制到普通用户目录（如「下载」或「桌面」）后重新运行，无需管理员权限或修改系统 ACL。
- **选中文字后没有出现「提问」按钮？**
  请确认当前处于 Codex Desktop 的 **Chat 模式** 且选中的是**助手回答**（用户消息和输入框草稿不会触发）；若仍未出现，可检查 `SelectionProbe.exe --status` 与 `--watcher-status`，或在 Codex 插件设置中确认 Hook 信任状态。
- **卸载会删除我的历史记录吗？**
  不会。标准卸载和便携卸载**默认保留** `%USERPROFILE%\.context-notes-native\data\history.db` 及其备份；仅当您在交互式卸载中明确选择删除本地历史时才会移除。

更多排查步骤见 [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md)。

## 数据存储位置 / Data Storage

所有本地运行数据均保存在当前 Windows 用户目录，不上传至第三方自建服务器：

| 类型 | 路径 | 说明 |
| --- | --- | --- |
| 历史数据库 | `%USERPROFILE%\.context-notes-native\data\history.db` | 保存引用原文、提问、回答、草稿、模型选择、账号作用域指纹、ThreadId 与窗口位置（SQLite WAL 模式，无应用层加密） |
| 自动与恢复备份 | `%USERPROFILE%\.context-notes-native\backups\` | 退出自动备份、升级前在线备份与手工导出备份 |
| 诊断日志 | `%USERPROFILE%\.context-notes-native\logs\` | 仅含运行状态与元数据，不记录完整对话正文 |

## 隐私与安全说明 / Privacy & Security

- **AI 云端处理**：本机 `codex.exe app-server` 是官方桥接进程，**不是完全离线 AI**。提交问题时，选中的引用原文、用户提问及恢复对话所需的上下文会进入对应 AI 服务流程并发送至 OpenAI 云端处理，受用户自身 OpenAI 账号服务条款与[隐私政策](https://openai.com/policies/privacy-policy/)约束。
- **零凭据窃取、零遥测**：不注入 Codex 进程，不读取浏览器 Cookie、进程内存、密码或明文 Token，不包含任何 `HttpClient`/`WebRequest`/`Socket` 或遥测统计。
- **生产零第三方依赖**：主程序 `SelectionProbe` 含 **0 个生产 NuGet 包**，仅调用 .NET 10 / WPF 与 Windows 系统库（含系统自带 `winsqlite3.dll`）。
- 完整隐私政策见 [PRIVACY.md](PRIVACY.md)，安全架构与漏洞报告见 [SECURITY.md](SECURITY.md) 与 [docs/SECURITY.md](docs/SECURITY.md)。

## 测试与审计现状 / Verification Status

- **已验证通过**：Release 0 警告/0 错误、106 项 xUnit 单元测试、80 项内置自测、29 项标准 EXE 隔离安装/升级/回滚/卸载验证、19 项便携安装管理验证、23 项独立合成库永久删除验证、三浮窗真实并发与重启恢复、30 分钟连续稳定性采样（61 样本、0 重启、0 错误日志）、以及完整 Git 历史与发布包安全审计。
- **已知未覆盖项**：独立干净 Windows OS（全新虚拟机/云主机）全流程安装验收当前标记为 **`NOT TESTED`**；可信 Authenticode 代码签名当前为 **`NotSigned`**。

## 目录结构 / Repository Layout

```text
ChatGptContextNotesNative.sln   .NET 10 解决方案（主程序 + xUnit 测试）
src/SelectionProbe/             WPF / UIA / Win32 / SQLite / App Server 主程序
tests/SelectionProbe.Tests/     xUnit 单元测试工程（106 项）
tests/HistoryDeletion.Acceptance/ 独立合成库永久删除验收工具（23 项）
codex-plugin/                   Codex 本地插件清单与 SessionStart Hook
installer/ContextNotes.iss      Inno Setup 当前用户安装脚本
scripts/                        构建、安装、卸载、诊断与自动化验证脚本
docs/                           架构文档、用户指南、安全/许可清单与各阶段验收报告
```

## 参与贡献与许可证 / Contributing & License

- 贡献指南：[CONTRIBUTING.md](CONTRIBUTING.md)
- 变更日志：[CHANGELOG.md](CHANGELOG.md)
- 第三方组件与商标声明：[docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md)
- 开源许可证：本项目自有代码采用 **[MIT License](LICENSE)**（Copyright (c) 2026 x549d）。
