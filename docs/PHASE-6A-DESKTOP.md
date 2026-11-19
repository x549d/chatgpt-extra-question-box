# Phase 6A 真实桌面验收记录

2026-11-18，Asia/Shanghai；基线 `50ea0b5`，main、CLEAN，无远端。没有把此前结果当作本轮 PASS。

## 启动与数据保护

本轮实际查询 Companion 24972、Watcher 39416、项目 App Server 39672，Session 1。UIA 重新识别 Codex Chat 助手选区。程序启动路径继续由 RequireInteractiveDesktop 验证 WinSta0/Default；没有改变 Phase 2R 架构。缓存四个运行文件哈希全部与上轮最终载荷一致，程序集 0.3.0.0。

运行前一致性备份：`%LOCALAPPDATA%\ContextNotesNative\backups\history-before-6a-20261118-223253.db`。SQLite Backup API，schema 2、integrity_check=ok、6 旁注/26 消息；SHA-256 `BAED0EAA6C2ED2A628679C77522EBD44D6ADE7DB78E67577FFC91764D41915A0`。原库保留。

起始发布文件重新核对：setup 2,282,709 字节，SHA-256 `E9EA822D5EE68B746700BBD2F0B300DA045E89062F81853267DD51602CD7CF7C`；portable 237,326 字节，SHA-256 `DD9436303412C277F746540FAAF19C2AA74B36E4DC81B46DA075C4633AD6D3F7`，与 SHA256SUMS 一致。

## 当前真实证据

| 项目 | 本轮结果 |
| --- | --- |
| 真实 Codex Chat | PASS，通过当前 OpenAI.Codex 包的实际窗口确认 |
| 中文助手划词 | PASS，真实拖动选择 21 字符；UIA role=assistant，eligible |
| 项目提问按钮 | PASS，截图显示项目的“提问”，日志 overlay_visible=true；没有把客户端自带“在侧边聊天中提问”菜单误认成项目按钮 |
| 位置不遮选区/输入框 | 本次截图 PASS，项目按钮位于选区右侧，输入框在下方；其他滚动/移动场景待测 |
| 自动选择浮窗 | BLOCKED，窗口 inventory 不返回该工具浮窗；不伪造 ID、不跨窗口注入 |
| 原文引用/中文输入法/Enter/追问/Markdown 表格/代码复制/停止/关闭折叠 | 用户手测确认；代理未独立操作浮窗，不能写成代理独立观测 |
| 流式逐字显示 | 用户总体确认，未取得独立逐帧证据 |
| 三个新旁注独立 Thread/历史 | PASS，真实落库三条、各 6 消息（3 user/3 assistant，全部回答已提交）、3 个非空独立 Thread、同一正确账号作用域 |
| 并发串流/关闭及停止隔离 | 用户按给出的步骤回复完成；未取得足以证明生成时间实际重叠的独立证据，严格并发验收仍 NOT TESTED |
| 用户消息选区 | PASS，真实选择用户“完成”，role_user、overlay_visible=false |
| 输入框草稿选区 | PASS，仅输入并选中未发送的合成草稿，role_composer、overlay_visible=false；随后清理该合成草稿，没有发送 |
| 空选区 | PASS，清除选择后 selection_empty，项目按钮隐藏 |
| 英文助手选区 | PASS，真实选择 Thread，role=assistant、eligible，截图显示项目按钮 |
| 选区滚出视口 | PASS，实际向上滚动使原英文选区离开视口，日志 selection_outside_viewport、overlay_visible=false；滚动后的异步检查确认隐藏 |
| 窗口移动/多行代码按钮/失焦 | 窗口拖动和系统菜单移动尝试未取得实际坐标变化证据，NOT TESTED；多行代码选区入口未独立测试；失焦隐藏有本轮运行日志，未作为完整多窗口矩阵验收 |
| 历史搜索 | PASS，Phase 6A 问题搜索实际返回两条命名匹配记录；第三条通过其原文“忆与持久化”匹配。未假设每条都使用相同问题前缀 |
| 日期筛选 | PASS，创建日期起点 2026/11/19 列表为空，恢复 2026/11/18 后目标重现 |
| 归档/恢复归档 | PASS，新建三轮合成旁注已归档→未归档，更新时间同时变化 |
| 软删除确认/回收站恢复 | PASS，实际确认可恢复删除，再从回收站恢复该合成旁注；未操作原六条数据 |
| 永久删除 | NOT TESTED；未获具体合成目标的动作前确认，不删除用户现有数据 |

向用户给出最少浮窗验收步骤后，用户首次回复“都完成”。只读核对只发现两条、分别三轮和一轮，因此没有直接标记三旁注通过；请用户补齐后收到“完成”，再次只读核对三条均为三轮，9 旁注/44 消息，原 26 消息差异 0，integrity_check=ok。用户手测确认与代理亲自观察分开记录。

原生软删除确认框的 AX 按钮索引不在缓存中；改用接口正式返回的第二张对话框截图及 screenshotId 完成确认，没有伪造窗口或跨进程点击。曾检测到用户在同窗口输入，重新观察后再继续。结束时关闭历史管理窗口，清除聊天选择，保留客户端及后台运行。

阶段结论：主要旁注功能获得用户实测与数据库证据，代理完成上述入口/历史项目；**6A 不作无条件全 PASS**。永久删除、实际并发重叠、窗口移动矩阵等仍未验收；不能用后续单元测试替代。

---

## Phase 6D 最终构建补充验收（2026-11-19 14:41～14:42，构建提交 `88be709`）

在完成共享存储视图修正（`7e1df68`）、旧轮次通知过滤修正（`aa6ba03`）和安装器修正（`88be709`）后，用户在真实交互桌面 `WinSta0\Default` 对最终部署版本（`SelectionProbe.dll` SHA-256 `C0563F6959CB8EDBBFD14BDD2392EE5B60840538A8BBFDB6865F07D267B3CDFE`）补齐了以下真实交互验收：

- **真实窗口移动与重定位（PASS）**：`companion.log` 记录 Codex 主窗口真实拖动产生 4 组不同窗口坐标 `window=[342,321,1779,1080]` → `[547,388,1779,1080]` → `[315,353,1779,1080]` → `[323,345,1779,1080]`，移动期间触发 `overlay_hidden_reason=window_moved`，停稳后「提问」按钮正确重定位到 `px=[1769,1078,99,45]` → `[1536,1042,99,45]` → `[1280,1012,99,45]` → `[1544,1034,99,45]`；旁注小框同时记录用户手动拖动 `reason=user_drag px=[1533,780,486,564]`。
- **点击「提问」、自动聚焦、原文引用与流式 AI 问答（PASS）**：`annotation_entry_clicked` → `focus_acquired keyboard_focused=True` → `annotation_opened quote_exact=True` → `app_server_initialized pid=27300 latency_ms=191` → `annotation_turn_completed response_length=13`（9.25 秒完成），日志 `error_type` 全部为 `none`。
- **多行代码选区入口说明（PARTIAL）**：用户确认在助手回答中测试了选区与窗口拖动，但本次日志记录的有效选区为单矩形（`rectangle_count=1`, `text_length=21`）；多行代码原文逐字引用与折叠渲染已在 Phase 1/3 实测及自测/xUnit 中验证，但本轮未新增 `rectangle_count > 1` 的独立 UIA 日志，保持如实记录。
- **永久删除与严格 UI 三浮窗同时并发**：仍保持 `NOT TESTED`（不擅自永久删除用户历史；服务层真实三线程同时 `active=3` 已通过但不能替代三浮窗同时点击 UI 证据）。
