# Phase 5 交付与验收报告

日期：2026-11-18（Asia/Shanghai）。基线 `24dde22`，代码交付提交 `4adcb61`。最终文档提交为本报告所在的 `docs: record phase 5 delivery and acceptance limits` 提交；完整最终哈希见 `git log -1` 及本次交付回复。未 push，未创建 Tag。

## 1. 项目状态

Phase 5A～5F 的实现、测试和分阶段提交已交付。**不能宣布全部正式产品化验收通过**：标准安装器在当前托管环境遇到注册表权限拒绝；缺少干净 Windows 环境；最后一轮桌面操作被用户通过物理 Esc 停止。

| 阶段 | 实现与自动化 | 真实验收状态 |
| --- | --- | --- |
| 5A 持久化 | 已实现；9 项数据库测试通过 | 三旁注各三轮真实调用、跨进程保留原 Thread 再续问通过；完整 Codex/Windows 重启未测 |
| 5B 历史管理 | 已实现；7 项搜索、筛选、归档、回收站与隔离测试通过 | 真实历史窗口列出三条记录并恢复原旁注小框；归档/删除完整桌面操作未测 |
| 5C 消息展示 | 已实现；9 项 Markdown / WPF 测试通过 | 原旁注消息与复制入口已观察；真实长代码、流式 Markdown 与复制操作全流程未测 |
| 5D 模型/用量 | 已实现；9 项模型、用量、恢复协议测试通过 | 实际 10 个模型、账号与限额窗口通过；实际账号切换和真实请求切换模型未测 |
| 5E 性能/稳定性 | 已实现；最终 12 项专项通过 | 进程崩溃、锁、空间不足注入、重复并发与真实 App Server 跨进程恢复通过；长时间浸泡及睡眠/唤醒未测 |
| 5F 安装/发布准备 | 安装器、便携包、管理器和文档已交付；13 项隔离配置安装断言通过 | EXE 编译通过，实际执行因 HKCU 卸载键拒绝而回滚；干净环境安装/升级/卸载未测；正式发布未就绪 |

0.3.0 已通过管理器部署至当前用户目录，并由官方 CLI 注册插件。最后状态是 **文件和注册已完成，后台未运行**：迁移部署时旧版 Companion / Watcher 已正常停止；用户 Esc 停止电脑操作后，未继续启动新版或执行桌面输入。不能把这个状态写成“全部部署且实机验收 PASS”。

## 2. 数据库交付

生产路径：`%USERPROFILE%\AppData\Local\ContextNotesNative\data\history.db`，自动备份 `history.db.backup.db`。生产库只位于当前 Windows 用户目录；测试库仅为隔离临时/工作区合成数据。

| 表 | 关键字段 |
| --- | --- |
| annotations | id 主键、account_scope、thread_id、selected_text、created_at、updated_at、payload（草稿/提交草稿/位置/折叠/引用折叠/模型/生成状态）、title、archived、deleted |
| messages | id 主键、annotation_id 外键、role、content、created_at、turn_id、completion_status、committed、sequence；同旁注 sequence 唯一 |
| settings | key 主键、value；默认模型按账号作用域分键 |

Schema **2**；事务执行 v1→v2，迁移前一致性备份为 `history.db.pre-v2.db`。未来版本拒绝打开，完整性失败保留原文件。Windows inbox SQLite、参数绑定、外键、WAL、synchronous FULL、串行事务和有界锁等待；关键状态立即保存，草稿/流式快照 750ms 合并保存。异常写入保留待写队列，显示未保存提示并重试；关键输入无法落盘时阻止新请求。

正常退出执行 SQLite 在线备份；历史窗口可导出备份。测试验证备份重新打开后消息、设置完整；强制杀死持有开放事务的子进程后，已提交输入保留，未提交修改回滚；SQLITE_FULL 注入后无半条记录，恢复空间后可再次写入。安装失败可恢复迁移前 schema 与数据，13 项安装断言中已实际验证。

