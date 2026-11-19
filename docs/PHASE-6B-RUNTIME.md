# Phase 6B 冷启动与恢复验收记录

后续状态（2026-11-19）：用户已恢复全部操作并重新授权 60 分钟采样，下文“暂缓”为当时记录，不再表示当前指示。首轮因历史视图故障在 21.60 分钟停止；修正与新一轮 13:13:59 启动的完整采样见 [存储记录](PHASE-6B-STORAGE.md) 和 [最终报告](PHASE-6-ACCEPTANCE.md)，不累计故障轮凑满时长。

2026-11-18，Asia/Shanghai，接续 6A `c05036a`。此页是可恢复的执行记录，未完成的冷启动和 30/60 分钟观察不标记 PASS。

## 数据保护

6A 补验后共 9 旁注/44 消息，三个新旁注各三轮、三个独立 Thread。每次重启前确认一致性备份存在：`%LOCALAPPDATA%\ContextNotesNative\backups\history-before-6b-20261118-230244.db`，SQLite Backup API、schema 2、完整性 ok，SHA-256 `2074F7253D64923D8B165BC30892563131E52DD99673F7DAA8C1245C652BDF7C`。没有覆盖原始数据库。

## 实际发现和修正

原 Watcher 的门控在同一 Codex PID 成功启动后永不再尝试。实际测试：正常停止 Companion 24972，其 App Server 39672 随所有者退出；通过既有 Watcher 重启得到 13088。随后仅终止已核对路径与父进程的 Companion 13088；12 秒后仍 companion_not_running，确认自动恢复缺陷。立即通过原重启信号恢复为 38824。

最小修正：Watcher 继续使用原 DesktopStartup、单实例锁、交互桌面与前台识别；在已验证 Codex 前台期间额外检查 Companion 实际就绪状态，缺失时按既有 10 秒间隔重试。健康实例不启动额外 launcher。只把原有 IsReady 改为内部可复用，未重写核心启动架构。

安全停止旧 Watcher/Companion 并等待退出后，仅更新缓存四个运行文件。从真实交互桌面启动本项目既有入口，得到无参数 Companion 18748、Watcher 27872。桌面控制接口报告后台没有可选择窗口，但随后命名事件/实际历史窗口证实启动成功。按 Ctrl+Alt+H 真实恢复历史，App Server 25288 按需初始化 193ms。

修正后实际回归：

| 场景 | 结果 |
| --- | --- |
| 正常退出清理 App Server | PASS，旧所有者退出后其项目 App Server 退出，主客户端 33644 未被终止 |
| 异常退出自动恢复 | PASS，核对属于项目的 18748 后模拟退出，Watcher 27872 自动拉起 30176；新进程出现约 617ms，原 App Server 25288 已清理 |
| 正常退出后自动恢复 | PASS，正常停止 30176，Watcher 在 10 秒重试间隔后自动拉起 6428；就绪检查约 10,561ms |
| 单实例 | 以上恢复过程中确认一个 Companion、一个 Watcher，UIA worker 是独立角色 |
| 重启后的持久化 | PASS，9 旁注/44 消息、integrity_check=ok，与重启前备份消息差异 0 |
| Watcher 重建 | PASS，停止后通过原应用桌面入口重建；不声称 Watcher 自身被终止后有独立服务自动复活它 |
| 生成中断 | 新增磁盘重开回归通过：恢复 Thread/草稿/部分内容，状态标记失败且不自动重发；真实活跃服务端生成期间杀进程尚未测试，不宣称服务端任务可恢复 |

自动恢复以**已验证 Codex 前台**为条件；停止产品时应先停止 Watcher 再停止 Companion，避免与恢复机制互相竞争。失去全部启动入口不等同于系统服务自恢复。

## 自动化回归与采样工具

新增 Phase6RecoveryTests 共 10 个实际发现用例：冷前台、健康实例、同客户端异常退出退避、恢复失败退避、新客户端 PID、无前台（两例）、Connecting/Generating 磁盘恢复（两例）、坏备份不能覆写最后好备份。全部通过。

