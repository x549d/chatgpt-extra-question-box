# Phase 2 · 浮动按钮验收

验收日期为 2026-11-18（北京时间）。基线为 f606855。实测使用 Windows 11 24H2 x64、Codex 桌面包 26.1111.2228.0、简体中文 Codex 模式和单屏 144 DPI（150% 缩放）。复用现有合成样本以及已完成的 Phase 1 回答进行选择。

结束验收时，恢复窗口位置的系统菜单快捷键落入样本聊天，误提交了一个字母 m，并收到 Codex 回复。该输入来自桌面验收工具。Phase 2 浮动按钮自身只处理点击和记录状态。验收后返回了原来的模式和开发聊天。

## 验收结果

| 检查项 | 状态 | 证据 |
| --- | --- | --- |
| 选区检测 | PASS | 复用 Phase 1 的 Document、TextPattern、角色及前台信息。单字符、单行、多行和 384 字符选区均触发。 |
| 按钮显示 | PASS | 实机只在选区旁看到独立的「向 ChatGPT 提问」按钮。无选区时未见 Companion 窗口、任务栏项或托盘 UI。 |
| 按钮定位 | PASS | 单屏 150% 下，普通文本、短选区和多行代码高亮未被遮挡。长选区旁按钮仍完整位于显示器工作区。左侧、下方、上方的回退通过几何自测，实机观测的按钮在右侧。 |
| 按钮隐藏 | PASS | 点击空白、选区滚出视口、选择非助手文本及失焦均隐藏。一次点击空白取消没有新增 UIA 事件，轮询仍清除按钮。 |
| 快速改选 | PASS | 约 0.56–0.62 秒间隔交替改选 6 次，6 次均显示在对应位置。 |
| 失焦与恢复 | PASS | 失焦时隐藏。返回 Codex 后重新采样，原选区若仍有效则重新显示。 |
| 滚动 | PASS | 可见选区随页面滚动移动。384 字符选区完全离开视口后，状态为 selection-outside-view 并隐藏按钮。 |
| 窗口移动 | PASS | 使用 Windows 窗口移动命令，非最大化窗口由 (593,216) 移至 (529,152)，按钮重新锚定。最大化后与还原后均重新定位。 |
| 角色识别 | PASS | 助手样本显示按钮。用户消息与未发送草稿负例均未显示。仅接受 Assistant，其他角色或无法确认的区域均按无效处理。 |
| 性能与超时 | PASS | 人为停顿期间真实 Codex 能滚动。工作进程超时后结束，按钮隐藏，随后新进程恢复采样。自然发生的 UIA 卡死为 NOT TESTED。 |
| DPI | PASS | 单屏 144 DPI 下实机尺寸为 171×42 物理像素，与 114×28 DIP 相符。 |
| 多显示器 | NOT TESTED | Win32 枚举结果为 1 台显示器。负原点、跨屏选区和不同缩放的纯几何用例通过。真实跨屏和混合 DPI 待测。 |
| 点击与键盘焦点 | PARTIAL | 实机鼠标命中独立工具窗口，按钮显示期间 Codex 保持前台。自测验证了 WM_MOUSEACTIVATE 返回 MA_NOACTIVATE、WPF 点击路由与焦点属性。桌面控制工具会识别出命中的工具窗，但不将该无任务栏窗口列为可点击目标，未取得一次完整的实鼠点击日志。 |

## 18 项桌面场景

| 编号 | 场景 | 状态 | 观测 |
| --- | --- | --- | --- |
| 1 | 单行中文 | PASS | 中文子选区显示按钮。 |
| 2 | 单行英文 | PASS | 直接改选英文，按钮移动到英文旁。 |
| 3 | 中文、英文和标点 | PASS | 整行混排显示，完整高亮保留。 |
| 4 | 两行普通文本 | PASS | 跨自动换行的两行说明显示按钮。 |
| 5 | 多行代码 | PASS | 两行代码显示按钮，定位未采用 UIA 返回的 1×2 碎片。 |
| 6 | 连续空格和缩进 | PASS | 复用 Phase 1 两行缩进代码，完整拖选后显示按钮。 |
| 7 | 快速改变选区 | PASS | 中英文交替 6 次，6 次均重新定位。 |
| 8 | 取消选择 | PASS | 点击空白清除高亮后隐藏，包括事件计数未增加的情形。 |
| 9 | Codex 失去焦点 | PASS | 切到其他桌面窗口时清除按钮。 |
| 10 | Codex 重新获得焦点 | PASS | 对仍有效的选区重新采样并显示。 |
| 11 | 页面滚动 | PASS | 跟随可见高亮。高亮完全滚出后隐藏。 |
| 12 | 移动 Codex 窗口 | PASS | 在还原窗口中移动，旧位置清除，按钮在新位置显示。 |
| 13 | 最大化和还原 | PASS | 两次窗口尺寸变更后均重新采样定位。 |
| 14 | 用户消息区域 | PASS | 用户消息高亮时按钮保持隐藏。 |
| 15 | 输入框选区 | PASS | 未发送草稿高亮为 Editable，没有按钮。 |
| 16 | 助手回答区域 | PASS | 合成回答、普通说明和已完成的长回答均触发。 |
| 17 | 长选区 | PASS | 384 个 UTF-16 代码单元、19 个 UIA 矩形的选区有按钮。 |
| 18 | 短选区 | PASS | 一个中文字符的 21×29 像素高亮有按钮。 |

