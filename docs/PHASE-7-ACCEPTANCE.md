# Phase 7 验收报告 — 0.3.0 Release Candidate

2026-11-19。当前结论：本机内部使用沿用 Phase 6 PASS；公开预览及正式稳定版仍 BLOCKED。生产实现未改动，没有重复 30/60 分钟采样或 Phase 6 安装器测试。详细证据分别见 [环境](PHASE-7A-ENVIRONMENT.md)、[真实浮窗](PHASE-7C-UI.md)、[隔离删除](PHASE-7C-DELETION.md) 和 [维护者决定](PHASE-7B-MAINTAINER-DECISIONS.md)。

## A. Git 与变更范围

- 初始 HEAD `341dd6e`，分支 main，初始工作区 CLEAN，无远端。
- 新提交：`6cd8b0f`（隔离删除验收工程与结果）、`b4142ce`（技术隐私和维护者决定）、`ff63d31`（独立环境与云许可边界）、`f23b2e5`（真实浮窗证据）。其后发布文档及审计提交见 `git log 341dd6e..HEAD --oneline`。
- 最终 HEAD 为包含本报告最终审计结果的提交，可用 `git log -1 --format=%H -- docs/PHASE-7-ACCEPTANCE.md` 精确取得；避免在提交自身内嵌无法自洽的自身哈希。最终交付回复另记录实际 HEAD。
- 未修改 src、installer、scripts 或 codex-plugin；仅新增使用已发布实现的独立合成库验收项目及文档。没有新增依赖，没有改动 OAuth、UIA、数据模型或生产部署。
- 没有 push、Tag 或 GitHub Release。最终 diff/status 和新发布物核对结果在本报告末尾记录。

## B. 干净 Windows 测试

**BLOCKED - CLEAN WINDOWS ENVIRONMENT UNAVAILABLE**。本机 Windows 11 Home 10.0.26100；发现常规路径之外的 VMware 17.6.3，建立独立 Full Clone 并到达 Windows 锁屏，未登录来宾，未在其中安装产品。用户改选云 Windows 后停止本地测试，结束时运行 VM 数 0。

用户已登录阿里云，预算最多 20 元。已有试用是 ECS 个人版 300 元额度，不等同无影抵扣。无影企业版 Windows 11 Pro 按需配置实际报价 1.11786 元/时，但采用 BYOL；用户对许可证回复“不清楚，先保留此项待确认”。未下单、未创建资源、费用 0 元。安装、升级、卸载、默认保留历史及真实 Codex/AI 全流程均 **NOT TESTED**，详见环境矩阵。

## C. 多旁注并发

**NOT TESTED（严格三浮窗并发）**。本轮恢复两个合成浮窗，引用和既有回答可见；控制接口不能可靠选择浮窗输入框，操作会激活历史根窗口，未发送请求。同时 AI 请求数为 **0**。因此串流、焦点冲突、独立关闭和移动重定位在并发条件下均无完成结论；有限恢复操作未观察到崩溃。Phase 6 的三个独立 Thread、多轮、服务层并发和冷启动通过结果不用于替代此项。

2026-11-19 16:51 只读生产库：schema 2、**16 旁注 / 104 消息**、integrity_check=ok、foreign_key_check 错误 0。相对原始 6/26 和冷启动前 13/102 两个备份，消息逐字段及旁注身份均无缺失/改变。没有对生产库执行永久删除、覆盖或迁移。

## D. 永久删除隔离专项

**PASS：23 断言，失败 0，跳过 0**。使用 CLEAN 源码 6aceb0e 的真实发布 DLL，SHA-256 `5837B767F51AE562714E8653A2DDEA3BFB8A58CE62F2A5901D5D5338734ECA59`。测试输出 `artifacts\phase7-deletion-20261119-1625`；独立新建合成数据库，结果 JSON 和数据库均不进入 Git 或发行载荷。

删除前 5 旁注 / 16 消息，删除目标账号两条回收站记录后 **3 / 10**。级联消息残留 0，其他账号、活动/归档旁注及设置逐字段不变。第二条删除触发错误时事务完整回滚；WAL/SHM、长读快照、TRUNCATE 检查点、完整性与外键检查通过。新连接仍 3/10，未复活。删除后备份恢复仍 3/10；显式恢复删除前备份则为 5/16，已在隐私说明中写明旧副本边界。该断言数不累加到已有 106 项 xUnit。

## E. LICENSE 与隐私

LICENSE 维持 **Proprietary**，未擅自授予分发权或声明代码权属。PRIVACY.md 已说明本地库、日志、备份、凭据边界，以及选区、问题和恢复上下文通过本机 App Server 进入 AI 服务处理流程；不承诺完全离线、零保留或不训练。

发布者法律名称和隐私联系字段仍为明确占位。代码权属/版权年份、私有或公众分发许可文本、公开产品名/品牌边界均待维护者批准。技术说明已完成，法律与公开发布身份决定 **BLOCKED**。

## F. 数字签名

重新检查 CurrentUser/My 与 LocalMachine/My，有效期内、有私钥的 Code Signing 证书 **0**。现有 setup.exe、SelectionProbe.exe、SelectionProbe.dll 均 **NotSigned**，没有签名者或 RFC 3161 时间戳；实际可信签名能力 NOT AVAILABLE / NOT TESTED。

[签名计划](SIGNING-PLAN.md) 已记录证书主体和资格、EXE/DLL 范围、先签载荷再生成哈希、签安装器、时间戳和验证顺序。未购买证书、未生成冒充可信签名的自签证书、未绕过安全警告。20 元云环境预算不用于签名采购。

## G. 发布判定

| 目标 | 结果 | 尚需完成 |
| --- | --- | --- |
| 当前用户本机内部使用 | PASS（沿用 Phase 6 已验证范围） | 严格三浮窗 UI 并发未包含在此判定中 |
| 私有预览分发 | BLOCKED | 维护者权属/分发许可及发布者信息 |
| 公开预览分发 | BLOCKED | 独立 Windows 全流程、严格 UI 并发、维护者许可/隐私/品牌决定 |
| 正式稳定版 | BLOCKED | 上述项目及可信签名、签名后必要回归 |

Phase 6 已通过的 106 xUnit、80 自测、29 安装器、19 便携管理、12 发布审计、9 Hook、6 权限探针和 30.005 分钟/61 样本稳定性证据继续有效，属于既有结果，**本轮未重跑**。60 分钟保持 OPTIONAL / NOT REQUIRED / NOT RUN。

## 本轮发布物核对

技术文档已更新，计划在提交为 CLEAN 后生成新的未签名 Preview 载荷；构建来源、产物哈希、审计数量和最终工作区结果待生成后追加。旧 Phase 6 产物与证据继续保留，不把新打包等同干净 OS 或 UI 并发验收通过。
