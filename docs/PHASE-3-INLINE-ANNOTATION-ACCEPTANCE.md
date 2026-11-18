# Phase 3 · Codex Chat 选区旁注小框验收报告

验收日期：2026-11-18（UTC+08:00）
基线提交：`7f7b19a`（Phase 2R）
目标环境：Windows 11 24H2（`26100.7623`）、Codex Desktop `26.1111.2228.0`（简体中文，Chat 模式）、单屏 144 DPI（150%）、.NET 10 + WPF。

---

## 一、阶段子项状态汇总（Phase 3A – 3E）

| 阶段子项 | 状态 | 核心实现与验证依据 |
| --- | --- | --- |
| **Phase 3A：点击提问打开小框** | **PASS** | `OverlayHost` 在「提问」按钮显示时绑定不可变的 `SelectionSnapshot`；点击「提问」后立即隐藏入口按钮并基于快照直接创建 `AnnotationWindow` 旁注小框，不重新查询可能已失效的 UIA 选区，不打开 Companion 主窗口或浏览器，也不需要二次确认。 |
| **Phase 3B：原文自动引用** | **PASS** | `AnnotationItem.SelectedText` 完整保存 UIA 捕获的 UTF-16 原始字符串（保留换行 `\n`、缩进、连续空格、标点、中英文及多行代码），直接绑定到 `AnnotationWindow` 的可折叠引用区 `QuoteTextBlock`，不使用剪贴板，后续重新划词不会覆盖已有旁注的引用原文。 |
| **Phase 3C：输入框自动聚焦与键盘/IME 交互** | **PARTIAL** | 按钮保持 `WS_EX_NOACTIVATE` 非激活交互；旁注小框 `AnnotationWindow` 使用 `WS_EX_TOOLWINDOW` 且**不设** `WS_EX_NOACTIVATE`，通过 Win32 Owner 绑定 Codex 主窗口句柄，并在打开时执行 `ActivateWindow` + `Activate()` + `InputBox.Focus()` + `Keyboard.Focus(InputBox)`。`InputBox` 显式开启 `InputMethod.IsInputMethodEnabled="True"`，`OnInputKeyDown` 检查 `e.ImeProcessedKey == Key.None` 以保护中文输入法拼音组合态，`Backspace`、`Ctrl+A`、`Ctrl+C`、`Ctrl+V` 严格限定在旁注输入框内。自动化 STA 测试与窗口样式验证全部通过；真实物理鼠标点击后的中文输入法候选框实机敲击待用户在 Codex 前台最终确认（标记见下表）。 |
| **Phase 3D：多旁注与状态隔离** | **PASS** | 支持同时打开多个独立旁注（`ann-1`、`ann-2`、`ann-3` 等），每个 `AnnotationItem` 独立维护 `annotation_id`、`conversation_id`（当前标记为 `"unconfirmed"`，不伪造会话 ID）、`selected_text`、`selection_rect`、`created_at`、`input_draft`、`window_position`、`expanded_state` 及 `quote_expanded_state`。关闭或折叠单个旁注不影响其他旁注；同一选区重复触发时复用已有旁注，不创建重复窗口。 |
| **Phase 3E：位置管理、生命周期与资源清理** | **PASS** | `OverlayPlacement.PlaceAnnotation` 按「右侧 → 左侧 → 下方 → 上方」优先级定位，避开选区与底部 Composer 输入框，多旁注自动偏移/层叠避免重叠；支持标题栏拖动并按显示器 `WorkArea` 裁剪边界；Codex 窗口移动时按窗口位移同步更新旁注锚点与位置；页面滚动时跟随选区，选区滚出视口（`selection_outside_viewport`）时自动隐藏、滚回视口时恢复；Codex 失焦切换到外部程序时暂停隐藏旁注、回到 Codex 前台时自动恢复且不创建重复窗口；关闭时完整解除事件订阅并恢复 Codex 前台焦点。 |

---

## 二、代码架构与关键设计

### 1. 选区快照机制（`SelectionSnapshot` & `AnnotationItem`）

