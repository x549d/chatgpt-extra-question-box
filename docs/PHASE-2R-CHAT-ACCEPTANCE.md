# Phase 2R · Codex Desktop Chat 验收

验收日期为 2026-11-18，时间均为 UTC+08:00。本轮从 `ad39503`（`main`）开始，最终测试版本为包含输入框边界修复的本提交。角色修复前的历史基线为 `329e7d5`。

**Phase 2R 为 PARTIAL。** 实机确认 `ad39503` 的真实助手选区触发「提问」，日志记录了点击时 Codex 保持前台。随后修复了输入框边界，真实窗口的只读几何复验和完整自测已通过。最终版仍需确认冷启动、物理点击和其余实机用例。

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
| 源码与部署 | PASS | 14:53:57 将 `ad39503` 的重建产物部署到本机。最终边界修复版于 15:03:47 部署，插件目录的 EXE、DLL、运行配置及依赖清单均与构建产物通过 SHA-256 比对。最终 DLL SHA-256 前缀为 `8B1AD6F90B53`。 |
| 插件与 Hook 配置 | PASS | 本机插件配置 `enabled=true`。安装包的插件清单、Hook 定义和启动脚本与仓库逐字节相同。唯一的 `SessionStart` 处理器包含正确的 Windows 启动命令。找到一条对应的已保存信任记录，未配置单项禁用。 |
| 后台状态与单实例 | PASS | 最终部署验证启动 PID `28236`，`--status` 返回 `companion_running`。第二个常驻实例以状态码 0 退出，原进程继续运行。 |
| 日志目录 | PASS | `companion.log` 记录新版启动、实际 Chat 和选区状态。独立只读诊断成功创建当轮日志。 |

## 验收矩阵

| 项目 | 状态 | 证据 |
| --- | --- | --- |
| Chat 模式 | PASS | 真实前台窗口的 UIA 提供程序与插件均报告 `mode_detection=chat` 或 `mode=chat`。定位规则同时要求会话正文、滚动区域和桌面主内容层级。 |
| 修复版冷启动自动拉起 | PARTIAL | 历史构建曾在完全退出后由已有会话的 `SessionStart:resume` 启动后台。修复版确认 Hook 配置一致，部署后的后台由本轮手动启动用于验证。打开已有 Chat 后的新版冷启动执行尚待复测。 |
| Hooks 设置页 | NOT TESTED | 插件本机配置已启用且存在信任记录。新版运行时是否仍认可该记录，待桌面设置页或新版冷启动日志核对。 |
| 助手回答识别 | PARTIAL | `ad39503` 对真实助手选区两次返回 `selection_role=assistant reason=eligible`。最终版的角色规则相同，待复验部署后的真实选区。 |
| 用户消息排除 | PARTIAL | 修复版的真实提供程序只读取样有 2 个可见文本范围判为 `User`，自测覆盖了交错角色标题。尚待物理拖选复测入口隐藏。 |
| 输入框排除 | PARTIAL | 历史构建的实际输入框选区报 `role_composer`，随后浮层报 `overlay_hidden_reason=role_composer`。修复版当前只观察到插入光标和 `selection_empty`，拖选待复测。 |
| 侧栏排除 | NOT TESTED | 保留导航区和侧栏的结构判断，未独立物理拖选复验。 |
| 提问按钮显示 | PARTIAL | `ad39503` 的真实窗口两次记录 `overlay_visible=true`。最终版的真实拖选待复验。 |
| 按钮位置与遮挡 | PARTIAL | `ad39503` 的实机样本在高亮右侧 11 像素，位于正文视口，无高亮或输入框重叠。另发现离屏编辑器干扰输入框边界计算，最终版已修复并在同一客户端通过只读边界比对。最终版借助可见按钮作目视复验待完成。 |
| 点击处理及前台保留 | PARTIAL | `ad39503` 的实际浮层多次记录 `annotation_entry_clicked codex_foreground=true`。程序未调用点击自动化，最终版点击待复验。 |
| 真实物理鼠标 | NOT TESTED | 点击日志记录事件与前台状态，事件本身不提供输入来源。等待用户确认物理鼠标点击。 |
| 后台持续运行 | PASS | Hook 启动的同一后台从 14:30:36 运行到 14:41:46 主动停止以更新程序，超过 185 秒，期间只读诊断独立运行。 |
| 取消选择和失焦 | PARTIAL | `ad39503` 的真实可见按钮在选区清空时消失。最终版真实日志观察到失焦与隐藏请求，从可见按钮状态触发的最终版失焦待复测。 |
| 恢复前台 | PARTIAL | 最终版识别到重新进前台的桌面窗口和 Chat 模式。有效选区和可见入口恢复待复测。 |
| 滚动与选区离屏 | NOT TESTED | 真实选区离屏后的按钮行为待复测。 |
| 移动窗口与最大化／还原 | NOT TESTED | 真实窗口几何变化后的按钮重定位待复测。 |
| 快速改选 | NOT TESTED | 真实 Chat 中 A → B → C 待复测。 |
| 多行、缩进与代码块 | PARTIAL | 保留 Phase 1 的 UIA 选区直接读取。中文、英文、标点、空格和换行逐字自测通过，修复版实际多行代码拖选待复测。 |
| 查询超时和恢复 | PARTIAL | .NET 自测测得隔离工作进程约 2 秒超时，随后成功重启。未同时在真实 Chat 操作滚动。 |
| 150% 单屏 DPI | PARTIAL | `ad39503` 的实际选区和浮层矩形距文本右边界 11 像素，最终版已在同一 DPI 下核对正文边界。最终版浮层的目视对齐待复测。 |
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
```

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

### 真实选区与点击

1. 在已有 Chat 中用鼠标拖选一小段助手回答。确认「提问」出现且贴近高亮，处于正文视口内，并避开高亮文字和输入框。
2. 用物理鼠标单击一次，观察 Codex 是否保持前台。核对当轮 `mode_detection=chat`、`selection_role=assistant`、`reason=eligible`、`overlay_visible=true`、`annotation_entry_clicked codex_foreground=true`。测试会话无需发送新消息。
3. 选择助手多行代码，再分别选中用户消息、输入框内未发送的草稿和侧栏文字。验证助手入口出现及其他区域排除。最后验证快速改选、滚动、窗口移动、失焦和恢复前台。

### 冷启动

1. 通过 Codex 的 **文件 → 退出 ChatGPT** 完全退出桌面客户端，再确认旧后台已退出。当前驻留进程会在检测到 Codex 关闭后的约 60 至 65 秒内退出。
2. 使用 Windows 开始菜单重新打开客户端，进入一个已有 Chat，保持空闲，无需发送新消息。检查 **设置 → Hooks** 中 Context Notes 的 `SessionStart` 条目，如有审核提示先核对脚本来源。[官方 Hooks 说明](https://learn.chatgpt.com/docs/hooks#review-and-trust-hooks)
3. 以新客户端 PID、当轮 `hook_fired`、新后台 PID 和 `companion_running` 核对启动因果。

Phase 2R 的核心条件是新版冷启动、真实助手选区、入口位置、用户与输入框排除以及物理点击后保持 Codex 前台。全部通过后再建议进入 Phase 3。