全量 dotnet test：98/98，通过 98、失败 0、跳过 0，保留原 88；项目自测 80/80，失败 0。TRX：`<local-temp>\context-notes-phase6-results\phase6b.trx`，不提交日志。

`scripts/Measure-Runtime.ps1` 是只读观察工具，按 PID/启动时间/归属分类 Companion、Watcher、UIA worker、项目 App Server（包括无参数 Companion），记录内存、CPU、线程/句柄和 DB/WAL/SHM 大小；结尾只读完整性与原记录对比。不会停止进程、创建生产数据库、保存问答或 Token。重复输出目录拒绝混合采样。一分钟试运行 5 个样本/60.35 秒通过，integrity ok、旧消息和标识差异 0；**一分钟不代替 30/60 分钟验收**。

## 冷启动前断点与后续执行

冷启动前客户端根 PID **3136**，启动 19:24:45；主客户端 App Server **33644**。全部 14 个 ChatGPT.exe 进程的 PID/父 PID/启动时间保存在忽略文件 `artifacts\phase6b-client-before.json`，EXE 来自 `WindowsApps\OpenAI.Codex_26.1111.2228.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe`。

当前本任务运行于待测 Codex 客户端内，完全退出会断开本次工具连接。因此先提交恢复修正与断点，交由用户从应用菜单完整退出、等待约 70 秒再重新打开本聊天。需要重连后证明旧客户端进程均退出、新根 PID/启动时间变化，并检查 Companion/Watcher/UIA/历史及真实续问。不能仅凭用户关闭了窗口宣称冷启动通过。

重连后的执行顺序：

1. 比对旧客户端 PID 与新根进程，重新列出桌面窗口并绑定，确认历史和续问。
2. 使用 Measure-Runtime 进行真实 60 分钟采样，分别在 30/60 分钟记录结果；结合实际窗口/AI/DB 观察，不能仅凭进程存在宣布健康。期间不部署不同运行代码。
3. 完成 6B 记录后，进入 6C 安装权限/隔离环境与便携包复验，再进入 6D 依赖许可、安全审计、签名成本和发布材料。
4. 最终全量回归、从 CLEAN 源码打包、部署最终载荷、更新 PHASE-6-ACCEPTANCE.md；各阶段独立提交，最终 CLEAN，不 push/Tag/公开发布。

当前只是用于实际恢复测试的中间载荷：`artifacts\phase6b-runtime` 已部署到插件缓存；安装目录、原 EXE/ZIP/SHA256SUMS 尚为 Phase 5J 产物，不能声称最终发布包已包含本轮修正。6C/6D 尚未完成，签名/干净环境限制没有被解除。

## 冷启动后实际结果（23:22～23:26）

用户明确完成完整退出并重开后，本轮逐个比对 PID 与启动时间：原 14 个客户端进程均不再存在；新根 **38188** 于 23:21:15 启动，原主客户端 App Server 33644 结束，新主客户端 App Server **20316 / 38188** 于 23:21:18 启动。这不是关闭窗口再显示。

旧 Companion 6428 结束，原 Watcher **27872** 保留；日志于 23:21:19 识别新 Codex PID，自动拉起 **Companion 7576 / 27872**，Session 1，UIA worker **12924 / 7576** 已订阅。程序仍执行 WinSta0/Default 强制校验。

真实 Ctrl+Alt+H 打开历史，项目 App Server **11960 / 7576** 按需初始化 195ms。刚重开时 schema 2、integrity_check=ok、9 旁注/44 消息；与冷启动前相比消息、Thread/账号/引用差异均 0。

从历史恢复“忆与持久化”，截图实际显示原引用、表格和代码。工具浮窗仍未暴露给控制接口，用户手动发送 `Phase 6B 冷启动续问：只回复 COLD_OK` 并反馈 `COLD_OK`；代理截图也实际观察到回答。SQL 核验该旁注 ThreadId 和账号均与冷启动前相同、回答已提交。期间有一次已停止生成记录及后续成功回答，最终 **9 旁注/48 消息**，旧 44 条消息差异 0、integrity_check=ok。没有把“服务端生成任务跨崩溃恢复”与正常续问混为一谈。