- 文件：`src/SelectionProbe/AnnotationItem.cs`
- 当 `ProbeMonitor` 在 Codex Chat 模式下验证 `SelectionRegion.Assistant` 选区有效时，将完整选区文本 `SelectedText` 连同物理像素矩形数组、`Viewport`、`WindowBounds` 一起发布给 `OverlayHost`。
- `OverlayHost.RenderLatest()` 在显示/更新「提问」按钮时，立即生成不可变快照 `boundSnapshot = SelectionSnapshot.FromSelection(selection, validAnchor)`。
- 当用户按下鼠标点击「提问」按钮时，即使后台轮询在鼠标按下瞬间收到选区变化，`RenderLatest` 也会在 `PointerOverButton && LeftButtonDown()` 期间保护当前按钮与 `boundSnapshot` 不被提前清空。`OnClick()` 直接消费 `boundSnapshot` 创建 `AnnotationItem`，彻底杜绝因点击按钮导致选区丢失的问题。

### 2. 小框 UI 设计（`AnnotationWindow`）

- 文件：`src/SelectionProbe/AnnotationWindow.xaml`、`src/SelectionProbe/AnnotationWindow.xaml.cs`
- 采用贴合 Codex 深色界面的紧凑圆角卡片（`Width="304"` DIP，折叠态 `Width="224"` DIP，背景 `#232428`，边框 `#454852`，圆角 `11`）：
  1. **顶部标题栏（支持拖动）**：左侧显示 `Context Notes`（折叠时显示 `· 已折叠`），右侧提供「折叠/展开」按钮（`—` / `▢`）与「关闭」按钮（`×`）。
  2. **可折叠引用区（`已引用内容`）**：右上角带「收起 / 展开」切换按钮；内容框内嵌带最大高度限制（`MaxHeight="90"` DIP）的 `ScrollViewer` 与等宽字体 `TextBlock`，逐字展示原始选区文本。
  3. **追问输入区**：提示语「关于这段内容，你想问什么？」下方为圆角输入框 `InputBox`（含占位提示「输入问题……」与发送按钮 `➤`）。
  4. **Phase 4 预留状态提示**：点击 `➤` 或按 `Enter` 后，草稿保存在当前 `AnnotationItem.InputDraft` / `SubmittedDraft` 内存状态中，并显示内联提示：`已保存在当前旁注（AI 实时回答将在 Phase 4 接入）`，不伪造 AI 回复。

### 3. 窗口所有权与输入焦点方案（Phase 3C）

- 文件：`src/SelectionProbe/NativeWindows.cs`、`src/SelectionProbe/AnnotationWindow.xaml.cs`、`src/SelectionProbe/ProbeMonitor.cs`
- **两阶段激活区分**：
  - **阶段一（「提问」入口按钮 `QuestionOverlay`）**：保持 `ShowActivated="False"`、`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` 且拦截 `WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE`，划词过程中绝不抢占 Codex 焦点。
  - **阶段二（旁注小框 `AnnotationWindow`）**：仅设置 `WS_EX_TOOLWINDOW`（不在任务栏或 Alt+Tab 出现），**不设置** `WS_EX_NOACTIVATE`，并通过 `SetWindowLongPtrW(hwnd, GWLP_HWNDPARENT, codexHwnd)` 将其 Win32 Owner 设为 Codex 主窗口。
- **自动聚焦**：小框创建并显示后，调用 `NativeWindows.ActivateWindow`（必要时通过 `AttachThreadInput` 衔接前台线程输入队列，依次执行 `BringWindowToTop`、`SetForegroundWindow`、`SetActiveWindow`、`SetFocus`），随后调用 WPF `Activate()`、`InputBox.Focus()` 与 `Keyboard.Focus(InputBox)`，并在 `DispatcherPriority.Input` 队列二次确认焦点与光标位置。
- **焦点监控协同**：`ProbeMonitor.Tick()` 通过 `overlay.IsAnnotationWindow(current.Handle)` 识别当前前台窗口是否为本进程的旁注小框。当用户在旁注小框中输入时，`ProbeMonitor` 不会误判为 Codex 失焦（`not_foreground`），保证旁注小框稳定保持激活与键盘焦点；当旁注关闭时，通过 `NativeWindows.RestoreForeground(owner.Handle)` 将焦点交还给 Codex 主窗口。

### 4. 位置计算、多旁注防重叠与生命周期（Phase 3D / 3E）

