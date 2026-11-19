# Phase 6 最终验收与发布判定

日期：2026-11-18～19，Asia/Shanghai。版本 0.3.0 Preview。区分代理实际操作、用户手测确认、自动回归及未验证事项，不以单元测试替代桌面验收。

## 当前判定

| 使用范围 | 判定 | 条件或阻断 |
| --- | --- | --- |
| 本机内部使用 | 待最终回归、采样及部署核验 | 真实划词、历史与 AI 已验证；间歇请求超时应可见且保留失败记录 |
| 公开预览分发 | FAIL | 标准 EXE 仍失败、缺少干净 Windows 验收、项目分发授权与隐私联系主体待维护者确认 |
| 正式稳定版 | BLOCKED | 还缺可信签名、完整桌面矩阵及上述分发条件 |

没有推送、正式 Tag 或 GitHub Release，没有购买证书、修改 ACL、静默提权或读取认证 Token。进行中的采样、最终构建及部署须在完成后追加，当前不表示所有 Phase 6 要求已通过。

## Git 与阶段记录

基线 `50ea0b5`；main，无远端。

| 提交 | 内容 |
| --- | --- |
| `c05036a` | 6A 桌面入口、历史操作及三旁注用户验收 |
| `d1bfbd7` | 6B Companion 恢复退避修正及运行采样 |
| `f7fbe71` | 6B 完整客户端冷启动与原 Thread 续问 |
| `7d120f3` | 6C 隔离安装 AppId、CLI stderr 和旧 PowerShell 测试兼容 |
| `7e1df68` | 6B 两个 AppData 视图的非覆盖合并与统一数据根目录 |

6D 文档、许可清单及发布审计门禁另行提交。最终提交和 CLEAN 状态在末尾记录；包内 release.json 的 source_commit 指向实际 CLEAN 构建提交，最终报告可随后形成只改文档的提交。

## 6A 真实桌面

详细证据：[6A 记录](PHASE-6A-DESKTOP.md)。实际目标为 OpenAI.Codex Windows 客户端 Chat，Session 1，保留 RequireInteractiveDesktop 对 WinSta0/Default 的强制验证。

| 项目 | 结果与证据范围 |
| --- | --- |
| 中文、英文助手选区及“提问” | PASS；真实拖选、截图、UIA role=assistant 和 overlay_visible 日志相互印证 |
| 用户消息、输入框草稿、空选区、选区滚出视口 | PASS；实际操作分别触发隐藏，合成草稿未发送 |
| 原文引用、中文输入法、Enter、流式回答、追问 | PASS（用户手测确认）；控制接口不能选择工具浮窗，代理未独立操作全部步骤 |
| Markdown 表格、代码复制、停止生成、关闭/折叠 | PASS（用户手测确认）；历史恢复截图另显示原引用及代码 |
| 三旁注独立 Thread/历史、账号作用域 | PASS；实际落库三条，各 3 user/3 assistant，三个不同非空 Thread、正确作用域 |
| 搜索、日期、归档/取消、软删除确认/恢复 | PASS；真实历史窗口，仅操作本轮合成目标 |
| 永久删除 | NOT TESTED；未删除任何用户原记录 |
| 严格同时生成不串流、停止/关闭隔离 | NOT TESTED；用户总体确认，但缺少生成时间实际重叠的独立证据 |
| 窗口移动、多行代码选区及完整定位矩阵 | NOT TESTED；不把无坐标变化的移动尝试算成功 |

6A 不作无条件全 PASS。首次用户反馈完成时只读核验仅两条且轮数不足，要求补齐后才确认三条各三轮，没有直接按口头反馈伪造落库结果。

## 6B 冷启动、恢复与数据

详细记录：[恢复与冷启动](PHASE-6B-RUNTIME.md)、[存储视图故障与修正](PHASE-6B-STORAGE.md)。

- 完整客户端冷启动 PASS：原 14 个客户端进程/启动时间均消失，新客户端根 PID/启动时间变化；Watcher 重建 Companion，真实历史恢复。用户完成 COLD_OK，代理截图及落库证明原 Thread/账号不变、旧消息不丢失。
- Companion 正常/异常退出恢复 PASS；修正同客户端 PID 内漏恢复，保留 10 秒退避，只有已验证 Codex 前台触发。项目 App Server 随所有者退出，主客户端 App Server 未被终止。
- Watcher 重建 PASS（既有启动入口）；没有系统服务保证其被杀后自行复活。完全停止产品应先停止 Watcher。
- 生成中断磁盘恢复回归 PASS：保留草稿/部分回答、标记失败；真实生成中杀进程及服务端任务跨崩溃恢复 NOT TESTED。
- 完整客户端冷启动发生于共享存储修正前；修正后已实际停止并重新启动整个项目、验证真实历史与跨进程读写，未再次完整退出 Codex，不能混淆。

### 两个实际数据视图

旧同名 `%LOCALAPPDATA%\ContextNotesNative\data\history.db` 在桌面与工具上下文分别为 1/0 和 12/66（旁注/消息）。运行实例自身备份 UI 导出及独立完整性核验确认两个有效视图，与 MSIX AppData 虚拟化相符；父进程看到相同路径/文件 ID 不证明桌面也看相同物理文件。

保全来源后在全新文件事务合并为 13/66，ID 无重叠、设置无冲突、外键错误 0、integrity_check=ok。合并备份 SHA-256：`D3245108C86369BF5BA1CCA26C7DBCD4E1EBD4ED40DFAAACC12D72E2C06919B0`。随后用 SQLite Backup API 导入此前不存在的目标。

