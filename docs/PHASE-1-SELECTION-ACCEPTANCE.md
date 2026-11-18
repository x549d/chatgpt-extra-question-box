# Phase 1 · 选区捕获原型验收

验收于 2026-11-18（北京时间）。基线为 733bdfb，目标框架为 net10.0-windows，使用 Windows 11 24H2 x64、Codex 桌面包 26.1111.2228.0、简体中文界面和 150% 缩放。根据用户要求，桌面操作由代理接手，在原生应用的 Codex 模式中完成。实际验收使用一个无项目聊天，仅发送一条合成样本提示。结束后返回了原来的模式和聊天，已清理未发送的测试草稿。

## 验收结果

| 项目 | 结果 | 真实桌面证据与限制 |
| --- | --- | --- |
| 1. 真实选区与 UIA 原文逐字一致 | PASS | 整行、两行代码及两个短选区均通过 UTF-16 序数比较。逐字对照见下文。 |
| 2. 中文、英文、标点、空格、换行 | PASS | 保留全角逗号和句号、半角英文标点、连续两个空格、第二行行首两个空格及一个 LF。比较期间未修剪或归一化。 |
| 3. 多行选区与代码块 | PASS | 真实文本代码块两行共 40 个 UTF-16 代码单元，鼠标选区与 UIA 读取完全一致。代码换行时返回额外矩形，几何结果见第 5 项。 |
| 4. 助手回答与其他区域的区分 | PASS | 同一个整行样本在助手回答中返回 Assistant，在用户消息中返回 User。另测用户标记、输入框选区及跨用户和助手的选区，分别返回 User、Editable、Unknown。结论覆盖本机中文布局与样本，英文 UI、侧边栏折叠、长对话和其他客户端版本为 NOT TESTED。 |
| 5. GetBoundingRectangles 定位 | PARTIAL | 普通文本与代码两行的主要矩形覆盖真实高亮。滚动时坐标更新。多行代码另外返回一个可重复的 1×2 物理像素矩形，位于高亮下方。 |
| 6. 选区变化事件可靠性 | PARTIAL | 快速改选的 6 次状态变化共收到 12 个事件。点击空白取消时状态变为 no-selection，同一采样的事件计数没有增加。事件与变化不一一对应，保留限频回读。 |
| 7. 多显示器和 DPI | PARTIAL | 本机单屏 144 DPI（150%）实测通过，使用物理屏幕像素。报告的 monitor_count 为 1，多屏、跨屏和混合缩放为 NOT TESTED。负坐标自测通过。 |
| 8. 快速连续选择 | PASS | 6 次中英文交替拖选，桌面观察间隔约 500–521 ms。探针依次捕获 6/6，均逐字匹配。短于采样间隔的极短选区未覆盖。 |
| 9. UIA 超时隔离 | PARTIAL | 在真实 Codex 前台模拟工作进程停顿，2038 ms 后看门狗清除选区并结束工作进程。停顿期间 Codex 仍可滚动，新工作进程启动后再次采集到同一选区。真实 UIA 调用卡死未复现。 |
| 10. 失焦后失效 | PASS | Codex 被切走后，探针输出 foreground=inactive 并清除状态，即便后台窗口仍公开旧选区。返回后创建新工作进程，重新读取新状态。 |
| 前台窗口与模式识别 | PASS | 校验前台 HWND、进程身份和当前 Codex 模式。同一 HWND 切换到 ChatGPT 模式时，探针报告 codex-mode-unconfirmed，停止读取其选区。 |

## 逐字对照

样本来自实际拖选的高亮。桌面控制工具独立确认拖选范围，探针调用 Document 上的 TextPattern.GetSelection 和 TextPatternRange.GetText，使用 StringComparison.Ordinal 比较。下表采用 JSON 字符串写法显示换行和双引号。长度为 UTF-16 代码单元数。 [UIA GetSelection](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.textpattern.getselection?view=windowsdesktop-10.0) · [UIA GetText](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.text.textpatternrange.gettext?view=windowsdesktop-10.0)

| 位置 | 实际选中文字（预期） | UIA 捕获原文 | 长度 | 结果 |
| --- | --- | --- | ---: | --- |
| 助手普通文本 | "P1 中文，标点。 English, test!" | "P1 中文，标点。 English, test!" | 24 | PASS |
| 助手代码块 | "P1 = \"中  文, English!\"\n  P2 = \"空格; test?\"" | "P1 = \"中  文, English!\"\n  P2 = \"空格; test?\"" | 40 | PASS |
| 助手中文子选区 | "中文，标点。" | "中文，标点。" | 6 | PASS |
| 助手英文子选区 | "English, test!" | "English, test!" | 14 | PASS |
| 用户消息中的相同文本 | "P1 中文，标点。 English, test!" | "P1 中文，标点。 English, test!" | 24 | PASS，角色 User |
| 用户消息负例 | "P1-USER-NEGATIVE-582" | "P1-USER-NEGATIVE-582" | 20 | PASS，角色 User |
| 未发送草稿 | "P1-EDIT-NEGATIVE-582" | Edit 区域识别后跳过正文和矩形读取 | — | PASS，角色 Editable |

代码块实际选区如下。第一行“中”和“文”之间、第二行开头各有两个 ASCII 空格，两行之间为 U+000A，结尾在双引号处。

~~~text
P1 = "中  文, English!"
  P2 = "空格; test?"
~~~

跨用户消息与助手回答的拖选覆盖两条消息，探针返回 Unknown。角色读取以 UIA 对话区域内的不可见角色标题为边界，使用 RangeFromChild 和范围端点比较。角色标签或端点关系不明确时返回 Unknown。角色识别的依据是当前客户端公开的可访问性结构，其标签、结构和范围在客户端更新后仍需复测。 [RangeFromChild](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.textpattern.rangefromchild?view=windowsdesktop-10.0) · [CompareEndpoints](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.text.textpatternrange.compareendpoints?view=windowsdesktop-10.0)

