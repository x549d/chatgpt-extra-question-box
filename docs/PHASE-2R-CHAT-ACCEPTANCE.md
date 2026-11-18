# Phase 2R · Codex Desktop Chat 验收

验收日期为 2026-11-18，时间均为 UTC+08:00。本轮从 `ad39503`（`main`）开始，最终测试版本为包含输入框边界修复的 `56b0ecc`。角色修复前的历史基线为 `329e7d5`。

**Phase 2R 为 PARTIAL。** 最终版已在真实 Chat 确认助手选区、按钮显示与定位、物理鼠标点击及前台保留、选区离开视口后隐藏，以及用户消息和输入框排除。冷启动的应用重开已通过，但只打开已有 Chat 时，未观察到 `SessionStart` 启动后台。

## 环境

| 项目 | 值 |
| --- | --- |
| Codex Desktop | `26.1111.2228.0`，简体中文，Chat 模式 |
| 本地插件 | `context-notes@context-notes-native` `0.2.0` |
| Windows | Windows 11 24H2，`26100.7623` |
| 显示 | 1 台显示器，144 DPI（150%） |
| .NET | .NET 10，WPF |
| 测试对象 | 真实 Codex Desktop 的已有 Chat，验收未发送新消息 |

## 修复版部署复核

| 项目 | 状态 | 证据 |
| --- | --- | --- |
| 源码与部署 | PASS | `56b0ecc` 的输入框边界修复版于 15:03:47 首次部署。15:39:41 为冷启动复验同步了本次 Release 构建，EXE、DLL、运行配置及依赖清单均与构建产物通过 SHA-256 比对。当前 DLL SHA-256 前缀为 `96A117870C1D`。 |
| 插件与 Hook 配置 | PASS | 本机插件配置 `enabled=true`。安装包的插件清单、Hook 定义和启动脚本与仓库逐字节相同。唯一的 `SessionStart` 处理器包含正确的 Windows 启动命令。找到一条对应的已保存信任记录，未配置单项禁用。 |
| 后台状态与单实例 | PASS | 最终部署验证启动 PID `28236`，`--status` 返回 `companion_running`。第二个常驻实例以状态码 0 退出，原进程继续运行。 |
| 日志目录 | PASS | `companion.log` 记录新版启动、实际 Chat 和选区状态。独立只读诊断成功创建当轮日志。 |

## 验收矩阵

| 项目 | 状态 | 证据 |
| --- | --- | --- |
| Chat 模式 | PASS | 真实前台窗口的 UIA 提供程序与插件均报告 `mode_detection=chat` 或 `mode=chat`。定位规则同时要求会话正文、滚动区域和桌面主内容层级。 |
| 修复版冷启动自动拉起 | FAIL | 当前后台已正常停止，旧 Codex 主进程 `34604` 所属的 14 个程序进程全部退出。通过 Windows 注册入口重开，主进程变为 `28004`。已有 Chat 的只读诊断确认 `mode=chat`。90 秒观察窗及随后导航验证期间无新的 `SessionStart` 启动日志，后台仍为 `companion_not_running`。 |
| Hooks 设置页 | NOT TESTED | 本机配置启用了插件，Hook 没有按来源过滤或被单项禁用，存在定义及脚本的信任记录。未在桌面设置页确认运行时信任状态。 |
| 助手回答识别 | PASS | 最终版在真实 Chat 返回 `selection_role=assistant`、`selection_valid=True`、`reason=eligible`。 |
| 用户消息排除 | PASS | 最终版的真实选区返回 `selection_role=user`、`selection_valid=False`、`reason=role_user`，浮层报 `overlay_hidden_reason=role_user`。 |
| 输入框排除 | PASS | 最终版在 15:38:19 和 15:38:27 检测到真实 Chat 输入框选区，报告 `selection_role=composer`、`selection_valid=False` 和 `overlay_hidden_reason=role_composer`。诊断仅记录状态与角色。 |
| 侧栏排除 | NOT TESTED | 保留导航区和侧栏的结构判断，未独立物理拖选复验。 |
| 提问按钮显示 | PASS | 最终版的真实助手选区后记录 `overlay_visible=true change=shown`，并给出实际 WPF 窗口坐标。 |
| 按钮位置与遮挡 | PASS | 最终版的高亮右边缘为 `1419`，按钮左边缘为 `1430`，间距 11 个物理像素。按钮顶部与高亮对齐，实际矩形完全处于正文裁剪区内，并避开输入框。窗口移动和多区域场景另列验收。 |
| 点击事件捕获 | PASS | 15:09:57.333 记录 `annotation_entry_clicked codex_foreground=true`，与用户确认的点击对应。 |
| 真实物理鼠标 | PASS | 用户确认于 2026-11-18 15:09:57 亲自用物理鼠标点击了「提问」按钮。 |
| Codex 保持前台 | PASS | 用户确认点击后 Codex 仍在前台，且同一事件记录 `codex_foreground=true`。 |
| 无意外发送主 Chat 消息 | PASS | 用户确认此次物理点击未向 Codex 主 Chat 发送消息。 |
| 后台持续运行 | PASS | 最终版后台 PID `28236` 持续运行约 35 分钟，直到 15:39:40 主动执行 `--stop`。冷启动后手动跑本机启动脚本得到 PID `8272`，约 69 秒后仍为 `companion_running`。冷启动后的自动驻留另见冷启动记录。 |
| 取消选择和失焦 | PARTIAL | `ad39503` 的真实可见按钮在选区清空时消失。最终版真实日志观察到失焦与隐藏请求，从可见按钮状态触发的最终版失焦待复测。 |
| 恢复前台 | PARTIAL | 最终版识别到重新进前台的桌面窗口和 Chat 模式。有效选区和可见入口恢复待复测。 |
| 选区离屏 | PASS | 点击后同一真实选区仍被识别为 `assistant`，其可见矩形变为 0，窗口尺寸和选区事件计数未变。入口记录 `overlay_visible=false overlay_hidden_reason=selection_outside_viewport`。 |
| 滚动时跟随 | PARTIAL | 实机已观察选区离开视口后隐藏，选区仍在视口时的连续滚动跟随待复测。 |
| 移动窗口与最大化／还原 | NOT TESTED | 真实窗口几何变化后的按钮重定位待复测。 |
| 快速改选 | NOT TESTED | 真实 Chat 中 A → B → C 待复测。 |
| 多行、缩进与代码块 | PARTIAL | 保留 Phase 1 的 UIA 选区直接读取。中文、英文、标点、空格和换行逐字自测通过，修复版实际多行代码拖选待复测。 |
| 查询超时和恢复 | PARTIAL | .NET 自测测得隔离工作进程约 2 秒超时，随后成功重启。未同时在真实 Chat 操作滚动。 |
| 150% 单屏 DPI | PASS | 最终版在 144 DPI 记录的 UIA 选区、WPF 浮层及正文裁剪区均为物理像素，水平间距 11 像素，与 7 DIP 转为物理像素并向上取整的结果一致。 |
| 多显示器与混合 DPI | NOT TESTED | 当前环境只有一台显示器，跨屏自测只覆盖几何算法。 |