当前统一为 `%USERPROFILE%\.context-notes-native`，库 `data\history.db`、日志 `logs`、恢复备份 `backups`。两侧旧库及所有备份保留。若新库缺失而发现旧库，路径解析器和安装器拒绝静默创建空替代；显式导入仅允许新的目的文件。未改用户原库、Codex 包、安全设置或其他插件。

原 6 旁注/26 消息及旧三条各 8 消息保护不变。6A 后 9/44，冷启动续问后 9/48，新增三个合成 AI 旁注后 12/66，合并空草稿后 13/66。后续合成续问/失败记录不计为原消息丢失，按记录集合对比而非文件大小验证。

### 长时观察

第一轮 12:34:39 启动，因真实历史读取故障于 21.60 分钟停止，health_pass=false，不拼入后一轮。

新一轮于 **2026-11-19 13:13:59 +08** 启动，间隔 30 秒，输出 `artifacts\runtime-6b-shared-20261119-1313`。期间不替换运行载荷，配合真实历史与合成 AI 操作；不是持续 60 分钟人工压力，active_ui_workload_verified=false。

13:20～13:23 三条原 Thread 续问：两条完成，一条 AI 响应超时。失败助手消息 Failed、committed=false，原消息完整，ThreadRecoveryRequired=false，不能写成三条全通过。重试、30/60 分钟指标及最终基线对比将在结束后追加。

## 6C 安装器

详细证据：[安装与权限](PHASE-6C-INSTALLER.md)。

| 项目 | 结果 |
| --- | --- |
| 非管理员父进程权限探针 | PASS 4/4：HKCU32、HKCU64、Programs/Temp 自有目录，仅清理自建探针 |
| 标准 EXE 手动启动 | FAIL；用户截图：无法创建 `AppData\Local\Temp\is-….tmp`，Error 5，发生于向导/项目预检前 |
| 标准 EXE 工具启动 | FAIL；“操作已被用户取消”，无成功安装日志/程序文件 |
| 仅本进程 TEMP/TMP 诊断 | FAIL；临时目录可创建，但项目预检 HKCU64 测试卸载键写入拒绝，未写程序文件 |
| 必须管理员/HKLM | 无功能需求；保持最低权限/HKCU，不提升、不修改 ACL |
| 测试注册隔离 | PASS；TESTROOT 从规范化路径导出 AppId，真实日志验证独立测试键；生产 GUID 不变 |
| 便携安装管理器 | 中间 ZIP 两个宿主各 19/19，最终 CLEAN 包需重验 |
| 干净 Windows | NOT TESTED；没有可用 Sandbox/VM，未擅自安装或启用虚拟化 |
| 标准 EXE 安装、升级、卸载 | NOT TESTED（启动已失败）；脚本隔离不能替代 |

父进程可写而安装器拒绝，最终外部策略原因尚未完全定位，不能简单宣称需要管理员或签名即可解决。实际测试卸载键在日志中为 `HKCU64\Software\Microsoft\Windows\CurrentVersion\Uninstall\ContextNotesNative.Test.<测试根 SHA256>_is1`；无 HKLM 写入需求。

管理器测试包括隔离安装、同版重装、文件/插件/Hook 哈希、CLI stderr、升级/迁移、SQLite 备份、失败回滚及失败恢复、默认保留历史卸载、明确删除合成库、无关配置保留、新目标导入与源备份不变。独立 profile/data/startup、真实官方 CLI、SkipLaunch，不证明干净机器 UI。没有在用户在用环境破坏性卸载。

## 6D 发布准备

增加 staging 与 ZIP 双侧逐文件哈希核对、原提交存在性和运行文件未签名检查；载荷不带库、日志、源码、PDB、auth.json/config.toml 或验收报告。release.json 明确 preview、NotSigned、formal_release_ready=false。

应用通过官方 App Server 处理已选引用、用户问题及恢复对话，不向无关服务器上传本地历史，不读取 Cookie/密码/进程 Token，不改变 OAuth。SQLite 参数绑定、WAL/事务/外键及 Backup API 保持；数据库/备份无应用层加密。软删除可恢复，永久删除不能代表清除独立备份或 Codex 自身会话。

当前无有效可用私钥的可信代码签名证书，EXE/DLL 为 NotSigned；签名 **BLOCKED**。没有自签冒充或采购。[签名方案](SIGNING-PLAN.md) 包含官方步骤、费用示例及主体地区限制。

[第三方清单](THIRD-PARTY-NOTICES.md) 区分 .NET/WPF 前置依赖、Windows 系统库、官方 Codex 和仅测试的 xUnit/Test SDK。Inno 本机许可文字允许列明条件下商业使用，未把编译器提示误写成已采购商业授权。本项目 LICENSE 保留原权利状态，公开分发授权、PRIVACY 真实发布主体/联系方式待维护者确认。

README、CHANGELOG、安装、用户指南、排查、安全、隐私、签名与许可清单已更新。最终构建/测试发现数、凭证扫描、产物大小/SHA、部署和 CLEAN 状态将在真实完成后追加。

## 剩余验收

1. 30/60 分钟采样与最终构建、部署核验（进行中）。
2. 标准 EXE 安装/升级/卸载及干净 Windows 首次启动/划词/AI。
3. 存储修正后的再次完整客户端冷启动、严格并发及窗口移动/多行代码/永久删除矩阵。
4. 真实生成中崩溃的服务端任务恢复，现行为保留内容、明确失败、不自动重发。
5. 可信签名、公众分发许可和隐私联系主体。

打包成功不会使这些项目自动 PASS。