最后只读核验：`quick_check=ok`、schema=2、3 条旁注、24 条消息、3 个唯一 Thread；自动备份存在（49,152 字节）。没有保存 OAuth/Access/Refresh Token、配对码或浏览器 Cookie。数据库不提供应用层加密，备份和 Codex 自身 session 存储须单独管理。

## 3. 功能交付

- 历史：创建、草稿、发送、流式检查点、完成/取消/失败、移动、折叠、关闭与正常退出保存；UUID 标识；中断回答恢复为 Failed / uncommitted；恢复不自动重发。
- Thread：新建 durable Thread，恢复时先核对账号再调用官方 `thread/resume`；失效时明确要求确认新 Thread，以引用和已完成记录重建上下文。
- 管理：Ctrl+Alt+H / 旁注 / 入口右键 / `--show-history`；账号、日期、归档筛选，300ms 搜索防抖和后台查询，50 条分页，标题修改、归档、软删除、恢复和确认清空。延迟保存不能复活已永久删除旁注。
- 展示：标题、粗体、斜体、列表、引用、表格、行内/围栏代码和 HTTP(S) 链接；不完整 Markdown 安全显示；代码保留缩进与换行、语言标识、横向滚动、原样复制和长代码折叠；长消息展开。未虚构重新生成或单消息服务端删除。
- 模型：真实分页目录、按账号保存默认、单旁注选择、不可用模型明确失败；用量仅展示官方返回字段。真实账号变化会阻止旧 Thread 误用，旧历史保留。
- 稳定性：完成消息 UI 复用、Dispatcher 刷新合并、迟到流式回调与终态串行、增量数据库更新、有界取消标记、关闭后回调隔离、连接初始化失败释放与幂等清理。
- 安装：按用户、版本目录、官方插件 CLI、逐文件哈希、Hook 清单验证、自有启动快捷方式、升级迁移/失败回滚和默认保留历史。标准 EXE / 卸载链尚未在允许注册表写入的干净 Windows 环境完成全流程。

## 4. 实际测试结果

| 项目 | 发现 / 通过 / 失败 / 跳过 |
| --- | --- |
| 原有项目自测 | **80 / 80 / 0 / 0** |
| 原有 xUnit | **39 / 39 / 0 / 0**，未删除既有用例 |
| 新增数据库 | **9 / 9 / 0 / 0** |
| 新增历史管理 | **7 / 7 / 0 / 0** |
| 新增 Markdown | **9 / 9 / 0 / 0** |
| 新增模型/用量/Thread 恢复 | **9 / 9 / 0 / 0** |
| 新增性能/稳定性 | **12 / 12 / 0 / 0** |
| 最终 xUnit 合计 | **85 / 85 / 0 / 0**，Release 和默认 `dotnet test` 均通过 |
| 安装独立脚本断言 | **13 / 13 / 0 / 0**；独立统计，不冒充 xUnit 或干净机器测试 |
| Release 构建 | **PASS：0 warnings，0 errors** |
| PowerShell 脚本解析 | **PASS** |
| 真实 AI 多轮与恢复 | **PASS**：3×3 轮 + 跨进程每条再追问 1 轮，共 24 消息，原 Thread ID 全部保持 |
| 真实模型/账号/限额读取 | **PASS**：模型 10 个，仅使用真实响应 |
| 5A～5E 热部署 | **PASS（此前版本）**：四个运行文件 SHA-256 匹配，WinSta0\Default Watcher 拉起，单实例已观察 |
| 最终 0.3.0 文件部署/注册 | **PASS**：安装目录与缓存逐文件哈希不匹配数 0 |
| 最终运行态和完整桌面验收 | **NOT COMPLETE**：用户停止电脑操作；Companion / Watcher 为 not_running，旧 Companion 子进程残留 0 |
| 标准 EXE 实际安装 | **BLOCKED**：退出码 4，HKCU Uninstall 的 RegCreateKeyEx code 5；安装器回滚了提取文件 |
| 干净 Windows 安装 | **NOT TESTED**，没有可用的干净虚拟环境 |
| 凭证扫描 | **PASS**：每阶段暂存差异 gitleaks 扫描；最终新增提交范围再次扫描 |
| Git 工作区 | 最终报告提交后核验 CLEAN |