## 根因与修复

真实 Chat 的 Chrome `RootWebArea` 将多条消息的无障碍角色标题暴露在同一个容器层级。旧规则在匹配到一组混合标题后返回 `Unknown`。新规则先验证选区处于 Chat 正文、滚动容器和主内容区的正确层级，再按 UIA 文本端点选择同一消息的最近前置隐藏角色标题。区间跨标题、角色冲突或正文结构不明时隐藏按钮。正文的角色标题在工作进程中短时缓存。

诊断只输出已知界面标签、长度、结构和几何。Hook 日志将事件来源限制在已知值，启动状态由前台窗口信息提供。真实选区正文和用户草稿均未写入验收材料。

本轮诊断发现长对话中已滚出视口的 `ProseMirror` 编辑器排在当前输入框前面。原代码缓存了第一个匹配项，导致正文底边未裁到当前输入框上方。最终版只接受与视口相交的可见编辑器，优先选择靠近底部的 `ProseMirror`，并在缓存编辑器离屏时重新定位。真实桌面的只读比对从视口 `[1041,296,1229,899]` 找到当前输入框 `[1121,1045,1069,60]`，将浮层位置可用范围裁至 `[1041,296,1229,725]`，在输入框前留出 24 个物理像素。

## 脱敏日志证据

本轮重新部署的状态记录如下。

