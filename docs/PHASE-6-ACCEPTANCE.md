# Phase 6 最终验收与发布判定

日期：2026-11-18～19，Asia/Shanghai。版本 0.3.0 Preview。区分代理实际操作、用户手测确认、自动回归及未验证事项，不以单元测试替代桌面验收。

## 当前判定

| 使用范围 | 判定 | 条件或阻断 |
| --- | --- | --- |
| 本机内部使用 | **PASS** | 真实划词、窗口重定位、三旁注多轮、最终构建冷启动、30 分钟连续稳定性及全量回归均已通过；60 分钟采样已取消（`OPTIONAL / NOT REQUIRED / NOT RUN`） |
| 公开预览分发 | **BLOCKED** | 标准 EXE 在正常中完整性目录与隔离 `/TESTROOT` 下已通过 29/29，但独立干净 Windows OS（Sandbox/VM）为 `NOT TESTED`，且 `LICENSE` 分发授权与 `PRIVACY.md` 发布主体/联系方式尚待维护者正式确认 |
| 正式稳定版 | **BLOCKED** | 缺少可信 Authenticode 代码签名证书（`BLOCKED`）、独立干净 Windows OS 全流程验收、完整 UI 并发/永久删除矩阵，以及维护者确认的法律与隐私文本 |

没有推送、没有创建正式 Tag 或 GitHub Release，没有购买证书、修改系统 ACL、静默提权或读取认证 Token。以下包含最终 30 分钟稳定性采样、标准安装器根因修复、最终构建冷启动及最终 CLEAN 构建与回归结果。

## Git 与阶段记录

基线 `50ea0b5`；main，无远端。

| 提交 | 内容 |
| --- | --- |
| `c05036a` | 6A 桌面入口、历史操作及三旁注用户验收 |
| `d1bfbd7` | 6B Companion 恢复退避修正及运行采样 |
| `f7fbe71` | 6B 完整客户端冷启动与原 Thread 续问 |
| `7d120f3` | 6C 隔离安装 AppId、CLI stderr 和旧 PowerShell 测试兼容 |
| `7e1df68` | 6B 两个 AppData 视图的非覆盖合并与统一数据根目录 |
| `6cc5bbe` | 6D 用户文档、第三方许可清单和 12 项发布审计 |
| `aa6ba03` | 6B 上一轮用量通知抢占绑定修正，106 项回归 |

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

13:20～13:23 三条原 Thread 续问：两条完成，一条超时。重试及三条实际并发进一步证明服务端已完成，本地却未确认全部回答。元数据跟踪定位：恢复线程的 `thread/tokenUsage/updated` 携带上一轮 turnId，在新轮开始之前抢先绑定，导致新轮 delta/completed 被过滤。它是本地事件处理缺陷，不应归因于外部 AI 超时。

最小修正仅允许实际处理的生命周期/回答事件参与轮次匹配，忽略用量、Hook 及未来未知通知；不改变 Thread/账号隔离、请求协议、停止机制或交互桌面架构。新增回归四项：三种旧轮元数据先于 start 响应，及真实回答先于 start 响应；修复前 3 失败/1 通过，修复后全量 **106/106**，失败 0、跳过 0，旧 102 全部保留。

13:13 轮已停止，health_pass=false，故障轮日志与失败消息完整保留，不凑满 30/60 分钟。后续必须部署修正后的固定运行载荷，再重新计时并复验真实并发。参见 [轮次事件修正](PHASE-6B-TURNS.md)。

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

## 固定最终载荷与真实部署（13:43～13:48）

最终实现/构建提交：`aa6ba0343edc29144eba16da010f258677b6709c`，构建时 CLEAN；包内 source_dirty=false、formal_release_ready=false。载荷 `artifacts\payload-e9f1a056c9ba4e50a5f5c5d0d804f374`。本文后续只改验收记录的提交不重新编译程序，避免改变长时观察中的运行字节。

| 发布物 | 字节 | SHA-256 |
| --- | ---: | --- |
| ContextNotesNative-0.3.0-setup.exe | 2,287,221 | `CE524B75D62A4C5D33E5533320E95DC84855A8F35674A5553C761B43B2321EA4` |
| ContextNotesNative-0.3.0-portable.zip | 243,201 | `71B04EFF8BDCD370788F69AA34B7B89AA99F84EC5C8F8F27248CF64EE841585D` |

