# Phase 6B 补充：跨上下文存储视图修正

2026-11-19。原 60 分钟观察于 12:34:39 启动，在真实历史窗口出现读取提示后于 21.60 分钟停止，保留 `artifacts\runtime-6b-20261119-1235\partial-result.json`，health_pass=false，不将其计为健康长时 PASS。

## 可复现证据

工具侧同名 `%LOCALAPPDATA%\ContextNotesNative\data\history.db` 读取为 12 旁注/66 消息，69,632 字节；真实桌面 SaveFileDialog 在同名路径显示 40 KB 文件。使用该运行实例自身的 SQLite 备份按钮导出到工作区后，独立校验为 **1 旁注/0 消息**、schema 2、integrity_check=ok。两边都有完整性，但不是同一个数据视图。

UI 导出文件：`<local-temp>\context-notes-phase6-results\history-ui-view-20261119-1304-0aa7.db`。另一个来源的显式位置为 Codex 包 `LocalCache\Local\ContextNotesNative\data\history.db`。在工具进程里 canonical 和 LocalCache 路径的文件 ID 相同；这不证明桌面子进程看到的也是同一物理文件。此差异与打包/非打包上下文的 AppData 虚拟化相符，不能只靠父进程路径或文件哈希作判断。

同一读取代码对独立 12/66 一致性副本能够正确反序列化，所有 quote/title 非空。因此未用“删坏记录”处理，也未覆盖原库。为以后故障补充安全诊断：历史窗口仅记录错误类型，提示中带类型，不写异常消息或聊天内容。

## 非破坏性修正与迁移

持久数据与主运行日志统一到 `%USERPROFILE%\.context-notes-native`，避开 AppData 虚拟化树。数据库为 `data\history.db`，日志为 `logs`，安装恢复备份为 `backups`。仍使用原 SQLite、原 Companion/Watcher、原官方 App Server 与交互桌面机制；没有修改 Codex 包清单、注册表虚拟化设置、ACL 或其他用户配置。

若新库不存在而检测到旧库，默认路径解析器及安装管理器会拒绝静默创建一个空替代库。升级需先导出并核验一致性备份，再以 `-LegacyHistoryBackup` 导入一个**原本不存在的目的库**。已有目的库会被拒绝，旧源库不迁移、不覆盖。多视图有不同记录时，必须先分别保全，不能只按数量选一份并丢弃另一份。

本机两侧 schema 相同，annotation/message ID 无重叠，设置无冲突。以只读来源在全新文件中事务合并：12/66 + 1/0 = **13/66**，外键错误 0，integrity_check=ok，UI 来源记录缺失 0。保留了那条未绑定账号的草稿，不擅自归入当前账号。

合并备份：`<local-temp>\context-notes-phase6-results\history-merged-20261119-shared-v1.db`，SHA-256 `D3245108C86369BF5BA1CCA26C7DBCD4E1EBD4ED40DFAAACC12D72E2C06919B0`。随后用 SQLite Backup API 写入此前不存在的共享用户库；创建时 SHA-256 `3036782425D7394920BC047244D714359E9C827D711853522FECA13AC6814EBA`。SQLite 后续打开 WAL 会改变文件级哈希，持续保护以一致性备份和记录对比为准。

两侧原库、所有旧备份、异常空备份均保留；没有直接覆盖或删除任一原数据库。工作区诊断副本均在忽略目录，不进入 Git 或发布包。

## 真实回归

安全停止所有项目进程后替换四个已核对运行文件，再从真实交互桌面启动。Companion **5220**、Watcher **31632**、项目 App Server **2632**；Session 1。新日志在共享目录正确写入当前时间。Ctrl+Alt+H 实际显示之前丢失的原历史及三个合成旁注，当前账号 12 条，另 1 条保留原空账号作用域；工具侧总计 13/66，两侧现在一致。

新增四项存储路径回归：不同 LocalAppData 视图解析相同新存储、存在旧库时阻止空替代、迁移后优先新库且不碰旧文件、缺失用户配置不能退回工作目录。全量 **102/102**，失败 0、跳过 0，全部旧 98 保留。安装专项另增加新目标导入和源备份不变两项，最终包会在两个 PowerShell 宿主重跑。

重新完整观察于 **13:13:59** 启动，目录 `artifacts\runtime-6b-shared-20261119-1313`，基线为合并备份，开始时一个 Companion、一个 Watcher、一个按需 App Server。采样期间不替换运行代码；30/60 分钟结果将在最终验收记录追加，不能用故障轮累计时长凑足。
