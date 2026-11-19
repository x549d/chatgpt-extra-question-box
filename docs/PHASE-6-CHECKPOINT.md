# Phase 6 工作断点：用户要求暂停

保存时间：2026-11-19 14:08，Asia/Shanghai。用户明确要求保存当前阶段并暂停。**不要自行恢复采样、桌面操作、AI 请求、安装、测试或发布，等待用户说继续。** 已部署应用保持运行，停止的是工作和采样。

## 已保存状态

- 项目 `<repo-root>`，main，无远端；不 push、不建正式 Tag/Release。实现及最终载荷源码提交 `aa6ba0343edc29144eba16da010f258677b6709c`，构建时 CLEAN；本断点另作纯文档提交。
- 6A 入口/搜索/日期/归档/软删除恢复实测，浮窗由用户手测。三新旁注各三轮、独立 Thread 已落库。严格 UI 并发、永久删除、真实窗口移动和多行代码入口仍非完整验收。
- 6B 已完成完整客户端冷启动及原 Thread COLD_OK、Companion 正常/异常退出恢复。完整客户端冷启动发生于最后存储/通知修正前，不能把它写成最终字节的再次完整客户端冷启动。
- 修复两个真实故障：两种 AppData 视图导致历史不一致；恢复线程的上一轮用量通知抢占新问题 turnId 导致虚假超时。旧库原地保留，事务合并到全新库；未覆盖/删除用户原库。
- 当前数据 `%USERPROFILE%\.context-notes-native\data\history.db`，日志 logs、备份 backups。已确认 13 旁注/102 消息；原 6/26 及旧三条各 8 消息完整，原引用/Thread/账号差异 0。此前失败消息保留。继续时重新只读确认，不假定数量或 PID 永远不变。
- 最后全量回归：Release 0 警告/0 错误；xUnit 106/106、失败 0、跳过 0；自测 80/80；两个 PowerShell 宿主安装管理器各 19/19；发布审计 12/12；当前用户权限探针 4/4。Git 历史/载荷 gitleaks 无发现。
- 真实修复后服务层三线程同时 active=3，约 9.67 秒全部完成、原 Thread 不变、保存正常；最终 DLL 的三条顺序续问也全部通过。不能替代三工具浮窗同时操作的 UI 验收。

## 最终载荷与部署

载荷 `artifacts\payload-e9f1a056c9ba4e50a5f5c5d0d804f374`，source_dirty=false，NotSigned，formal_release_ready=false。EXE 2,287,221 字节，SHA-256 `CE524B75D62A4C5D33E5533320E95DC84855A8F35674A5553C761B43B2321EA4`；ZIP 243,201 字节，SHA-256 `71B04EFF8BDCD370788F69AA34B7B89AA99F84EC5C8F8F27248CF64EE841585D`。SHA256SUMS 同目录。

安装目录 `%LOCALAPPDATA%\Programs\ContextNotesNative\versions\0.3.0`，缓存 `%USERPROFILE%\.codex\plugins\cache\context-notes-native\context-notes\0.3.0`，与工作区 bin 四个运行文件哈希一致。DLL `1F4D8E03AA864F21956FAD33BA4EA542F9AE197BDBBAB31D88F06E482DC3C759`。通过真实交互桌面重启整个项目，Companion 22064、Watcher 23764、项目 App Server 18476，Session 1；实际历史可见，划词按钮及失焦隐藏已再测。窗口拖动无坐标变化证据，不能标 PASS。

部署前新建一致性备份 `%USERPROFILE%\.context-notes-native\backups\history-before-final6-20261119-134326.db`，schema 2、integrity ok，SHA-256 `E9E9B0033BD3F230D1297F37F04A43F6BC91048269353648F2833481CED6A7EF`。旧程序/本项目状态另保留于 `artifacts\deployment-recovery-fd52805e848c406692367ab90d40f5a3`。旧 AppData/LocalCache 来源与所有旧备份保留，避免覆盖原库。

## 采样断点

1. 第一轮 12:34：历史读取故障，21.60 分钟停止，health_pass=false。
2. 第二轮 13:13：轮次通知缺陷，26.635 分钟停止，health_pass=false。
3. 最终修复轮 **13:44:45～14:07:51**：用户暂停，**47 样本/23.111 分钟**；观察器 35380 已停止，`artifacts\runtime-6b-final-20261119-1345\partial-result.json` 标记 stopped_by_user_pause、complete_60_minutes=false、health_pass=null。原始 samples.jsonl 保留；不是 30/60 分钟通过。不得把三轮累计或暂停期间程序运行时间凑成完整测试。

当前区间三个常驻角色各 1 且 PID 不变，Companion 私有内存 84.83→97.43 MiB，峰值 103.76 MiB；CPU 整机归一均值 0.1668%、峰值 0.6445%。期间有真实历史/划词和 AI 操作，但不是持续人工 UI 压力测试。

## 剩余工作与恢复顺序

1. 用户重新授权后先核验实际源码/进程/载荷和只读数据完整性；需要长时验收时选择新输出目录重新完成连续 30/60 分钟，不能续写已有观察目录或声称当前 23 分钟足够。
2. 继续时用实际窗口/AI/持久化证明健康，记录内存、CPU、进程数量、DB/WAL、错误和基线消息差异，再作最终运行判定。固定程序已部署，不必无理由重复编译或重启。
3. 6C 标准 EXE 仍 FAIL：用户真实 Error 5 无法创建 Temp/is-*.tmp；仅本进程 TEMP/TMP 诊断可进入 HKCU64 预检，但测试卸载键仍拒绝。父进程四项权限探针通过不等于安装器成功，外部策略原因未证实。不改 ACL、不静默提权。
4. 无 Sandbox/VM，干净 Windows 的安装、升级、卸载及首次划词/AI NOT TESTED。隔离管理器 19 项用了 SkipLaunch，不能替代。需要可用且获授权的独立环境。
5. 严格 UI 并发、窗口移动/多行代码/永久删除矩阵、最终修正后再次完整客户端冷启动、真实生成中崩溃等仍有缺项；不擅自永久删除数据或退出承载本聊天的客户端。
6. 可信签名无可用证书，BLOCKED；不采购或自签冒充。公众分发许可、PRIVACY 发布主体/联系方式需维护者确认。内部本机已验证范围可用，完整长时判定待补；公开预览 FAIL、正式稳定版 BLOCKED。
7. 后续更新最终报告、必要时纯文档提交，保持 CLEAN；无明确授权不 push/Tag/正式发布。

完整证据见 [最终报告](PHASE-6-ACCEPTANCE.md)、[存储修正](PHASE-6B-STORAGE.md)、[通知修正](PHASE-6B-TURNS.md)、[安装器](PHASE-6C-INSTALLER.md)。忽略目录的诊断原始输出位于 `<local-temp>\context-notes-phase6-results`，不提交数据库、问答或日志。