均在 artifacts，SHA256SUMS 两行及各自 .sha256 已生成；这是未签名预览包，EXE 成功编译并不代表安装通过。

最终源码回归：Release 构建 0 警告/0 错误；xUnit 发现/通过 **106**、失败 0、跳过 0；原项目自测 **80/80**。实际最终包内管理器 PowerShell 7 **19/19**，实际解压 ZIP 的 Windows PowerShell 5.1 **19/19**，各失败 0/跳过 0；没有合计冒充 38 个不同功能。发布检查 **12/12**，当前用户权限 **4/4**。完整 Git 历史与最终载荷的 gitleaks 脱敏扫描均 0 发现；不以扫描代替人工边界审查。

部署前使用最终程序的 SQLite Backup API 创建新的 `%USERPROFILE%\.context-notes-native\backups\history-before-final6-20261119-134326.db`，schema 2、integrity ok，SHA-256 `E9E9B0033BD3F230D1297F37F04A43F6BC91048269353648F2833481CED6A7EF`。旧程序和本项目状态文件另保全于忽略的 deployment-recovery 目录。先停 Watcher 再停 Companion，确认缓存进程退出后同步固定载荷，不覆盖数据库。

安装目录 `%LOCALAPPDATA%\Programs\ContextNotesNative\versions\0.3.0` 全部载荷哈希匹配；缓存、安装目录、工作区 bin 四个运行文件均与发布暂存一致。DLL SHA-256 `1F4D8E03AA864F21956FAD33BA4EA542F9AE197BDBBAB31D88F06E482DC3C759`。官方 CLI 只读确认 marketplace 仍指向此稳定用户目录，其他注册来源保留。启动快捷方式仍指向缓存原路径。没有以手动文件同步冒充标准安装器成功。

真实交互桌面启动新 Companion **22064**、Watcher **23764**，Session 1；实际 Ctrl+Alt+H 恢复历史、项目 App Server **18476 / 22064** 按需启动，初始化 233ms。旧项目 App Server 2632 已退出，主客户端进程仍在。最终部署没有保留加载旧代码的 Watcher。

修正后真实服务层并发：三个不同原 Thread 实际同时 active=3，约 **9.67 秒**全部 Completed，Thread 保持、持久化错误 0。它验证真实官方 AI 与服务层路由，不替代三个工具浮窗同时操作的 UI 证据。最终发布 DLL 的既有 --phase5-live-resume 随后也实际三条全部通过，模型目录 10、用量可读、账号隔离记录 0，原 Thread 均保留；非模拟结果。

13:47 只读核验共 **13/102**，部署前备份的消息差异 0，新失败消息 0。原 6/26 与原引用/Thread/账号逐项差异也为 0。之前故障生成的失败记录保留。

最终完整观察已于 **2026-11-19 13:44:45 +08** 重新启动，输出 `artifacts\runtime-6b-final-20261119-1345`，基线为上述部署前备份。此后不替换固定程序。

## 用户要求暂停的断点（2026-11-19 14:08）

用户明确要求“保存当前工作阶段，暂停工作”。已停止桌面操作、等待循环及本轮采样进程 35380，保留程序运行，没有继续 AI 请求、安装或测试。继续前必须收到新的恢复指示。

本轮采样实际 **47 个样本 / 1,386.657 秒 / 23.111 分钟**，最后样本 14:07:51。partial-result.json 标记 stopped_by_user_pause、complete_60_minutes=false、health_pass=null，不能判为 30/60 分钟通过；暂停与两轮先前故障分别记录。

在已记录区间，Companion 22064、Watcher 23764、其 App Server 18476 始终各一个，无重启；UIA worker 按需为 0～1 个。Companion 私有内存 84.83→97.43 MiB、峰值 103.76 MiB，整机归一 CPU 均值 0.1668%、峰值 0.6445%；Watcher 私有内存 7.75→9.20 MiB，App Server 26.86→22.40 MiB。这些只是 23 分钟观察，不能证明完整长时稳定性。

最终源码、程序、EXE/ZIP/SHA256SUMS 已保留；只有本页及 [恢复断点](PHASE-6-CHECKPOINT.md) 更新，不重新编译或部署。当前进度为 **6B 最终长时验收暂停，6C 安装/干净环境受阻，6D 未签名预览材料及回归完成**。可信正式发布仍 BLOCKED，不推送或创建正式版本。

---

## Phase 6 最终收尾验收（2026-11-19 14:17～15:14，接续 `3d05e77`）