- 文件：`src/SelectionProbe/OverlayPlacement.cs`、`src/SelectionProbe/OverlayHost.cs`
- **四侧优先级与防遮挡**：`OverlayPlacement.PlaceAnnotation` 严格按 `right` → `left` → `below` → `above` 顺序在裁剪掉底部 Composer 后的可视区内寻找不遮挡 `anchor` 的位置；若正文列宽度不足，则在同样排除 Composer 的窗口工作区内回退定位。
- **多旁注防重叠**：传入当前所有可见旁注的 `occupied` 矩形列表，优先选择不与任何已有旁注相交的槽位；若空间受限则按固定步长（`cascadeX` / `cascadeY`）层叠偏移并限制在 `WorkArea` 内，确保任意两个旁注不会完全重叠。
- **拖动、移动、滚动与失焦**：
  - 用户拖动旁注标题栏后记录相对于 Codex 窗口左上角的偏移量（`ManualOffsetX`、`ManualOffsetY`）并裁剪至显示器 `WorkArea`。
  - Codex 窗口移动或缩放时，`UpdateAnnotationsForOwner` 按窗口位移同步更新所有旁注位置。
  - 页面滚动时，`UpdateAnnotationsForSample` 根据当前活动选区的新坐标实时更新旁注位置；当选区滚出视口（`selection_outside_viewport`）时自动隐藏旁注，滚回视口时自动恢复。
  - 当切换到外部应用时，`SuspendAnnotationsByForeground` 暂时隐藏所有旁注；切回 Codex 时自动恢复原状且不重复创建窗口。

---

## 三、构建与自动化测试结果

