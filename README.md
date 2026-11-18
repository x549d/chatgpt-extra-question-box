# ChatGPT Context Notes Native

在 **Windows 版 Codex 桌面客户端的 Chat 模式**中实现“划词追问”：选中助手回答里的一段文字，选区旁出现一个小按钮「提问」；点击后在选区旁直接弹出一个小型提问框，自动引用选中文字，输入框自动获得焦点，可以立即针对这段内容输入问题。不需要复制文字、切换窗口、打开 Edge，也没有独立的聊天界面。

完整需求见 [docs/PROJECT-SPEC.md](docs/PROJECT-SPEC.md)。

## 技术路线

- .NET 10 + WPF + Windows UI Automation（UIA）。
- UIA 读取真实 Codex 窗口里的选区文字和屏幕矩形，能保留连续空格、缩进和换行，因此**不使用剪贴板**。
- 「提问」入口按钮采用不抢焦点的透明 WPF 浮层（`WS_EX_NOACTIVATE`）；点击后打开的旁注小框（`AnnotationWindow`）绑定 Codex 主窗口为 Win32 Owner，支持完整键盘输入与中文输入法（IME）。
- 不注入 Codex 进程，不修改安装文件，不读取进程内存。
- UIA 调用放在可超时、可重启的独立工作进程中（两秒看门狗），WPF 界面线程不做 UIA 查询。
- 以 Codex **本地插件**形式安装，可由 `SessionStart` 拉起后台；Windows 启动监视器覆盖冷启动后直接打开已有 Chat 的场景。
- AI 后端（Phase 4）计划复用本机 Codex App Server 的现有 ChatGPT 登录，不依赖 Edge 和浏览器扩展。

## 阶段进度

| 阶段 | 内容 | 状态 | Commit / 报告 |
| --- | --- | --- | --- |
| Phase 0 | 可行性调查 | 完成 | `733bdfb` · [PHASE-0-FEASIBILITY.md](docs/PHASE-0-FEASIBILITY.md) |
| Phase 1 | 真实选区捕获 | 完成 | `f606855` · [PHASE-1-SELECTION-ACCEPTANCE.md](docs/PHASE-1-SELECTION-ACCEPTANCE.md) |
| Phase 2 | 选区旁浮动按钮（Codex 模式） | 完成 | `53bf7c3` · [PHASE-2-OVERLAY-ACCEPTANCE.md](docs/PHASE-2-OVERLAY-ACCEPTANCE.md) |
| Phase 2R | Codex Chat 模式支持 + 常驻 + 插件化 | **PASS** | `7f7b19a` · [PHASE-2R-CHAT-ACCEPTANCE.md](docs/PHASE-2R-CHAT-ACCEPTANCE.md) |
| **Phase 3** | **选区旁注小框（自动引用原文、自动聚焦、多旁注与生命周期）** | **已完成（自测 78/78，xUnit 13/13，本机已部署）** | [PHASE-3-INLINE-ANNOTATION-ACCEPTANCE.md](docs/PHASE-3-INLINE-ANNOTATION-ACCEPTANCE.md) |
| Phase 4 | 接入本机 ChatGPT 登录 / App Server 实时回答 | 未开始 | — |
| Phase 5 | 连续追问、历史持久化与会话隔离 | 未开始 | — |

---

## Phase 3 已实现能力（2026-11-18）

详见 [docs/PHASE-3-INLINE-ANNOTATION-ACCEPTANCE.md](docs/PHASE-3-INLINE-ANNOTATION-ACCEPTANCE.md)。