### 1. 分阶段提交记录

基线：`3d05e77`（main，无远端，未 push，未建正式 Tag / GitHub Release）。

| 提交 | 内容 |
| --- | --- |
| `88be709` | `fix(phase6b)`：定位并修复安装器 Temp/HKCU Error 5（Low Mandatory Level 根因）、`CODEX_HOME` 初始化、部署失败退出码与自动回滚、测试 AppId 对齐及 Watcher 轮询；新增 29 项标准 EXE 测试脚本 |
| `46f5525` | `test(phase6c)`：记录标准 EXE 29/29 与便携管理器 19/19 验收及干净 Windows OS 限制 |
| `3e081ee` | `test(phase6d)`：记录最终构建冷启动（0 重叠旧 PID）与真实窗口拖动重定位验收 |
| `6aceb0e` | `docs(phase6e)`：更新第三方许可与 OpenAI 商标边界声明、安全审计、发布说明与 CHANGELOG（CLEAN 构建提交） |
| 最终验收提交 | `test(phase6a)`：记录 30 分钟连续稳定性 PASS 及更新最终验收/断点/README 文档（不修改代码） |

### 2. Phase 6A 长期运行稳定性验收（30 分钟标准）

- **标准调整**：30 分钟连续采样为正式验收门槛；**60 分钟测试标记为 `OPTIONAL / NOT REQUIRED / NOT RUN`**，未执行，不作为发布阻断项。
- **历史 47 样本保全**：`artifacts\runtime-6b-final-20261119-1345`（`13:44:45`～`14:07:51`，23.111 分钟，47 样本）完整保留、未覆盖；因暂停产生间断（情况 B），未与新采样拼接，重新执行完整连续 30 分钟采样。
- **连续 30 分钟有效采样（PASS）**：输出目录 `artifacts\runtime-6a-30min-20261119-1443`，基线为冷启动前备份 `history-before-phase6d-coldstart-20261119-143409.db`。
  - 时间窗：`2026-11-19 14:43:28.721 +08:00` ～ `15:13:28.834 +08:00`，耗时 **`1,800.282` 秒（`30.005` 分钟）**。
  - 样本数：**61 个连续样本**，间隔均值 `30.00` 秒（最大 `30.28` 秒，无间断）。
  - **Companion**：单一恒定 PID **`29208`**（`count=[1,1]`，无重启），私有内存 `107.46 → 105.79 MiB`（`delta -1.67 MiB`，峰值 `109.52 MiB`），整机归一 CPU 均值 `0.0239%`（峰值 `0.0454%`），句柄 `800 → 798`，线程 `27 → 22`。
  - **Watcher**：单一恒定 PID **`10716`**（`count=[1,1]`，无重启），私有内存 `8.66 → 8.68 MiB`（`delta +0.02 MiB`），CPU 均值 `0.0002%`（峰值 `0.0032%`），句柄 `371 → 364`，线程 `8 → 5`。
  - **项目 App Server**：单一恒定 PID **`27300`**（父进程 `29208`，`count=[1,1]`，0 孤儿进程），私有内存 `43.86 → 39.84 MiB`（`delta -4.02 MiB`），CPU 均值 `0.0016%`（峰值 `0.0130%`），句柄 `438 → 391`，线程 `38 → 25`。
  - **UIA Worker**：`[0, 0]`（空闲期无常驻残留）。
  - **异常日志与数据库**：30 分钟窗口内 `companion.log` 异常/错误/崩溃/超时行数为 **0**；SQLite 只读校验 `integrity=ok`、schema 2、**14 旁注 / 104 消息**、`baseline_message_difference=0`、`baseline_note_identity_difference=0`，DB/WAL/SHM 大小恒定（`98,304 / 127,752 / 32,768` 字节）。注：本轮紧接 `14:41`～`14:42` 真实冷启动与 AI 问答之后，采样期间为稳态后台驻留（`active_ui_workload_verified=false`）。

### 3. Phase 6B / 6C 标准安装器修复与验收