## 位置、失效与超时证据

原始坐标采用 (x, y, 宽, 高)，以物理屏幕像素表示。真实截图用于对照高亮与按钮。截图包含应用侧栏，仓库保存脱敏坐标和结果。按钮使用 114×28 DIP，通过目标显示器上的 WPF DPI 换算为物理像素，按 Windows 工作区约束。 [UIA 屏幕坐标](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-screenscaling) · [MONITORINFO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo)

| 场景 | UIA 选区矩形或窗口变化 | 按钮记录 |
| --- | --- | --- |
| 中文与英文交替改选 | (1191,753,126,29) 与 (1323,753,116,29) | 按钮 x 坐标在 1328 和 1450 间交替 6 次，y 为 793，留出同一行的相邻文字。 |
| 两行代码 | (1193,886,243,24)、(1193,916,211,24)，另有 (1435,961,1,2) | 按钮为 (1447,886,171,42)，小矩形未参与锚点。 |
| 滚动两行代码 | 第一行从 y=886 移至 y=1051 | 按钮跟随至 y=1051。长选区完全滚出视口后，日志记录 selection-outside-view。 |
| 非最大化移动 | 窗口从 (593,216,1736,1038) 变为 (529,152,1736,1038) | 按钮从 (1450,958,171,42) 重定位到 (1387,894,171,42)。 |
| 最大化与还原 | 最大化为 (0,0,2560,1528)，还原为 (593,216,1736,1038) | 按钮在新布局稳定后分别定位到 (1038,742,171,42) 和 (1450,958,171,42)。 |
| 停顿演练 | 18.137 秒开始停顿，Codex 页面仍响应滚轮 | 看门狗实测 2053 ms。旧按钮因 uia-timeout 隐藏，新工作进程采样后以更新后的矩形重新显示。 |

## 实现与复测

基于 Phase 1 的单请求工作进程与两秒看门狗，选区重新采样间隔为 180 ms，前台、窗口移动及按键状态检查约 55 ms 一次。旧查询的窗口或操作代次不匹配时丢弃结果。WPF 在独立 STA Dispatcher 上处理最新状态，不执行 UIA 查询。按钮使用 ShowActivated=False、WS_EX_NOACTIVATE 与 WM_MOUSEACTIVATE，并以 SWP_NOACTIVATE 定位。 [WPF ShowActivated](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.showactivated?view=windowsdesktop-10.0) · [Win32 扩展样式](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) · [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)

定位先排除宽度或高度不超过 2 物理像素的矩形，再用 DPI 和与文本行的邻近关系辨别近零碎片。正常行高的窄字符可以独立作为锚点。多行在同一显示器可见范围内合并。工作区未找到可容纳且不遮盖选区的位置时隐藏。实机所有按钮均为右侧定位。

Release 构建无警告和错误，32 项自测全部通过。自测覆盖 Phase 1 序数比较与进程恢复、四侧定位、异常矩形、单字符、多屏几何、隐藏窗口初始化与非激活鼠标消息。诊断只记录状态、角色、长度、矩形和耗时。旧样本模式也仅记录样本编号及比较结果。选区文本通过工作进程与父进程间的匿名管道传输。

在交互 Windows 用户 PowerShell 中可用以下方式复测。沿用 Phase 1 的当前用户临时目录运行方式，以便 UIA 访问桌面提供者。

~~~powershell
Set-Location -LiteralPath '<repo-root>'
dotnet build src/SelectionProbe/SelectionProbe.csproj -c Release
$overlayTestDir = Join-Path ([IO.Path]::GetTempPath()) ('ctx-notes-phase2-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $overlayTestDir | Out-Null
Get-ChildItem -File -LiteralPath '.\src\SelectionProbe\bin\Release\net10.0-windows' |
    Copy-Item -Destination $overlayTestDir
Start-Process -FilePath (Join-Path $overlayTestDir 'SelectionProbe.exe') -ArgumentList '--overlay','--seconds','185' -WindowStyle Hidden -RedirectStandardOutput (Join-Path $overlayTestDir 'overlay.log')
~~~

可在测试中添加 --exercise-timeout，在首个有效助手选区触发一次受控停顿。--self-test 执行脱机测试。无参数运行仅启动后台监听，验收使用限时参数，进程到期自行清理。

## 交付与门槛

提交内容为独立 WPF 按钮、锚点和定位逻辑、Phase 1 采样接线、脱敏日志、回归自测和本报告。实机验证窗口移动、恢复和视口失效。多屏混合 DPI、自然 UIA 卡死与完整实鼠点击证据仍待补充。Phase 2 提交后暂停，等待用户验收，再安排 Phase 3 的按钮点击与旁注交互。
