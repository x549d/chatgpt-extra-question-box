# Phase 7 接续检查点

2026-11-19。项目 `<repo-root>`，main，初始无远端。用户现已授权 GitHub 私有源码仓库 `https://github.com/x549d/context-notes-native`，仓库已创建；最终推送与远端 SHA 对应关系在交付时核对。基线 341dd6e；最新详细状态见 [Phase 7 验收报告](PHASE-7-ACCEPTANCE.md)。

已完成：独立合成库永久删除 23/23；技术 PRIVACY 云处理及旧备份范围；Proprietary/第三方/品牌审查与维护者决定清单；证书与签名状态只读复核。生产实现未改动。禁止重新采样 30/60 分钟或重跑已通过 Phase 6 安装器专项。

生产库仍在 `%USERPROFILE%\.context-notes-native\data\history.db`。用户三条新请求后为 **19/110**、schema 2、完整性 ok、外键错误 0；正常重启后相对重启前在线备份的全部消息与旁注身份差异 0，原 6/26 及冷启动前 13/102 已有零差异证据。不要覆盖、清空、迁移或永久删除真实历史。合成删除库仅在 `artifacts\phase7-deletion-20261119-1625`。

## 继续顺序

1. 云 Windows：用户选择新建、平台由代理选、总预算 20 元；阿里云已登录。ECS 300 元试用不能假定抵扣无影。无影 Windows 11 Pro 按需方案 1.11786 元/时，BYOL 许可证资格待确认，用户明确先保留。未创建任何云资源、费用 0；不要自动下单、接受新条款或扩大权限。不得用 Windows Server / 无影个人版冒充目标 Codex 桌面。许可与实时总价确认后才继续独立 OS 的全新安装、升级、卸载及 AI/历史流程。
2. 严格 UI 并发已 **PASS**：用户完成三个新浮窗的并发、焦点/关闭/移动操作并回复成功；日志确认三个 Thread 实际重叠 14.49 秒。代理已正常重启 Companion 并从历史逐个恢复三条，数据库全量比较无差异，见 [记录](PHASE-7C-UI.md)。不要反复要求用户重做。Watcher 仍 10716，新 Companion 27176；最后 C 恢复浮窗保留。
3. 维护者一次性提供权属主体/年份、私有/公众 Proprietary 许可文本、公开发布者法律名称、隐私联系邮箱/网址、公开名称/品牌决定；详见 [决定清单](PHASE-7B-MAINTAINER-DECISIONS.md)。未提供前保持占位与 BLOCKED。
4. 可信签名：可用代码签名证书 0，不购买、不自签冒充。将来由维护者提供适用签名方案后依 [计划](SIGNING-PLAN.md) 签 EXE/DLL、时间戳、重建哈希并做相应回归。

本地 VMware 完整克隆保留在 `artifacts\phase7-vm-isolated-gui`，原 VM 未启动或删除；结束 `vmrun list` 为 0。没有启用管理员功能、创建系统账户或修改系统安全配置。

已从 CLEAN `586953808e57af4a663f27dd6eafa145e3abf571` 生成最终未签名 Preview，载荷 `artifacts\payload-4993670973654d0e8fd1485a9661d26b`。新包发布审计 12/12、9 个文档相对链接无断链、载荷泄露扫描 0；SHA256SUMS 均匹配。正常用户交付目录为 `%USERPROFILE%\Downloads\ContextNotesNative-0.3.0-Phase7-20261119`，最终哈希详见验收报告。包不含验收工程、测试数据库、日志、认证文件或源代码缓存。没有部署新运行库，当前后台保持原 Phase 6 已验收版本。

原 Phase 6 顶层 EXE/ZIP/哈希保留在 `artifacts\phase6-preserved-before-phase7-20261119`；不要把新的 source_commit 与生产后台版本混为一谈。随包用户文档不包含仓库内部验收报告，因此打包之后的结果记录提交无须再次打包。最终 HEAD 由本报告与验收报告的最终审计提交确定，交付回复中给出精确哈希。仅按用户新授权推送指定 GitHub 私有源码仓库，不建 Tag、不创建 Release、不公开仓库、不上传用户库/日志/备份。

用户已完成 UI 操作，并选择仅 GitHub 私有源码交付；无需等待公开发布主体信息才推送该指定私有仓库。公众许可/发布者/隐私联系/品牌决定仍未提供，保持占位。云许可证不清楚的明确答复已记录为待确认，不反复询问或自行采购。
