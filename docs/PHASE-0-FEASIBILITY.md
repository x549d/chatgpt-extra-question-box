# Phase 0 · 技术可行性报告

验收日期为 2026-11-18（北京时间）。项目范围和阶段门槛见 [项目开发规范](PROJECT-SPEC.md)。

## 结论

当前 Windows 11 上的真实 Codex 桌面窗口可以通过 Windows UI Automation 读取非空文本选区和对应的屏幕矩形，也能收到选区变化事件。C#、WPF 和 .NET 10 的本机编译验证通过。可以提交 Phase 1 选区捕获原型计划供验收。原文逐字一致性、助手回答区域识别和复杂场景仍需验证。

同一 Windows 用户启动的独立 Codex App Server 以 stdio 工作，识别到已存在的 ChatGPT Plus 登录，并返回模型目录与限额窗口。桌面端账号身份比对和真实推理仍待验证。Phase 0 在只读查询结束后停止。

## 验收清单

| # | 检查项 | 结果 | 证据与范围 |
| --- | --- | --- | --- |
| 1 | Windows 版本 | PASS | 本机为 x64，24H2，构建号 26100.7623，属 Windows 11。注册表兼容字段 ProductName 显示旧的 Windows 10 名称，版本判定依据系统构建号。Windows 10 未上机验证。 |
| 2 | Codex 客户端版本 | PASS | 安装包标识显示 OpenAI.Codex 26.1111.2228.0 x64。独立 CLI 自报 codex-cli 0.169.0-alpha.11.1。 |
| 3 | Codex 进程与窗口 | PASS | 真实前台主窗口由安装包内的 ChatGPT.exe 持有，窗口类为 Chrome_WidgetWin_1，采样窗口 DPI 为 144（150%）。后台另有 codex.exe。Edge 处于前台时，Win32 检查返回 msedge，可与 Codex 区分。 |
| 4 | .NET SDK 和 WPF | PASS | 本机 .NET SDK 10.0.402，Windows Desktop Runtime 有 10.0.13 和 8.0.22，现有 WindowsDesktop 引用包为 10.0.13。带 WPF 和 UIA 类型引用的临时 net10.0-windows 工程离线构建成功，0 警告、0 错误。受限执行环境的 dotnet --info 和 NuGet 访问各有错误，构建在正常 Windows 用户环境通过。 |
| 5 | UI Automation 可访问性 | PASS | 从真实前台 HWND 取得 ControlType.Window，窗口内找到一个 ControlType.Document。先前焦点链为 Pane 和 Window，鼠标下的某条路径经 Group 到 Document。 |
| 6 | TextPattern | PASS | Document 公开 TextPattern，SupportedTextSelection 为 Single。基线时窗口内匹配到 15 个 TextPattern 提供者。TextPattern2 和原生 IAccessible 的运行时能力未测。 |
| 7 | 选区文本获取 | PASS | Document 的 GetSelection → GetText 先返回 0 长度范围，连续采样随后读到长度为 14 和 8 的两个非空范围。本项通过范围限于读取到非空选区。探针仅输出长度，文本逐字一致性未核对。 |
| 8 | 选区屏幕矩形 | PASS | 两个非空范围的 GetBoundingRectangles 均返回一个正尺寸矩形。同样长度的后续采样中矩形纵坐标发生变化。跨行矩形、受控滚动及多显示器定位尚未验收。 |
| 9 | 选区变化事件 | PASS | 在目标窗口子树注册 TextSelectionChangedEvent，35 秒窗口内收到 1 次事件。窗口处于前台期间另计到 1 次焦点事件。事件逐次覆盖率及切换或取消选区时的行为未测。 |
| 10 | Codex App Server | PASS | 实际版本能导出协议 schema。独立 stdio 子进程响应 initialize 和后续查询，关闭输入后子进程以状态 0 退出。线程、增量输出和中断只核对了 schema。 |
| 11 | 账号复用可行性 | PARTIAL | 普通 Windows 用户环境下，account/read 返回 chatgpt、plus 和非空账号标识。model/list 返回 10 条记录，account/rateLimits/read 返回主要限额窗口。未核对与桌面当前账号是否完全一致，也未运行真实推理。 |
| 12 | 官方桌面 UI 扩展接口 | PARTIAL | 核对了官方桌面、插件和 App Server 文档。公开能力包括技能、MCP、插件自有 UI 和服务协议。已查文档中未发现向现有 Codex 助手回答的文本选区注入菜单项或按钮的公开入口。 |
| 13 | 无 Edge 依赖 | PARTIAL | 选区验证走 Codex 桌面 HWND 和 UIA，账号查询走独立 App Server stdio，两条已测试路径均未调用 Edge、浏览器扩展或浏览器授权。完整无 Edge 工作流待 Phase 2–4 联调。 |

