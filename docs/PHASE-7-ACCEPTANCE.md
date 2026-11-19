# Phase 7 验收报告 — 0.3.0 Release Candidate

2026-11-19。当前结论：本机内部使用沿用 Phase 6 PASS；公开预览及正式稳定版仍 BLOCKED。生产实现未改动，没有重复 30/60 分钟采样或 Phase 6 安装器测试。详细证据分别见 [环境](PHASE-7A-ENVIRONMENT.md)、[真实浮窗](PHASE-7C-UI.md)、[隔离删除](PHASE-7C-DELETION.md) 和 [维护者决定](PHASE-7B-MAINTAINER-DECISIONS.md)。

## A. Git 与变更范围

- 初始 HEAD `341dd6e`，分支 main，初始工作区 CLEAN，无远端。
- 新提交：`6cd8b0f`（隔离删除验收工程与结果）、`b4142ce`（技术隐私和维护者决定）、`ff63d31`（独立环境与云许可边界）、`f23b2e5`（真实浮窗证据）、`2c0e60e`（发布材料与阶段报告）、`5869538`（随包安全文档链接修正），以及包含本报告最终审计结果的提交；完整列表见 `git log 341dd6e..HEAD --oneline`。
- 最终 HEAD 为包含本报告最终审计结果的提交，可用 `git log -1 --format=%H -- docs/PHASE-7-ACCEPTANCE.md` 精确取得；避免在提交自身内嵌无法自洽的自身哈希。最终交付回复另记录实际 HEAD。
- 未修改 src、installer、scripts 或 codex-plugin；仅新增使用已发布实现的独立合成库验收项目及文档。没有新增依赖，没有改动 OAuth、UIA、数据模型或生产部署。
- 用户后续明确选择仅 GitHub 交付，并批准新建私有仓库 `x549d/context-notes-native`；该私有仓库已创建。最终提交可推送至此指定仓库，远端 SHA 与 CLEAN 状态在交付前核对；这不包含公开仓库、Tag 或 GitHub Release 授权。

## B. 干净 Windows 测试

**BLOCKED - CLEAN WINDOWS ENVIRONMENT UNAVAILABLE**。本机 Windows 11 Home 10.0.26100；发现常规路径之外的 VMware 17.6.3，建立独立 Full Clone 并到达 Windows 锁屏，未登录来宾，未在其中安装产品。用户改选云 Windows 后停止本地测试，结束时运行 VM 数 0。

用户已登录阿里云，预算最多 20 元。已有试用是 ECS 个人版 300 元额度，不等同无影抵扣。无影企业版 Windows 11 Pro 按需配置实际报价 1.11786 元/时，但采用 BYOL；用户对许可证回复“不清楚，先保留此项待确认”。未下单、未创建资源、费用 0 元。安装、升级、卸载、默认保留历史及真实 Codex/AI 全流程均 **NOT TESTED**，详见环境矩阵。

## C. 多旁注并发

**PASS（用户实际操作 + 日志/数据库核对 + 代理重启恢复）**。用户在三个新浮窗发送 Phase7-A/B/C 50 行问题，操作了并发流式、输入/焦点、关闭一个及移动主窗口，回复“成功”。日志核实三条新请求在约 1.02 秒内启动，各自不同 AnnotationId / ThreadId、同一账号作用域，执行重叠 **14.490 秒**；回答各 50 行、分别 committed=1、GenerationState=Completed，内容哈希不同。无串流、焦点冲突及独立关闭/移动异常的 UI 结论来自用户反馈，不伪称代理自动控制了浮窗输入。

新问题正常新增 3 旁注 / 6 消息，生产库为 schema 2、**19 / 110**。在线备份后正常停止旧 Companion 29208，原 Watcher 10716 拉起新 Companion 27176，旧 App Server 27300 随旧 Companion 清理。重启后全部消息和旁注身份与备份比较差异 **0**，integrity_check=ok、外键错误 0；代理实际从历史界面逐一打开 A/B/C，引用和回答均可恢复，未重发请求。WindowPosition 有效；CurrentAnchorRect 由当前宿主重建而非持久化旧值。此前原始 6/26 与冷启动前 13/102 的比较亦为 0 差异，没有永久删除、覆盖或迁移生产库。

## D. 永久删除隔离专项

**PASS：23 断言，失败 0，跳过 0**。使用 CLEAN 源码 6aceb0e 的真实发布 DLL，SHA-256 `5837B767F51AE562714E8653A2DDEA3BFB8A58CE62F2A5901D5D5338734ECA59`。测试输出 `artifacts\phase7-deletion-20261119-1625`；独立新建合成数据库，结果 JSON 和数据库均不进入 Git 或发行载荷。

删除前 5 旁注 / 16 消息，删除目标账号两条回收站记录后 **3 / 10**。级联消息残留 0，其他账号、活动/归档旁注及设置逐字段不变。第二条删除触发错误时事务完整回滚；WAL/SHM、长读快照、TRUNCATE 检查点、完整性与外键检查通过。新连接仍 3/10，未复活。删除后备份恢复仍 3/10；显式恢复删除前备份则为 5/16，已在隐私说明中写明旧副本边界。该断言数不累加到已有 106 项 xUnit。