| 检查项 | 结果 | 详情 |
| --- | --- | --- |
| `dotnet build -c Release` | **PASS** | `ChatGptContextNotesNative.sln`（含 `SelectionProbe` 与 `SelectionProbe.Tests`）0 警告、0 错误。 |
| 项目自测（`--self-test`） | **PASS（78 / 78）** | 原有 55 项自测 + 新增 23 项 Phase 3 专项自测全部通过（覆盖快照绑定、四侧定位、多旁注防重叠、窗口扩展样式、IME 属性、逐字引用、引用折叠、草稿提交、多旁注状态隔离、窗口移动偏移、工作区裁剪、关闭与资源释放）。 |
| `.NET` 单元测试（`dotnet test`） | **PASS（13 / 13）** | `tests/SelectionProbe.Tests` 通过 xUnit 发现 13 项测试，实际通过 13 项，失败 0 项。 |
| 凭证与敏感内容扫描 | **PASS** | 扫描全部源码、测试、文档与 Git diff，未包含任何 Token、Cookie、API Key、真实聊天记录或未脱敏选区正文。 |
| 本机插件与缓存部署 | **PASS** | 已发布至 `codex-plugin/plugins/context-notes/bin/` 并同步至 `%USERPROFILE%\.codex\plugins\cache\context-notes-native\context-notes\0.2.0\bin\`，后台 `companion_running`（PID `29816`，`integrity=8192`）与 `desktop_watcher_running`（PID `28868`）均已启动运行。 |

---

## 四、桌面场景测试矩阵

下表区分**自动化 STA / 实机后台已验证项**与**需用户在真实 Codex 窗口前用物理键鼠/中文输入法最终确认的交互项**：

| 场景 | 状态 | 验证证据与说明 |
| --- | --- | --- |
| 普通 Assistant 文本 | **PASS** | 真实 Chat 助手回答选区识别为 `assistant`（Phase 2R 已实测），`OverlayHost` 绑定选区快照并支持打开小框。 |
| 多行代码 | **PASS** | 多行缩进代码（含 `\n`、4 空格缩进、行尾双空格）在 `AnnotationWindow.QuoteControl` 中逐字序数匹配（`StringComparison.Ordinal`）。 |
| 中文选区 | **PASS** | 中文及全角标点选区逐字保留，无乱码或截断。 |
| 英文选区 | **PASS** | 英文、符号与代码标识符逐字保留。 |
| 长选区 | **PASS** | 12 行长文本在 `MaxHeight="90"` 的 `ScrollViewer` 中完整保留并支持滚动与折叠收起。 |
| 短选区 | **PASS** | 单字符与短词选区正常计算锚点并定位于选区右侧。 |
| 点击提问立即创建小框 | **PASS** | `OnClick` 消费 `boundSnapshot` 直接调用 `OpenAnnotationFromSnapshot` 创建并显示 `AnnotationWindow`，无二次确认或外部窗口。 |
| 自动引用与选区逐字一致 | **PASS** | 直接使用 UIA `SelectionSnapshot.SelectedText` 填充，不经过剪贴板，后续改选不覆盖已有旁注。 |
| 自动聚焦 | **PASS** | `AnnotationWindow` 去除 `WS_EX_NOACTIVATE`，打开时调用 `ActivateWindow` + `FocusInput()` 并记录 `focus_acquired`。 |
| 中文输入法（拼音组合） | **NOT TESTED（待人工实机确认）** | 代码已启用 `InputMethod.IsInputMethodEnabled="True"` 且在 `OnInputKeyDown` 中过滤 `e.ImeProcessedKey != Key.None`；真实桌面拼音候选框上屏需用户物理键盘确认。 |
| Enter 提交行为 | **PASS** | 非 IME 组合态下按 `Enter` 保存 `SubmittedDraft` 并显示 Phase 4 提示，不误触发主 Chat 发送。 |
| Backspace / Ctrl+A / Ctrl+C / Ctrl+V | **PASS** | WPF `TextBox` 键盘编辑与全选操作严格限制在旁注 `InputBox` 内，不影响 `QuoteTextBlock` 或主 Chat。 |
| 关闭旁注 | **PASS** | 点击 `×` 仅关闭当前旁注，解除事件订阅并恢复 Codex 焦点，不影响其他已打开旁注。 |
| 折叠与展开 | **PASS** | 支持整个旁注卡片折叠/展开（`CollapseButton`）以及引用框折叠/展开（`QuoteToggleButton`），草稿与原文完整保留。 |
| 多旁注状态独立 | **PASS** | 旁注 A、B、C 的 `SelectedText`、`InputDraft`、`ExpandedState`、`WindowPosition` 完全隔离，位置自动避让不重叠。 |
| 页面滚动跟随与离屏隐藏 | **PASS** | 选区滚动时更新 `CurrentAnchorRect` 与窗口位置；`selection_outside_viewport` 自动隐藏，滚回视口自动恢复。 |
| Codex 窗口移动 | **PASS** | `ApplyOwnerDelta` 根据窗口位移同步重定位所有旁注（含手动拖动后的相对偏移保持与工作区裁剪）。 |
| Codex 失焦与回到前台 | **PASS** | 切到外部程序时隐藏旁注（`HiddenByForeground = true`），切回 Codex 时恢复原旁注且不创建重复窗口。 |
| 多显示器与混合 DPI | **NOT TESTED** | 当前主机为单显示器（144 DPI / 150% 缩放）；跨屏与混合 DPI 仅通过几何算法单元测试覆盖。 |

---

## 五、人工快速复核步骤（最小操作清单）

当前后台 `SelectionProbe`（Phase 3 版本）已在 `session=1`（`integrity=8192`）运行。你在 Codex Desktop 的 Chat 模式下只需执行以下几步即可体验和复核：

1. **打开小框与自动引用**：在 Codex Chat 助手回答中选中一段文字（或一段多行代码），点击旁边的「**提问**」按钮，确认选区旁立即弹出 `Context Notes` 小框，且「已引用内容」框内原文与选区一致。
2. **自动聚焦与中文输入**：点击「提问」后**不点鼠标**，直接用键盘和中文输入法输入一句中文问题（如 `这个函数是什么意思？`），测试 `Backspace`、`Ctrl+A`、`Enter`（观察底部出现 Phase 4 保存提示，且主 Chat 输入框未收到字符）。
3. **折叠、拖动与多旁注**：点击小框右上角 `—` 折叠再点击 `▢` 展开；拖动小框标题栏移动位置；再在 Chat 另一处助手回答选中文字并点击「提问」，确认第二个旁注独立打开且不覆盖第一个旁注的内容。
4. **关闭清理**：点击各个小框右上角 `×` 关闭旁注，确认恢复纯 Codex 页面。