系统和 UIA 依据 [Windows 构建号](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview)、[.NET 支持矩阵](https://learn.microsoft.com/en-us/dotnet/core/install/windows)、[TextPattern](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.textpattern?view=windowsdesktop-10.0) 和 [GetBoundingRectangles](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.text.textpatternrange.getboundingrectangles?view=windowsdesktop-10.0)。公开扩展范围依据 [桌面客户端](https://learn.chatgpt.com/docs/app)、[插件能力](https://learn.chatgpt.com/docs/plugins) 和 [插件自有 UI](https://developers.openai.com/plugins/build/chatgpt-ui)。

## 真实选区证据

前台 HWND 由 Win32 检查取得。用户在桌面端操作选区，临时 C# 诊断只调用读取和事件订阅接口。UIA 调用在独立 MTA 进程中执行，使外部看门狗可以在超时时只结束探针。窗口查找限制在目标 HWND 范围内。探针在 40 秒内完成 61 次 Document 采样，结束时窗口仍为前台。单独的事件探针完成订阅和清理。

| 连续采样时间 | UTF-16 长度 | 屏幕矩形 (x, y, 宽, 高) |
| --- | --- | --- |
| 0.002 s | 14 | (1288, 705, 244, 29) |
| 4.614 s | 8 | (1553, 705, 168, 29) |
| 25.009 s | 8 | (1553, 590, 168, 29) |
| 26.328 s | 8 | (1553, 556, 168, 29) |
| 37.521 s | 8 | (1553, 558, 168, 29) |

旧原型曾在真实 Codex 上遇到 UIA 超时。本次采样完成，说明至少当前窗口和状态下存在可用路径。仍需监测调用卡住、控件重建、焦点脱离文档和选区失效等情况。TextPattern2、MSAA / IAccessible 只作备选调查，接口可用性及其对精确选区的作用尚未验证。 [UIA 线程模型](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading) · [事件订阅](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-eventsforclients) · [TextPattern2](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nn-uiautomationclient-iuiautomationtextpattern2) · [LegacyIAccessible](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-accessingmsaaservers) · [UIA 坐标与 DPI](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-screenscaling)

### 自动复制评估

**NOT TESTED。** 这次已取得直接 UIA 选区，自动复制没有启用，也没有做实际剪贴板实验。Windows 允许发布剪贴板内容的一方通过附加格式禁止历史记录和云同步。若复制由 Codex 发布，Companion 无法保证自己在系统或第三方剪贴板管理器读到内容之前附加标记。事后恢复可能覆盖并发复制，而且延迟呈现、自定义格式和所有权无法保证完整恢复。将来如需研究，先确认真实选区，再用无敏感样本文本验证一次性复制、并发更改保护、Windows 历史、云同步和第三方管理器的实际行为。坐标缺失仍须由独立的准确选区接口解决。 [Windows 剪贴板格式、历史和云同步](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats) · [剪贴板序列号](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboardsequencenumber)

## App Server 与认证

用安装在本机的 CLI 执行 codex app-server generate-json-schema，核对该二进制公布的方法和字段。当前 stdio 协议以换行分隔消息，initialize 请求成功后发送 initialized 通知。受限任务 shell 里子进程因找不到用户主目录在初始化前退出。用同一台机器的正常交互式 Windows 用户环境重试成功，账号由官方服务器解析。探针输出认证类型、计划和标识是否存在，邮箱与账号 ID 留在进程内。 [官方 App Server 文档](https://learn.chatgpt.com/docs/app-server) · [Codex 平台说明](https://developers.openai.com/blog/codex-as-a-platform) · [官方仓库](https://github.com/openai/codex)

| 方法或事件 | 本机 schema | 本次实测 |
| --- | --- | --- |
| initialize、initialized | 存在 | PASS，收到 Windows 平台响应，后续查询成功 |
| account/read | 存在，支持 refreshToken 字段 | PASS，以 false 查询得到现有 ChatGPT Plus 登录 |
| model/list | 存在，支持分页 | PASS，首个请求返回 10 条 |
| account/rateLimits/read | 存在 | PASS，返回主要限额窗口 |
| thread/start、thread/resume | 存在 | NOT TESTED |
| turn/start、turn/interrupt | 存在 | NOT TESTED，本机 schema 要求 interrupt 指定 threadId 和 turnId |
| item/agentMessage/delta、turn/completed | 存在 | NOT TESTED |

Phase 4 要在发起真实推理前比对桌面与独立进程的账号。在未授权工具的使用场景下，还要验证 Shell、MCP、插件、钩子、工作区指令和本地文件访问不会引入额外上下文或副作用。先在隔离工作目录用版本支持的配置关停相关能力，并验证默认继承的能力，避免只依赖提示词或审批设置。之后用无敏感测试文本验证流式完成、取消、连续追问与线程隔离。需把模型请求成功和限额查询成功分别记录。 [配置参考](https://learn.chatgpt.com/docs/config-file/config-reference) · [MCP 配置](https://learn.chatgpt.com/docs/extend/mcp)

当前路径没有要求额外登录。若后续证实桌面登录态无法复用，暂停认证接入。备选 A 是 App Server 的官方 ChatGPT 托管登录。备选 B 是在集成具备官方接入资格时使用 Sign in with ChatGPT，并经用户确认。备选 C 是用户明确授权的 OpenAI API Key，按 API 单独计费，也须经用户确认。 [App Server 认证](https://learn.chatgpt.com/docs/app-server#authentication) · [Sign in with ChatGPT 支持范围](https://learn.chatgpt.com/docs/plugins#connect-supported-partners-with-sign-in-with-chatgpt)

## Phase 1 待验收计划

1. 用 .NET 10 和 C# 建立最小选区原型。只在当前前台 HWND 的进程和安装身份符合 Codex 时启用，窗口类只用于辅助识别。
2. 以 Document 的 TextPattern 为主路径。用可超时重启的独立 MTA 进程处理查询，结合选区事件和限频回读判断选区有效性。滤除插入点、无可见矩形、失焦后的旧结果和输入框选区。用 UIA 元素范围核对能否稳定区分助手回答与其他区域。
3. 在真实桌面验证中文、英文、多行代码和多段选择，逐字对照测试样本并逐行比对矩形。验证取消、改选、滚动、失焦与恢复，检查冷启动和超时恢复，多屏与不同 DPI 条件允许时同时覆盖。按真实覆盖更新 Phase 1 验收报告并提交独立 Git commit。
4. 真实助手回答的原文和定位通过后才进入 Phase 2。若无法稳定获得有效选区或确认归属，记录复现条件和阻塞。自动复制需要单独完成剪贴板影响验证方可启用。

## 交付边界

本阶段新建独立 Git 仓库，提交文件为 docs/PROJECT-SPEC.md 和本报告。用 git log -1 --oneline 查看本阶段提交。诊断和生成的协议文件仅用于临时验证，仓库内尚无产品代码。旧项目仅用于只读参考。验证期间保留现有登录状态，Codex 与旧 Companion 继续运行。Phase 1 等待用户确认。
