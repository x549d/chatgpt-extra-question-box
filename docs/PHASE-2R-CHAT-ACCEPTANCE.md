# Phase 2R · Codex Desktop Chat 验收

验收日期为 2026-11-18，时间均为 UTC+08:00。项目基线为 `329e7d5`。修复后的 Release 已更新到本机已安装的 `context-notes` 插件缓存，通过 SHA-256 核对 DLL 与构建产物一致。

**Phase 2R 为 PARTIAL，Phase 3 为 BLOCKED。** 冷启动自动拉起已观察到。已定位并修复 Chat 中的角色误判，真实 UI Automation 只读样本已复核。修复版仍需完成助手选区、按钮目视定位和物理鼠标点击的端到端验收。

## 环境

| 项目 | 值 |
| --- | --- |
| Codex Desktop | `26.1111.2228.0`，简体中文，Chat 模式 |
| Windows | Windows 11 24H2，`26100.7623` |
| 显示 | 1 台显示器，144 DPI（150%） |
| .NET | .NET 10，WPF |
| 测试对象 | 真实 Codex Desktop 的已有 Chat，验收未发送新消息 |

## 验收矩阵

| 项目 | 状态 | 证据 |
| --- | --- | --- |
| Chat 模式 | PASS | 真实前台窗口的 UIA 提供程序与插件均报告 `mode_detection=chat` 或 `mode=chat`。定位规则同时要求会话正文、滚动区域和桌面主内容层级。 |
| 冷启动自动拉起 | PASS | 先停止已安装后台并确认退出，再完全退出 Codex。应用以新进程重新打开时，已有会话的 `SessionStart:resume` 在未发送新消息的情况下自动启动后台。该次重启恢复了进行中的任务，完全空闲时仅打开已有 Chat 的触发时机仍需单独复核。 |
| Hooks 设置页 | NOT TESTED | 本轮以新产生的 Hook 执行日志确认效果，界面开关状态未单独复核。 |
| 助手回答识别 | PARTIAL | 基线在真实正文选区返回 `role_unknown`。修复版对真实提供程序执行只读取样，4 个可见文本范围判为 `Assistant`。该取样未改变真实选区。 |
| 用户消息排除 | PARTIAL | 修复版的真实提供程序只读取样有 2 个可见文本范围判为 `User`，自测覆盖了交错角色标题。尚待物理拖选复测入口隐藏。 |
| 输入框排除 | PASS | 实际输入框选区报 `role_composer`，随后浮层报 `overlay_hidden_reason=role_composer`。该处理分支在修复中保持原有路径。 |
| 侧栏排除 | NOT TESTED | 保留导航区和侧栏的结构判断，未独立物理拖选复验。 |
| 提问按钮显示 | PARTIAL | WPF 窗口与按钮事件自测通过。基线因角色误判没有显示，修复版缺真实助手拖选后的可见性证据。 |
| 按钮位置与遮挡 | PARTIAL | 选区定位自测覆盖右、左、下、上、可视区裁剪和不重叠。当前真实窗口的按钮位置待目视验收。 |
| 实鼠点击与前台保留 | NOT TESTED | 自测通过 `WM_MOUSEACTIVATE` 处理及按钮事件转发。本轮未取得真实点击记录 `annotation_entry_clicked codex_foreground=true`。 |
| 后台持续运行 | PASS | Hook 启动的同一后台从 14:30:36 运行到 14:41:46 主动停止以更新程序，超过 185 秒，期间只读诊断独立运行。 |
| 取消选择和失焦 | PARTIAL | 真实日志观察到 `selection_empty`、`not_foreground` 及隐藏请求。此前浮层处于隐藏状态，仍需见到可见按钮后复核。 |
| 恢复前台 | PARTIAL | 重新识别桌面前台窗口和 Chat 模式。恢复可见入口待复测。 |
| 滚动与选区离屏 | NOT TESTED | 真实选区离屏后的按钮行为待复测。 |
| 移动窗口与最大化／还原 | NOT TESTED | 真实窗口几何变化后的按钮重定位待复测。 |
| 快速改选 | NOT TESTED | 真实 Chat 中 A → B → C 待复测。 |
| 多行、缩进与代码块 | PARTIAL | 保留 Phase 1 的 UIA 选区直接读取。中文、英文、标点、空格和换行逐字自测通过，修复版实际多行代码拖选待复测。 |
| 查询超时和恢复 | PARTIAL | .NET 自测测得隔离工作进程约 2 秒超时，随后成功重启。未同时在真实 Chat 操作滚动。 |
| 150% 单屏 DPI | PARTIAL | 真实 UIA 选区返回物理屏幕矩形，自测覆盖布局。浮层的目视对齐待复测。 |
| 多显示器与混合 DPI | NOT TESTED | 当前环境只有一台显示器，跨屏自测只覆盖几何算法。 |

## 根因与修复

真实 Chat 的 Chrome `RootWebArea` 将多条消息的无障碍角色标题暴露在同一个容器层级。旧规则在匹配到一组混合标题后返回 `Unknown`。新规则先验证选区处于 Chat 正文、滚动容器和主内容区的正确层级，再按 UIA 文本端点选择同一消息的最近前置隐藏角色标题。区间跨标题、角色冲突或正文结构不明时隐藏按钮。正文的角色标题在工作进程中短时缓存。

诊断只输出已知界面标签、长度、结构和几何。Hook 日志将事件来源限制在已知值，启动状态由前台窗口信息提供。真实选区正文和用户草稿均未写入验收材料。

## 脱敏日志证据

```text
14:26:53.801  companion stopped reason=stop_requested
14:30:36.753  hook_fired source=SessionStart:resume companion=started pid=22596
14:30:40.723  mode_detection=chat selection_detected=False reason=selection_empty
14:35:11.274  mode_detection=chat selection_detected=True selection_role=composer reason=role_composer
14:35:11.275  overlay_visible=false overlay_hidden_reason=role_composer
14:37:30.205  inspect range=0 utf16=6 rects=[1397,645,85,29] [1481,645,43,29]
14:37:30.205  inspect range=0 classify=Unknown path=Group/Group
14:41:47.154  companion started process pid=3448
14:41:47.474  mode_detection=chat selection_detected=False reason=selection_empty
```

修复版的更新后后台是为验收主动重启的。首次冷启动证据来自更新前的已安装版本。另行调用修复版角色分类读取真实 Chat 的可见文本范围，得到 `Assistant=4`、`User=2`、`Unknown=18`。该检查只询问 UIA 文本位置，对原选区、按钮和键盘焦点无操作。

## 自动检查

| 检查 | 结果 |
| --- | --- |
| Release build | PASS，0 个警告、0 个错误 |
| `.NET --self-test` | PASS，48/48，其中含角色区间回归、定位、WPF HWND 和超时自测 |
| `dotnet test` | 命令成功，项目未注册独立测试运行器，发现 0 项 |
| Python | 0/0，不适用。项目无 Python 测试目录，仓库根目录发现 0 项 |

## 剩余验收

在真实桌面 Chat 中用鼠标拖选助手回答，确认按钮靠近高亮区域并单击一次。核对 Codex 保持前台、原输入框未收到输入、日志收到点击事件。再拖选用户消息确认按钮隐藏。通过 Phase 2R 必需项后进入 Phase 3。