```text
14:53:57.226  companion stopped reason=stop_requested
14:53:57.608  companion started process pid=22316
14:53:57.913  mode_detection=chat selection_detected=False reason=selection_empty dpi=144 monitor_count=1
14:54:34.721  inspect_ready seconds=300
14:55:23.243  overlay_visible=false overlay_hidden_reason=not_foreground
14:58:55.588  mode_detection=chat selection_detected=True selection_role=assistant reason=eligible
14:58:55.589  selection_rectangle=[1508,865,127,29]
14:58:56.135  overlay_visible=true change=shown side=right px=[1646,865,99,45] viewport=[1041,296,1229,899] dpi=144
14:58:57.059  annotation_entry_clicked codex_foreground=true
14:58:59.761  overlay_visible=false overlay_hidden_reason=selection_empty
14:59:42.096  mode_detection=chat selection_detected=True selection_role=assistant reason=eligible
14:59:42.098  overlay_visible=true change=shown side=right px=[1646,865,99,45]
14:59:44.288  annotation_entry_clicked codex_foreground=true
15:03:47.551  companion started process pid=28236
15:03:47.859  mode_detection=chat selection_detected=False reason=selection_empty dpi=144 monitor_count=1
15:09:56.130  mode_detection=chat selection_detected=True selection_role=assistant selection_valid=True reason=eligible
15:09:56.132  selection_rectangle=[1271,902,148,29]
15:09:56.645  overlay_visible=true change=shown side=right px=[1430,902,99,45] viewport=[1041,296,1229,791] dpi=144
15:09:57.333  annotation_entry_clicked codex_foreground=true
15:09:59.597  overlay_visible=false overlay_hidden_reason=selection_outside_viewport
15:10:07.793  mode_detection=chat selection_detected=True selection_role=user selection_valid=False reason=role_user
15:10:07.793  overlay_visible=false overlay_hidden_reason=role_user
15:38:19.921  mode_detection=chat selection_detected=True selection_role=composer selection_valid=False reason=role_composer
15:38:19.921  overlay_visible=false overlay_hidden_reason=role_composer
15:38:27.033  mode_detection=chat selection_detected=True selection_role=composer selection_valid=False reason=role_composer
15:38:27.033  overlay_visible=false overlay_hidden_reason=role_composer
```

### 冷启动分项

| 项目 | 状态 | 证据 |
| --- | --- | --- |
| 停止旧后台 | PASS | `--stop` 后进程退出，`--status` 返回 `companion_not_running`。 |
| 完全退出与重新打开 | PASS | 普通关窗仍保留原进程。使用应用退出后观察到程序包进程数为 0，随后由 Windows 注册入口打开新实例。 |
| 打开已有 Chat | PASS | 使用桌面客户端导航到已有对话，只读检查报告 `mode=chat`。本次冷启动测试未发送消息。 |
| `SessionStart` 启动 Hook | FAIL | 本轮打开已有 Chat 后无新的 `SessionStart` 运行记录。 |
| Hook 自动启动后台 | FAIL | 独立只读检查结束时 `--status` 返回 `companion_not_running`。 |
| 自动启动后驻留及显示按钮 | NOT TESTED | 自动启动前置条件未满足。实际浮层能力已由上文同一源码版本单独验证。 |
| 本地启动脚本与后台恢复 | PASS | 用安装的脚本入口单独诊断。没有提供生命周期数据，日志来源为 `unparsed`，新后台启动后持续运行。 |

```text
15:39:40.701  companion stopped reason=stop_requested
15:42:59.074  old_package_processes_exited remaining=0
15:42:59.125  registered_app_launch_requested
15:43:01.640  new_codex_window pid=28004
15:44:29.527  cold_watcher_timeout stage=hook
15:45:11.538  inspect selection_changed mode=chat
15:45:25.736  inspect_stopped
15:46:58.751  hook_fired source=unparsed companion=started pid=8272
```

插件定义和脚本均与仓库逐字节一致，直接调用相同的 Windows 启动脚本成功。失败发生在冷启动的 Chat 页面与本地启动脚本之间。现有证据无法区分此路径未建立可触发 Hook 的运行会话，或由客户端跳过 Hook。`SessionStart` 的公开契约描述的是运行会话生命周期。[官方 Hooks 说明](https://learn.chatgpt.com/docs/hooks#sessionstart)

此前的实机记录保留如下。

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
| `.NET --self-test` | PASS，50/50。原有 48 项和新增的离屏编辑器筛选、输入框边界定位回归均通过。 |
| `dotnet test` | 发现 0 项，项目未注册独立测试运行器 |
| 凭证扫描 | PASS，扫描已跟踪文件和本轮差异，未命中已检查的凭证模式 |

## 剩余验收

### 冷启动重验

1. 对照 Release 构建核对本机部署，记录旧进程，再通过后台的 `--stop` 正常停止并验证 `companion_not_running`。通过 Codex 的 **文件 → 退出 ChatGPT** 完全退出桌面客户端，确认旧进程退出。
2. 使用 Windows 开始菜单重新打开客户端，进入一个已有 Chat，保持空闲，无需发送新消息。检查 **设置 → Hooks** 中 Context Notes 的 `SessionStart` 条目，如有审核提示先核对脚本来源。[官方 Hooks 说明](https://learn.chatgpt.com/docs/hooks#review-and-trust-hooks)
3. 以新客户端 PID、当轮 `hook_fired`、新后台 PID 和两次间隔检查的 `companion_running` 核对启动和驻留。用鼠标拖选助手回答，确认冷启动后的「提问」出现。

先核对桌面端能否在只打开已有 Chat 时派发项目的 `SessionStart`，再按上述步骤复验。该核心项通过后即可结束 Phase 2R，开始 Phase 3。多屏和混合 DPI 保留为 NOT TESTED。
