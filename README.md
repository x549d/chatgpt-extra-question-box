# ChatGPT Context Notes Native

在 **Windows 版 Codex 桌面客户端的 Chat 模式**中实现“划词追问”：选中助手回答里的一段文字，选区旁出现一个小按钮「提问」；点击后在选区旁直接弹出一个小型旁注框，自动引用选中文字，输入框自动获得焦点；用户直接输入问题并按 `Enter`，即可复用本机已登录的 Codex 账号在旁注小框内实时流式获得 AI 回答，并支持多轮连续追问与多个独立旁注。不需要复制文字、切换窗口、打开 Edge，也没有独立的聊天界面。

完整需求见 [docs/PROJECT-SPEC.md](docs/PROJECT-SPEC.md)。

## Phase 5 / 0.3.0 预览版

新增本地 SQLite 历史、恢复原 Thread、历史搜索/归档/回收站、Markdown 与代码复制、真实模型目录和用量状态。按 **Ctrl+Alt+H** 打开历史；数据库位于 `%LOCALAPPDATA%\ContextNotesNative\data`。退出不会清空问答，恢复不会自动重发问题。

- [使用说明](docs/USER-GUIDE.md) · [安装与更新](docs/INSTALLATION.md) · [故障排查](docs/TROUBLESHOOTING.md)
- [实现和测试记录](docs/PHASE-5-PRODUCT.md) · [安全说明](docs/SECURITY.md) · [隐私信息草案](PRIVACY.md)
- [最终交付与未完成项](docs/PHASE-5-ACCEPTANCE.md)：包括实际测试数量、部署状态、安装器受阻原因和桌面操作停止后的状态。
- 本阶段保留原有 39 项测试，当前共 **88 项 xUnit**；原有自测 **80 项**；隔离安装专项 **16 项**。隔离 Codex profile 不能替代干净 Windows 环境认证。
- 安装包由 `scripts/Build-Release.ps1` 生成到 artifacts，按当前用户安装。未签名，许可证沿用现有 manifest 的 Proprietary 状态，公开发布前需维护者确认法律文本与隐私联系信息。

Phase 4 下方的 39 项统计和 ephemeral Thread 描述属于历史基线；当前恢复行为以 Phase 5 文档为准。

5G～5J 已完成后台恢复、部分真实桌面验收、更新元数据显示与筛选控件对比度修正、SQLite 一致性升级备份和回滚失败保留、发布包审计。标准安装和完整工具浮窗 UI 仍有未通过/未测试项，最新状态见 [最终验收记录](docs/PHASE-5GJ-ACCEPTANCE.md)。发布产物仍为未签名 Preview，附 SHA256SUMS 与 release.json，不代表正式发布通过。

## 技术路线

- .NET 10 + WPF + Windows UI Automation（UIA）+ Codex App Server（stdio JSON-RPC 2.0）。
- UIA 读取真实 Codex 窗口里的选区文字和屏幕矩形，能保留连续空格、缩进和换行，因此**不使用剪贴板**。
- 「提问」入口按钮采用不抢焦点的透明 WPF 浮层（`WS_EX_NOACTIVATE`）；点击后打开的旁注小框（`AnnotationWindow`）绑定 Codex 主窗口为 Win32 Owner，支持完整键盘输入与中文输入法（IME）。
- AI 回答通过本机官方 `codex.exe app-server`（stdio JSON-RPC）复用当前 Windows 用户已登录的 ChatGPT 账号完成，**不依赖 Edge、不读取 Cookie 或进程内存、不存储任何明文 Token**。
- 后台与启动监视器在获取单实例锁前校验 `WinSta0\Default` 交互桌面；`codex.exe app-server` 子进程绑定 Win32 Job Object（`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`），防止任何孤儿进程残留。

## 阶段进度

| 阶段 | 内容 | 状态 | Commit / 报告 |
| --- | --- | --- | --- |
| Phase 0 | 可行性调查 | **PASS** | `733bdfb` · [PHASE-0-FEASIBILITY.md](docs/PHASE-0-FEASIBILITY.md) |
| Phase 1 | 真实选区捕获 | **PASS** | `f606855` · [PHASE-1-SELECTION-ACCEPTANCE.md](docs/PHASE-1-SELECTION-ACCEPTANCE.md) |
| Phase 2 | 选区旁浮动按钮（Codex 模式） | **PASS** | `53bf7c3` · [PHASE-2-OVERLAY-ACCEPTANCE.md](docs/PHASE-2-OVERLAY-ACCEPTANCE.md) |
| Phase 2R | Codex Chat 模式支持 + 常驻 + 插件化 | **PASS** | `7f7b19a` · [PHASE-2R-CHAT-ACCEPTANCE.md](docs/PHASE-2R-CHAT-ACCEPTANCE.md) |
| Phase 3 | 选区旁注小框（自动引用、自动聚焦、多旁注与生产桌面修复） | **PASS** | `04b1d35` · [PHASE-3-INLINE-ANNOTATION-ACCEPTANCE.md](docs/PHASE-3-INLINE-ANNOTATION-ACCEPTANCE.md) · [PRODUCTION-COMPANION-OVERLAY-REPAIR.md](docs/PRODUCTION-COMPANION-OVERLAY-REPAIR.md) |
| **Phase 4** | **Codex App Server 接入、流式 AI 回答、连续追问、多旁注隔离与取消生成** | **PASS（自测 80/80，xUnit 39/39，本机真实账号推理与连续追问通过）** | [PHASE-4-ACCEPTANCE.md](docs/PHASE-4-ACCEPTANCE.md) · [PHASE-4-APP-SERVER.md](docs/PHASE-4-APP-SERVER.md) · [APP-SERVER-ARCHITECTURE.md](docs/APP-SERVER-ARCHITECTURE.md) |