## 屏幕矩形与滚动

矩形为物理屏幕坐标，格式为 (x, y, 宽, 高)。对照时窗口的 Win32 矩形为 (593, 216, 1736, 1038)，窗口截图为 1157×692 像素。将 UIA 坐标转到截图后，对照了可见高亮的位置。普通文本另有指针落在矩形内的记录。 [GetBoundingRectangles](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.text.textpatternrange.getboundingrectangles?view=windowsdesktop-10.0) · [UIA 屏幕坐标和 DPI](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-screenscaling)

| 实际鼠标高亮 | UIA 物理矩形 | 对照结果 |
| --- | --- | --- |
| 助手整行，滚动前 | (1162, 753, 277, 29) | 覆盖高亮，指针命中矩形。 |
| 代码第一行，滚动前 | (1193, 886, 243, 24) | 对齐第一行高亮。 |
| 代码第二行，滚动前 | (1193, 916, 211, 24) | 对齐第二行高亮。 |
| 代码块附加矩形，滚动前 | (1435, 961, 1, 2) | 位于两行高亮下方，保留为原始 UIA 几何证据。 |
| 滚动后的两行主要矩形 | (1193, 1017, 243, 24)、(1193, 1047, 211, 24) | 两行纵坐标分别增加 131，文本仍逐字匹配。最终版启动后复核了这组坐标。 |
| 最终版快速改选，中文与英文 | (1191, 792, 126, 29)、(1323, 792, 116, 29) | 分别覆盖可见的中文和英文子选区。 |

## 连续选择、看门狗与失焦

- 最终版探针在 84.896、85.391、85.934、86.472、87.015、87.523 秒依次捕获中文、英文、中文、英文、中文、英文。选区变化计数在该轮从 6 增至 18。107.195 秒变为 no-selection，计数仍为 18。
- 59.902 秒开始模拟工作进程停顿。停顿期间真实 Codex 页面响应滚轮，回答位置移动。61.940 秒清除状态，62.126 秒启动替换进程，62.359 秒重新采集选区。工作进程中的停顿是故障注入，主进程的超时和恢复路径为实际执行。 [UIA MTA 与事件线程](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading)
- 122.260 秒采集到代码选区，122.759 秒随应用切换清除状态。128.655 秒回到 Codex 前台，128.966 秒重新读取选区。141.629 秒，同一个 HWND 切回 ChatGPT 模式，报告 codex-mode-unconfirmed。

## 实现和复测

探针为独立 WinExe，提供限时后台诊断。前台识别采用 Win32 HWND 和已验证的 Codex 包家族，包身份查询返回拒绝访问时只接受系统 WindowsApps 下与该包名称和发布者匹配的宿主映像。UIA 读取限制在当前窗口。MTA 工作进程负责 Document、TextPattern 和事件订阅，父进程在前台变化、故障或超过两秒时间预算时放弃工作进程与旧结果。窗口与选区信息仅在诊断中使用。普通输出只有元数据，只有开启 --fixtures 且匹配完整的合成样本时才输出逐字证据。

Release 构建为 0 警告、0 错误。最终版脱机自测通过，包括序数比对、LF 和 CRLF 差异、缩进、负屏幕坐标、包身份路径、模拟停顿和替换进程。真实桌面结果来自另一次独立执行。开发期间发现从受限工作区直接启动与从当前用户临时目录运行同一构建时，包身份及 UIA 提供者的可见性有差异。真实验收从当前用户临时目录运行，整个构建和测试保持 .NET 10。

复测使用交互 Windows 用户的 PowerShell，打开真实桌面 Codex 后手动选择上面的合成样本。命令将构建结果放到用户临时目录，并收集有限时间的探针输出。

~~~powershell
Set-Location -LiteralPath '<repo-root>'
dotnet build src/SelectionProbe/SelectionProbe.csproj -c Release
$selectionProbeDir = Join-Path ([IO.Path]::GetTempPath()) ('context-notes-phase1-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $selectionProbeDir | Out-Null
Get-ChildItem -File -LiteralPath '.\src\SelectionProbe\bin\Release\net10.0-windows' |
    Copy-Item -Destination $selectionProbeDir
$selectionProbeExe = Join-Path $selectionProbeDir 'SelectionProbe.exe'
$selectionProbeOutput = Join-Path $selectionProbeDir 'acceptance.log'
$selectionProbeError = Join-Path $selectionProbeDir 'errors.log'
$selectionProbe = Start-Process -FilePath $selectionProbeExe -ArgumentList '--fixtures','--exercise-timeout','--seconds','185' -WindowStyle Hidden -RedirectStandardOutput $selectionProbeOutput -RedirectStandardError $selectionProbeError -PassThru
$selectionProbe.WaitForExit()
Get-Content -LiteralPath $selectionProbeOutput
~~~

可将参数换成 --self-test 执行脱机测试。验收采样到期会自行退出。合成测试聊天保留在桌面历史中供复核。

## 交付与阶段门槛

新增 src/SelectionProbe 下的 .NET 10 选区探针源码、用于忽略构建输出的 .gitignore，以及本验收报告。基线规范与 Phase 0 报告保持原样。Phase 1 使用独立本地提交，用 git log -1 --oneline 查看。

已测实机路径满足本阶段的精确文本读取。额外矩形、选区事件覆盖、多屏、混合 DPI 和 UIA 自然卡死仍是已知风险。等待用户验收后，Phase 2 计划只做选区按钮的定位、更新和失效验证，先复核多行异常矩形和多显示器坐标。本阶段止于选区探针。