## E. LICENSE 与隐私

LICENSE 维持 **Proprietary**，未擅自授予分发权或声明代码权属。PRIVACY.md 已说明本地库、日志、备份、凭据边界，以及选区、问题和恢复上下文通过本机 App Server 进入 AI 服务处理流程；不承诺完全离线、零保留或不训练。

发布者法律名称和隐私联系字段仍为明确占位。代码权属/版权年份、私有或公众分发许可文本、公开产品名/品牌边界均待维护者批准。技术说明已完成，法律与公开发布身份决定 **BLOCKED**。

用户回复“仅发布到github”，随后明确选择新建 **私有** `x549d/context-notes-native`。这是本项目源码向其指定私有仓库同步的授权，不将其解释为公众许可、签名采购或法律身份声明。LICENSE 继续不变，不因该授权阻止已明确允许的私有源码推送。

## F. 数字签名

重新检查 CurrentUser/My 与 LocalMachine/My，有效期内、有私钥的 Code Signing 证书 **0**。现有 setup.exe、SelectionProbe.exe、SelectionProbe.dll 均 **NotSigned**，没有签名者或 RFC 3161 时间戳；实际可信签名能力 NOT AVAILABLE / NOT TESTED。

[签名计划](SIGNING-PLAN.md) 已记录证书主体和资格、EXE/DLL 范围、先签载荷再生成哈希、签安装器、时间戳和验证顺序。未购买证书、未生成冒充可信签名的自签证书、未绕过安全警告。20 元云环境预算不用于签名采购。

## G. 发布判定

| 目标 | 结果 | 尚需完成 |
| --- | --- | --- |
| 当前用户本机内部使用 | PASS（Phase 6 + 本轮 UI/删除专项） | 不等同干净 OS 验收 |
| 指定 GitHub 私有源码仓库 | 已授权，创建完成 | 最终提交推送并核对远端 SHA；交付回复记录实际结果 |
| 私有预览分发 | BLOCKED | 维护者权属/分发许可及发布者信息 |
| 公开预览分发 | BLOCKED | 独立 Windows 全流程、维护者许可/隐私/品牌决定 |
| 正式稳定版 | BLOCKED | 上述项目及可信签名、签名后必要回归 |

Phase 6 已通过的 106 xUnit、80 自测、29 安装器、19 便携管理、12 发布审计、9 Hook、6 权限探针和 30.005 分钟/61 样本稳定性证据继续有效，属于既有结果，**本轮未重跑**。60 分钟保持 OPTIONAL / NOT REQUIRED / NOT RUN。

## 本轮发布物核对

已从 **CLEAN 提交 `586953808e57af4a663f27dd6eafa145e3abf571`** 生成最终未签名 Preview。release.json 为 source_dirty=false、channel=preview、formal_release_ready=false、signature=NotSigned。之后仅追加仓库验收报告，没有改变随包用户文档或生产实现；最终 Git HEAD 与打包源码 HEAD 因结果记录提交而不同。

- 最终载荷：`artifacts\payload-4993670973654d0e8fd1485a9661d26b`。
- 便于本机运行的正常用户交付目录：`%USERPROFILE%\Downloads\ContextNotesNative-0.3.0-Phase7-20261119`。含 EXE、ZIP、SHA256SUMS、逐物哈希、RELEASE-README.md 和 CHANGELOG.md；与仓库 artifacts 下最终文件哈希相同。直接运行工作区低完整性目录的限制仍适用，未修改 ACL。
- EXE **2,291,479 字节**；SHA-256 `FF7F73C8CCCADF4896BACC34B220177106900C4A31BCFF6014EFB9C66F1EBE42`。
- ZIP **247,726 字节**；SHA-256 `CD288229FDBE6254D116D561F9D8219C90BC920999564FAB87104FAD5C79F973`。
- 新包发布审计 **12/12**；ZIP 所有载荷逐文件匹配 SHA256.json 与 staging，setup 从同一个 staging 的递归 Files 规则编译成功。本轮未运行或解包安装器来声称新环境安装通过。
- 无测试数据库、聊天历史、日志、认证文件、源码/测试工程、obj 或 PDB；运行所需插件 bin 目录属于必要载荷。随包 Markdown 相对文档链接核对 **9 个，断链 0**。
- 最终载荷脱敏 gitleaks 扫描 **0 泄露**；各阶段提交前差异扫描均为 0。原 Phase 6 EXE/ZIP/SHA 已保留在 `artifacts\phase6-preserved-before-phase7-20261119`，旧 payload 及稳定性证据未覆盖。
- 使用已有官方 Inno 6.7.4 编译器的相同哈希副本，未安装新依赖；未启动新安装器、未部署新运行库或重启生产后台。构建日志：`<local-temp>\context-notes-phase6-results\phase7-release-final-build.txt`。

最终保存门禁为 diff --check 通过、提交完成后 status --short 为空（CLEAN）；交付回复记录实际 HEAD 及指定私有仓库的远端同步结果。用户已补齐严格 UI 并发操作并授权私有源码同步；独立 OS、公众法律身份和签名阻断保持，云许可证按其明确选择保留待确认。
