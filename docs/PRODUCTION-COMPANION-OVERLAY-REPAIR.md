# 生产 Companion 浮层故障修复

日期为 2026-11-18，时间为 UTC+08:00。基线为 `56b4b6c`，`main` 开始时干净。客户端为 Windows Codex Desktop `26.1111.2228.0`，本机插件为 `0.2.0`。

## 根因与对照

故障版本的部署 DLL 为 156160 字节，SHA-256 为 `5FBAC36A5D28E83AC1B44C42049D78A59488507B56BCD5111C885D5530C708EF`。部署的 DLL、EXE、运行配置及依赖清单与当时的 Release 逐项相符。

旧 Companion PID `29816` 和 Watcher PID `28868` 使用 `dotnet` 运行。它们与 Codex PID `15744` 同属 Windows 会话 1，令牌登录会话标识相同。然而，从正常交互桌面用公开的 `GetThreadDesktop` 查询时，两个旧后台的全部线程均返回 `ERROR_ACCESS_DENIED`。同一调用器能够看到 Codex 的交互线程在 `Default`。Windows 会话 ID 和项目命名对象存在只能证实后台进程在运行，无法证实后台连接到了 Codex 所在的输入桌面。[Windows 窗口站连接](https://learn.microsoft.com/en-us/windows/win32/winstation/process-connection-to-a-window-station) [GetThreadDesktop](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getthreaddesktop)

交互桌面的 `--inspect` 在 17:49:23 读取了 102 个 UTF-16 码元、五个可用选区矩形及 `Assistant` 分类。检查正常用户的伴随日志时，最后一条可见记录截至 17:21:37。该日志中缺少后续选区事件，单独看它无法判断旧进程执行到了哪一步。

先正常停止两个旧进程，保留相同二进制，从 `WinSta0\Default` 重新启动。18:03:47 的对照 Companion PID 为 `33324`，Watcher PID 为 `29364`。18:04:36 识别同一个 Codex 窗口并启动 UIA 工作进程，18:04:37 订阅成功。18:04:48 识别真实助手选区且判定 `eligible`，18:04:49 在 `[2044,975,99,45]` 显示入口，用户确认按钮出现。此前后唯一改变是后台的启动上下文。旧进程具体创建者的桌面名称不可读，故障定位到旧后台无法接入正常的交互桌面。

## 三条执行路径

| 模式 | 调用链与查验 |
| --- | --- |
| `--inspect` | `Program.Main` → `InspectRunner.Run` → 独立 `ProbeWorker` → `Operation.Inspect` → `UiaSelection.Inspect`。每轮间隔约 700 ms，独立请求上限 8 秒。仅记录 UIA 结构，直接调用分类和矩形读取。 |
| `--companion` | `Program.Main` → `Companion.Run` → STA `OverlayHost` 与 `ProbeMonitor.RunAsync` → `ProbeWorker.Operation.Sample` → `UiaSelection.Read` → `ProbeMonitor.Record` → `OverlayHost.Publish` → `RenderLatest` → `QuestionOverlay`。 |
| `--watch-codex` | `Program.Main` → `DesktopStartup.Watch` → 校验前台程序包身份 → `Companion.LaunchFromDesktop`。Watcher 驱动后台启动，选区采集和浮层由独立 Companion 实例负责。 |

`ProbeMonitor.Tick` 约每 55 ms 检查状态。在窗口和选区稳定时约每 180 ms 发出一次采样。首轮样本后，`UiaSelection.Subscribe` 在目标窗口子树上注册 `TextSelectionChangedEvent`，通过计数器反映通知。选区内容始终来自轮询工作的 `Sample` 回复，即使订阅失败仍可采样。单次请求 2000 ms 超时后终止并替换工作进程。正常释放时注销 UIA 回调，工作进程退出时其订阅随之释放。渲染和按钮生命期由独立 WPF Dispatcher 管理。

`UiaSelection.Read` 和 `Inspect` 复用 Chat 识别及角色分类。生产 `Sample` 检查前台窗口、非空文本和可见矩形。`ProbeMonitor` 以角色、视口和窗口几何生成 `OverlaySelection`；渲染线程作废过期样本后，用实际定位锚点生成 `SelectionSnapshot` 并绑定按钮。原文只在工作进程协议和内存对象中流转。`ConversationId` 在创建 `AnnotationItem` 时才赋值，入口可见性与它无关。点击沿用原有快照、焦点和多旁注路径。

## 可见性判定与诊断

运行入口在占用单实例互斥锁前校验 `WinSta0\Default`。其他桌面以退出码 4 拒绝并记录 `startup_rejected`。日志优先写入本地正常配置目录，无法打开文件时尝试临时目录，再使用标准错误记录同一条脱敏元数据。状态与停止入口仍可检查、清理旧进程。

| 决策点 | 隐藏或跳过的原因 |
| --- | --- |
| `ProbeMonitor.Tick` | 第一次检查及窗口变化时记录 `foreground_classified`。没有前台窗口为 `no_foreground_window`，非 Codex 前台为 `not_foreground`。旁注输入窗口获得焦点时隐藏入口，既有旁注可继续输入。防抖等待和鼠标拖选期间延迟采样。 |
| `ProbeMonitor.Receive` | 前台变化、窗口变化、鼠标拖选和选区代次变化可丢弃过期回复。`selection_sample_dropped` 记录原因。UIA 超时、启动失败和协议错误记录 `probe_request_failed` 及异常类型。 |
| `ProbeMonitor.Record` | 记录 Chat 模式、角色、文本长度、矩形数、资格以及事件计数变化。角色不符、空文本、无效矩形、无效视口和窗口移动分别报告原因。轮询读取与实际 UIA 事件分别记录。 |
| `OverlayHost.PublishBlocker` | Dispatcher 接收快照时检查代次、前台窗口、窗口位置及是否正在拖选。已释放实例拒绝新的发布请求。 |
| `OverlayHost.RenderLatest` | 无选区时按传入的原因隐藏。实际按住入口时记录 `button_press_in_progress`，延后隐藏以保留点击。无可见显示区、目标坐标不合格、定位 API 失败或窗口构建失败分别记录原因。 |
| `QuestionOverlay` | WPF 非激活工具窗口，初始 `Topmost`、`ShowActivated=False`、`WS_EX_NOACTIVATE`，由现有屏幕像素与 DPI 转换定位。入口 HWND 独立于 Codex，旁注窗口拥有既有的 Codex Owner。`overlay_shown` 只在 Win32 确认可见，且实际边界在工作区内并避开选区时记录。 |
| `OverlayHost.OnClick` | 非预期前台或入口意外激活时记录 `overlay_click_ignored`。合格点击复用绑定快照，创建的旁注记录窗口可见、原文逐字匹配和焦点请求状态。 |

生产诊断行含时间戳、PID、模式、临时选区 ID、长度、矩形数、角色、资格原因、可见状态及错误类型。`selection_event_received` 代表实际收到 UIA 事件，`selection_text_read` 可由轮询产生。原文和用户问题仅在内存中传递。

## 真实生产验收

以下记录来自部署后的 `--companion` PID `28388`。用户确认按钮、旁注、完整引用和拼音输入正常，并完成了多行代码、用户消息与输入框草稿的复测。

| 场景 | 结果 | 证据 |
| --- | --- | --- |
| 普通助手文字与中文 | PASS | 18:19:14，选区 `2` 记录 `selection_event_received`、`selection_text_read`、`selection_role_classified` 及 `selection_eligible`，共 24 个 UTF-16 码元、1 个矩形。 |
| 快照、浮层创建及实际显示 | PASS | 同一选区 `2` 连续出现 `overlay_created`、`selection_snapshot_bound`、`overlay_shown`。实际入口为 `[1866,1095,99,45]`，正文视口为 `[1134,504,1249,810]`，Win32 证实入口可见。 |
| 多行代码与重新划选 | PASS | 18:21:30，选区 `3` 识别为助手，90 个 UTF-16 码元、4 个矩形。按钮在选区上方 `[1206,1163,99,45]` 显示。 |
| 用户消息排除 | PASS | 18:21:34，选区 `4` 识别为 `user`，记录 `overlay_hidden` 及 `role_user`。 |
| 输入框草稿排除 | PASS | 18:21:38，选区 `5` 识别为 `composer`，记录 `overlay_hidden` 及 `role_composer`。 |
| 空选区与离屏 | PASS | 18:18:58，离屏助手选区报告 `selection_outside_viewport` 并隐藏。18:21:43，空选区报告 `selection_empty` 并保持隐藏。 |
| 物理点击和旁注 | PASS | 18:19:15 的用户实鼠点击记录 `has_snapshot=True` 及 `codex_foreground=true`。随后的 `annotation_opened` 显示旁注 HWND 可见。 |
| 原文引用 | PASS | `annotation_opened` 的逐字匹配 `quote_exact=True`，并经用户确认。 |
| 自动聚焦及中文输入法 | PASS | `focus_acquired keyboard_focused=True`，用户确认直接拼音输入正常。旁注关闭时记录 `annotation_closed` 并释放焦点。 |
| 多显示器、混合 DPI、Windows 重新登录 | NOT TESTED | 本轮基于现有单显示器交互桌面，复验的是生产运行链路。 |

```text
18:19:14.308  selection_event_received / selection_role_classified / selection_eligible  selection_id=2
18:19:14.738  overlay_created           selection_id=2
18:19:14.759  selection_snapshot_bound  selection_id=2
18:19:14.803  overlay_shown             selection_id=2
18:19:15.321  annotation_entry_clicked  codex_foreground=true has_snapshot=True
18:19:15.496  focus_acquired            keyboard_focused=True
18:19:15.506  annotation_opened         selection_id=2 annotation_visible=True quote_exact=True focus_requested=True
```

## 构建、部署和回归

| 项目 | 结果 |
| --- | --- |
| Release | PASS，0 个警告、0 个错误。 |
| 项目自测 | PASS，78/78。 |
| xUnit | PASS，27/27。保留原 13 项，新增生产快照链路、各资格条件、桌面判定及异常脱敏。 |
| 非默认桌面实测 | PASS。`--companion`、`--watch-codex` 和 `--launch-watcher` 均返回退出码 `4`，伴随及 Watcher 状态返回 `3`。标准日志文件不可写时，诊断规范地落入临时目录。 |
| 部署 | PASS。18:17:00 部署并验证四个文件的 SHA-256 一致。DLL 为 `73A9C0CD1989E6573B8E648AF6A907DE09FE050BABDD8F8739C2B5E73C63BA34`。 |
| 运行及单实例 | PASS。当前启动的 Companion PID `28388` 与 Watcher PID `38868` 均进入运行状态，重复启动上述两种模式均正常退出。 |
| 凭证及提交范围 | PASS。已检查 Git 新增行与跟踪文件，生产日志、用户正文及构建产物未纳入提交。 |

Phase 2R 的选区与自动启动路径继续沿用。Phase 3 的 `AnnotationWindow`、多旁注、引文和输入焦点逻辑保留，新增逐字比对状态供生产复核。