20 旁注 / 1000 消息的首轮写入、更新、搜索、备份和重开耗时 210ms；测试进程 CPU 增量 406ms，Private Bytes 增量约 12.1MB。测量来自共享 xUnit 进程，不是独立 Companion 的长期内存曲线。1/5/10 WPF 旁注各刷新 50 次并清理；10 路并发生成/取消循环 5 次。最初 11 项稳定性用例完整运行后又单独重复两次；增加未确认终态用例后最终 12 项全部通过。这些证据不等于长时间运行、断电或睡眠认证。

可复现记录：`<local-temp>\context-notes-phase5-results\phase5-final.trx`、`phase5e.trx`、`phase5e-repeat-1.trx`、`phase5e-repeat-2.trx`；合成安装测试目录位于未入库的 artifacts。普通 EXE 受阻日志：`artifacts\setup-smoke\setup-fixed.log`。

## 5. 产物与 Git

| 产物 | 位置 / SHA-256 |
| --- | --- |
| 安装器 | `artifacts\ContextNotesNative-0.3.0-setup.exe`；`9B40E2324D373DD1A745050B9D4A803CE1245EB8D05264D99742BF3BE6999B93` |
| 便携包 | `artifacts\ContextNotesNative-0.3.0-portable.zip`；`23BAD0DF64EAB131DA8762536FF6998B399E07A49BD2011F1D865AC275FCD9F7` |
| 稳定本机程序目录 | `%LOCALAPPDATA%\Programs\ContextNotesNative\versions\0.3.0` |
| 当前插件缓存 | `%USERPROFILE%\.codex\plugins\cache\context-notes-native\context-notes\0.3.0` |

EXE 未签名，属于预览交付。可在桌面操作重新获准后从新版本 `bin\SelectionProbe.exe` 启动（默认同时启动自有 Watcher），再用 Ctrl+Alt+H 打开历史。此报告不自动启动程序。

| 阶段 | Commit |
| --- | --- |
| 基线 | `24dde22` |
| 5A | `5bf33cd` |
| 5B | `4ed6d10` |
| 5C | `2c44636` |
| 5D | `d252484` |
| 5E | `1be715a` |
| 5F | `4adcb61` |
| 最终文档 | 本报告所在提交，完整哈希见最终回复 |

相对基线共修改/新增 **39 个文件**（含本报告）。保留原有历史，无 reset --hard、无 push、无正式 Tag；没有提交 bin、obj、安装产物、测试数据库、用户历史或凭据。

## 6. 未完成、受阻和已知限制

1. **用户停止桌面操作**：物理 Esc 后不再调用 Computer Use；新版尚未启动，未继续重启 Codex、恢复三个窗口或做完整交互验收。
2. **标准安装受阻**：当前托管环境拒绝 HKCU 卸载键写入。便携编译器和独立部署管理器可用，没有修改注册表权限、系统安全设置或绕过 Codex Hook 信任。干净环境安装、首次运行、覆盖升级、标准卸载待验收。
3. **正式发布未就绪**：没有可信代码签名；LICENSE 只如实保留 manifest 的 Proprietary 状态，未获新的许可证授权；隐私文本缺少发布者法律身份和联系信息，明确标为草案。
4. **长期和环境验证未测**：Windows 睡眠/唤醒、实际 Windows/Codex 意外退出、长时间 CPU/内存/Dispatcher 曲线、真实多账号切换、真实请求模型切换，以及全部 Markdown/IME/拖动/复制的最终版桌面回归。
5. **数据边界**：SQLite 和备份未加密；软删除、永久删除记录不会清理已有外部备份、SQLite 空闲页和官方 App Server 自己的 durable session 文件。原 Thread 不可用的新上下文恢复与真正 resume 有明确区别。

上述项目均未伪装为 PASS，也没有用模拟账号/模型数据冒充实际协议能力。