---

## Phase 4 已实现能力（2026-11-18）

详见：
- [docs/APP-SERVER-ARCHITECTURE.md](docs/APP-SERVER-ARCHITECTURE.md)（总体架构与协议流程）
- [docs/PHASE-4-APP-SERVER.md](docs/PHASE-4-APP-SERVER.md)（Phase 4A～4E 实现说明）
- [docs/PHASE-4-ACCEPTANCE.md](docs/PHASE-4-ACCEPTANCE.md)（测试矩阵、真实账号推理证据与部署核验）

| 子阶段 | 内容 | 状态 |
| --- | --- | --- |
| **Phase 4A** | `CodexAppServerClient`：异步 stdio JSON-RPC 客户端、分段/粘包流解析、多请求并发 ID 隔离、`initialize` / `account/read` / `model/list` / `thread/start`、Win32 Job Object 防孤儿进程 | **PASS** |
| **Phase 4B** | `AnnotationService`：连接 `AnnotationWindow` 发送事件与真实 App Server 请求，自动封装不可变引用原文与用户问题，管理 `Idle` / `Connecting` / `Generating` / `Completed` / `Cancelled` / `Failed` 状态 | **PASS** |
| **Phase 4C** | `item/agentMessage/delta` 实时流式增量接收与 30 ms 节流合并，`AnnotationWindow` 多轮问答气泡展示、自动滚动、`Enter` 发送与 `Shift+Enter` 换行 | **PASS** |
| **Phase 4D** | 同一旁注多轮连续追问（复用同一 `ThreadId` 保持上下文）、多旁注独立 `ThreadId` 与并发流式隔离、`ScopeFingerprint`（SHA-256）账号切换检测与隔离 | **PASS** |
| **Phase 4E** | 红色「停止」按钮与 `turn/interrupt` 服务端取消生成、迟到 `delta` 屏蔽、App Server 异常退出自动重连与历史重锚定、窗口关闭立即解绑 UI 回调 | **PASS** |

---

## 目录结构

```text
ChatGptContextNotesNative.sln   解决方案文件（含主程序与 xUnit 测试工程）
docs/                           需求规范、架构设计与各阶段验收报告
src/SelectionProbe/             .NET 10 WPF 后台进程
  CodexAppServerClient.cs       stdio JSON-RPC 2.0 客户端与子进程生命周期管理
  AnnotationService.cs          旁注 AI 会话协调、流式增量合并、取消与账号隔离
  AnnotationItem.cs             选区快照 SelectionSnapshot、旁注状态与多轮消息模型
  AnnotationWindow.xaml         旁注小框 WPF 界面（引用区、流式对话区、输入框、发送/停止按钮）
  AnnotationWindow.xaml.cs      旁注小框交互逻辑（自动聚焦、IME 处理、流式渲染、折叠/关闭/拖动）
  UiaSelection.cs               Chat 模式判断、角色识别、可视区与输入框裁剪
  UiaSelection.Inspect.cs       只读结构诊断（--inspect）
  ProbeMonitor.cs               前台 / 窗口 / 鼠标状态与采样调度
  ProbeWorker.cs                可超时的 UIA 工作进程
  OverlayHost.cs                WPF 浮层线程、按钮快照绑定、多旁注与 AI 服务托管
  OverlayPlacement.cs           按钮与旁注四侧定位、防重叠层叠及工作区裁剪
  Companion.cs                  常驻、单实例、交互桌面校验、--launch / --stop / --status
  Diagnostics.cs                只记录元数据的脱敏日志与桌面校验
  DesktopStartup.cs             当前用户的 Codex 前台启动监视器与热重启桥接
tests/SelectionProbe.Tests/     xUnit 单元测试工程（39 项测试覆盖 Phase 3 与 Phase 4）
codex-plugin/                   Codex 本地 marketplace 与插件（bin/ 由安装脚本生成，不入库）
scripts/Install-Plugin.ps1      发布程序到插件与缓存并热重启后台
scripts/Set-WindowsStartup.ps1  注册或注销当前用户的 Windows 启动补充路径
scripts/Test-HookDiagnostic.ps1 独立 Hook 诊断回归
scripts/Uninstall-Plugin.ps1
```

## 常用命令

```powershell
# Release 构建整个解决方案（0 警告、0 错误）
dotnet build ChatGptContextNotesNative.sln -c Release -p:NuGetAudit=false

# 运行 xUnit 单元测试（39/39 通过）
dotnet test ChatGptContextNotesNative.sln -c Release --no-build

# 运行内置自测（80/80 通过）
dotnet src/SelectionProbe/bin/Release/net10.0-windows/SelectionProbe.dll --self-test

# 运行本机真实 Codex App Server 端到端多轮追问与多旁注验证
dotnet src/SelectionProbe/bin/Release/net10.0-windows/SelectionProbe.dll --verify-app-server

# 构建插件、同步更新本地插件缓存并通过交互桌面监视器热重启后台
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-Plugin.ps1 -SkipMarketplace

# 查看后台进程与启动监视器状态 / 停止
dotnet src/SelectionProbe/bin/Release/net10.0-windows/SelectionProbe.dll --status
dotnet src/SelectionProbe/bin/Release/net10.0-windows/SelectionProbe.dll --watcher-status
dotnet src/SelectionProbe/bin/Release/net10.0-windows/SelectionProbe.dll --stop
```

## 安全约束

不注入 Codex 进程，不修改其安装文件，不读取进程内存、Cookie、密码或明文 Token。账号隔离仅使用不可逆的 SHA-256 作用域指纹。日志只记录状态、角色、矩形数量与坐标、文本长度、耗时和错误类型，**不记录选中文字、用户提问或 AI 回答正文**。