**冷启动、后台自动恢复、历史恢复、同 Thread AI 续问：PASS。**

用户随后明确要求“先不进行60分钟采样”，故没有启动 60 分钟采样，也不将这次会话累计时长或进程运行时长冒充长时测试。30/60 分钟连续交互稳定性均保留 **NOT TESTED / 长时项暂缓**；仅有此前一分钟采样工具试运行，不能证明无内存泄漏。继续执行可独立完成的 6C/6D。

---

## 最终修正版 Codex Desktop 冷启动验收（2026-11-19 14:34～14:43，提交 `88be709`）

本轮使用包含共享存储修正（`7e1df68`）、旧轮次通知过滤修正（`aa6ba03`）及安装器修正（`88be709`）的最终部署二进制，补齐此前缺少的**最终修正版完整冷启动**证据：

- **部署文件校验**：`%USERPROFILE%\.codex\plugins\cache\context-notes-native\context-notes\0.3.0\bin` 与 `%LOCALAPPDATA%\Programs\ContextNotesNative\versions\0.3.0` 一致，`SelectionProbe.dll` SHA-256 `C0563F6959CB8EDBBFD14BDD2392EE5B60840538A8BBFDB6865F07D267B3CDFE`，`SelectionProbe.exe` SHA-256 `662CBC1E41D374D8B0E4D54F81F3FB36B4AF4D01C6239D03A713E4562CEB5C80`。
- **冷启动前数据库备份**：SQLite Backup API 生成 `%USERPROFILE%\.context-notes-native\backups\history-before-phase6d-coldstart-20261119-143409.db`（SHA-256 `4A87E0A7BE54FD1146CFAFCB9808EDB062B1D0028FA74C61EBE74F391B752E2A`，schema 2，`integrity_check=ok`，13 旁注 / 102 消息）。
- **冷启动前进程快照**：`artifacts\phase6d-client-before.json` 记录全部 14 个旧 Codex 进程（原根 PID `15620`，主 AppServer `3596`，启动于 `12:31:46`）。
- **冷启动后真实比对**：
  - 旧 14 个 Codex 进程全部退出，**重叠 PID 数 = 0**；新 Codex 根 PID **`11116`**（启动 `14:41:36`）、新主客户端 AppServer **`9668`**（启动 `14:41:38`）。
  - 交互桌面 Watcher PID **`10716`**（Session 1，启动 `14:41:20`）于 `14:41:48.568` 检测到新 Codex PID `11116`，自动拉起 Companion PID **`29208`**（Session 1，完整性 `8192`，`desktop_startup source=windows_foreground companion=started pid=29208`）。
  - `14:42:26.617` 实测 `SessionStart:startup` Hook 触发并被单实例安全去重（`companion=already_running`）。
  - 项目 AppServer PID **`27300`**（父进程 `29208`，Session 1）于 `14:42:18.633` 按需启动，初始化耗时 `191 ms`。
- **冷启动后首次真实 AI 问答与持久化**：
  - 用户在未向主 Chat 发送消息的前提下，选中助手回答、点击「提问」、发送测试问题并完成流式回答（`annotation_turn_completed response_length=13`，9.25 秒完成，日志无错误）。
  - 只读 SQLite 复核：`integrity_check=ok`、schema 2、**14 旁注 / 104 消息**（新增旁注 `ann-ed886f9c125a4ca4a73c9669fc47881d`，独立非空 ThreadId，账号作用域 `acct-154d33b...`，2 条已提交消息），与冷启动前备份对比 **`baseline_msg_diff=0`、`baseline_note_id_diff=0`**，未复发双 AppData 视图不一致或旧轮次通知抢占故障。

**最终修正版冷启动、后台自动拉起、窗口重定位、AI 流式回答与历史持久化：PASS。**