- **根因**：`<local-workspace>` 带有 Codex Windows Sandbox 继承 SACL `Mandatory Label\Low Mandatory Level:(OI)(CI)(NW)`，导致直接编译在 `artifacts\` 的 EXE 继承低完整性标签并被 Windows MIC 降级为低完整性进程（`S-1-16-4096`），从而被内核拒绝写入 `%TEMP%`、HKCU 卸载键与开始菜单（Error 5）。复制至正常中完整性目录后无需管理员权限、不修改 ACL 即可通过。详见 [PHASE-6C-INSTALLER.md](PHASE-6C-INSTALLER.md)。
- **标准 EXE 端到端（`Test-StandardInstaller.ps1`，隔离 `/TESTROOT`，中完整性目录）**：**29/29 PASS**（全新安装、Temp/HKCU64 预检、原地重装、安装失败自动回滚与非零退出码 1、升级失败回滚恢复 schema v1、v1→v2 迁移备份、默认保留历史卸载、显式 `/REMOVEHISTORY=1` 删除历史卸载）。
- **便携 ZIP 管理器（`Test-Installation.ps1`，Windows PowerShell 5.1）**：**19/19 PASS**；权限探针：**6/6 PASS**（`effective_admin=False`）；Hook 诊断：**9/9 PASS**。
- **干净 Windows OS（Windows Sandbox / VM / 新账户）**：**NOT TESTED**（本机无可用虚拟化工具，启用功能或新建账户需管理员权限）。

### 4. Phase 6D 最终构建冷启动与桌面验收

- **最终构建冷启动（PASS）**：完整退出原 14 个 Codex 进程（重叠 PID = 0）→ 新 Codex 根 PID `11116`、新主 AppServer `9668` → 交互桌面 Watcher `10716` 自动拉起 Companion `29208` → `SessionStart:startup` Hook 单实例去重 → 项目 AppServer `27300` 按需初始化 `191 ms` → 选中助手回答并完成真实 AI 问答（9.25 秒，新增 `ann-ed886f9c125a4ca4a73c9669fc47881d`，落库 14/104，`baseline_msg_diff=0`，0 错误）。详见 [PHASE-6B-RUNTIME.md](PHASE-6B-RUNTIME.md)。
- **窗口拖动重定位（PASS）**：真实拖动 Codex 窗口记录 4 组不同坐标与 `overlay_hidden_reason=window_moved` 隐藏/重定位，旁注框记录 `reason=user_drag`。详见 [PHASE-6A-DESKTOP.md](PHASE-6A-DESKTOP.md)。
- **仍保留说明的桌面细项**：多行代码块选区（本轮冷启动日志为 `rectangle_count=1`，标记 `PARTIAL`）、永久删除与严格 UI 三浮窗同时并发重叠（标记 `NOT TESTED`）。

### 5. 最终 CLEAN 构建、回归与安全验证（基于提交 `6aceb0e`）

- 载荷：`artifacts\payload-4a22812f03ab4fcd85eb4015bf10d400`（`source_commit=6aceb0e...`，`source_dirty=false`，`signature=NotSigned`，`formal_release_ready=false`）。

| 发布物（`artifacts\`） | 字节 | SHA-256 |
| --- | ---: | --- |
| `ContextNotesNative-0.3.0-setup.exe` | 2,289,879 | `8DDB13D4171A3946C26CC628945E404049CAE7F973F4B4D8E3F1547918C7FE86` |
| `ContextNotesNative-0.3.0-portable.zip` | 240,539 | `A942C7E761A4B67D59A4BEBF009854A78DFC1652E1FAEC31FF2C6AF29C74BC41` |

- 回归总览：
  - `dotnet build -c Release`：**0 警告、0 错误**
  - `dotnet test`：**106 / 106 PASS**（失败 0、跳过 0）
  - `--self-test`：**80 / 80 PASS**
  - `Test-StandardInstaller.ps1`：**29 / 29 PASS**
  - `Test-Installation.ps1`：**19 / 19 PASS**
  - `Test-Release.ps1`：**12 / 12 PASS**
  - `Test-HookDiagnostic.ps1`：**9 / 9 PASS**
  - `Test-InstallationPermissions.ps1`：**6 / 6 PASS**（`effective_admin=False`）
  - PowerShell 静态解析：**11 / 11 脚本 0 错误**
  - 数据库备份与恢复往返（临时副本）：**PASS**（`integrity=ok`, schema 2）
  - `gitleaks` 真实扫描（中完整性副本）：全仓库 **45 个提交（905.17 KB）+ 最终载荷（56.46 KB）= 0 泄露**
  - 可信代码签名：**BLOCKED**（本机无可用证书）；第三方许可、OpenAI 商标边界与隐私声明草案已审校并保留维护者确认门禁。