| 子阶段 | 内容 | 状态 |
| --- | --- | --- |
| **Phase 3A** | 点击「提问」后基于按钮绑定的 `SelectionSnapshot` 立即在选区旁弹出 `AnnotationWindow` 小框，不重新查询可能失效的 UIA 选区，不打开 Companion 或浏览器 | **PASS** |
| **Phase 3B** | 自动引用选中文字（完整保留换行、缩进、连续空格、标点、中英文及多行代码），支持引用区折叠/展开，后续重新选区不覆盖已有旁注 | **PASS** |
| **Phase 3C** | 打开小框时通过 Win32 Owner + `ActivateWindow` + `Keyboard.Focus` 自动聚焦输入框；启用 `InputMethod.IsInputMethodEnabled` 并过滤 `ImeProcessedKey` 保护中文输入法组合态；支持 `Enter`、`Backspace`、`Ctrl+A/C/V` 严格限定在旁注输入框内 | **PARTIAL（自动化全通过，实机中文输入法待人工最终确认）** |
| **Phase 3D** | 支持多个独立旁注（`ann-1`、`ann-2`…）并存，各自独立保存 `selected_text`、`input_draft`、`expanded_state`、`window_position`；关闭或折叠互不干扰；位置自动避让防重叠 | **PASS** |
| **Phase 3E** | 右/左/下/上四侧优先级定位并避开底部 Composer；支持标题栏拖动与显示器工作区裁剪；Codex 窗口移动同步重定位；页面滚动跟随与离屏自动隐藏/恢复；Codex 失焦隐藏与回前台恢复且不重复建窗；关闭时完整释放事件与窗口资源 | **PASS** |

---

## 目录结构

```text
ChatGptContextNotesNative.sln   解决方案文件（含主程序与 xUnit 测试工程）
docs/                           需求规范与各阶段验收报告
src/SelectionProbe/             .NET 10 WPF 后台进程（选区采样、浮层、旁注小框、诊断）
  AnnotationItem.cs             选区快照 SelectionSnapshot 与旁注独立状态模型 AnnotationItem
  AnnotationWindow.xaml         旁注小框 WPF 界面（深色紧凑卡片、可折叠引用区、输入框）
  AnnotationWindow.xaml.cs      旁注小框交互逻辑（自动聚焦、IME 处理、折叠/关闭/拖动）
  UiaSelection.cs               Chat 模式判断、角色识别、可视区与输入框裁剪
  UiaSelection.Inspect.cs       只读结构诊断（--inspect）
  ProbeMonitor.cs               前台 / 窗口 / 鼠标状态与采样调度（识别旁注焦点）
  ProbeWorker.cs                可超时的 UIA 工作进程
  OverlayHost.cs                WPF 浮层线程、按钮快照绑定、多旁注管理与生命周期
  OverlayPlacement.cs           按钮与旁注四侧定位、防重叠层叠及工作区裁剪
  Companion.cs                  常驻、单实例、--launch / --stop / --status
  Diagnostics.cs                只记录元数据的脱敏日志
  DesktopStartup.cs             当前用户的 Codex 前台启动监视器
tests/SelectionProbe.Tests/     xUnit 单元测试工程（覆盖 Phase 3A–3E）
codex-plugin/                   Codex 本地 marketplace 与插件（bin/ 由安装脚本生成，不入库）
scripts/Install-Plugin.ps1      发布程序到插件与缓存并注册 marketplace
scripts/Set-WindowsStartup.ps1  注册或注销当前用户的 Windows 启动补充路径
scripts/Test-HookDiagnostic.ps1 独立 Hook 诊断回归
scripts/Uninstall-Plugin.ps1
```

## 常用命令

```powershell
# Release 构建整个解决方案（0 警告、0 错误）
dotnet build ChatGptContextNotesNative.sln -c Release -p:NuGetAudit=false

# 运行 xUnit 单元测试（13/13 通过）
dotnet test ChatGptContextNotesNative.sln -c Release --no-build

# 运行内置自测（78/78 通过）
dotnet src/SelectionProbe/bin/Release/net10.0-windows/SelectionProbe.dll --self-test

# 构建插件并同步更新本地插件缓存
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-Plugin.ps1 -SkipMarketplace

# 查看后台进程与启动监视器状态 / 停止
dotnet src/SelectionProbe/bin/Release/net10.0-windows/SelectionProbe.dll --status
dotnet src/SelectionProbe/bin/Release/net10.0-windows/SelectionProbe.dll --watcher-status
dotnet src/SelectionProbe/bin/Release/net10.0-windows/SelectionProbe.dll --stop
```

## 安全约束

不注入 Codex 进程，不修改其安装文件，不读取进程内存、Cookie、密码或 Token。日志只记录状态、角色、矩形数量与坐标、文本长度、耗时和错误类型，**不记录选中文字或用户输入问题正文**。测试时不在真实 Chat 中输入或发送内容。
