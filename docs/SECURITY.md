# 安全与数据处理

Phase 8 开源发布更新（2026-11-19）：
- 本项目已完成代码权属与第三方许可审查（生产工程 0 个 NuGet 依赖，0 个外部图标/字体资源，12 个仅测试期包为 MIT/Apache-2.0 且不随包分发）及完整敏感数据安全审查（`gitleaks 8.30.1` 与全量 Blob/二进制深度扫描确认高危凭据、API Key、Token、Cookie、私钥、SQLite 数据库与用户聊天历史均为 **0 泄露**）。
- 本项目自有代码已正式采用 **MIT License**（见 [LICENSE](../LICENSE)）；第三方组件与 OpenAI 商标边界见 [第三方许可清单](THIRD-PARTY-NOTICES.md)；发布者与公开隐私联系信息已更新于 [隐私说明](../PRIVACY.md)。
- 0.3.0 公开包目前为**未签名（`NotSigned`）**，请使用 `SHA256SUMS` 校验完整性；签名状态与后续方案见 [SIGNING-PLAN.md](SIGNING-PLAN.md)。

Phase 7 技术审查（2026-11-19）：本机 App Server 是 AI 服务桥接进程，选区、提问和恢复上下文会进入对应云端处理流程，read-only 工具策略不阻断推理请求。独立合成数据库的永久删除专项 23/23 通过，覆盖事务回滚、账号隔离、消息级联、WAL 快照和重开连接；删除前的旧备份仍可恢复旧记录，因此永久删除不表示物理擦除所有副本。真实用户数据库未参与破坏性测试。数据处理范围见随包提供的 [隐私说明](../PRIVACY.md)；独立验收记录保存在源码仓库中，不随运行包分发。

Phase 6 审计结论：
- 安装器 Temp/HKCU 拒绝访问已定位为沙箱工作区目录继承的 Windows `Low Mandatory Level` SACL，在正常中完整性目录与隔离 `/TESTROOT` 下通过 **29/29** 标准 EXE 测试（无需管理员权限、不修改 ACL、不写 HKLM）；全新安装失败自动回滚并返回非零退出码。
- 源码静态审计确认生产工程无 `HttpClient`/`WebRequest`/`Socket`/遥测/Cookie/Token/认证文件读取逻辑，0 个生产 NuGet 包，0 个外部图标/字体资源。

数据与进程安全边界：
- 升级备份和恢复使用 SQLite Backup API，源库在迁移前备份，包含已提交 WAL；回滚失败保留当前用户 backups 目录中的恢复副本。受控环境权限拒绝不通过提权或修改 ACL 绕过。发行 ZIP 仅包含运行载荷和用户文档，逐文件哈希、发布物 `SHA256SUMS` 与源码提交记录由独立审计脚本核对。
- 应用通过官方 App Server stdio 协议复用官方登录状态。没有 Cookie 读取、内存 Token 提取、OAuth 绕过、配对码收集或认证凭据落库逻辑。App Server 位于空工作目录，使用 read-only sandbox、禁止审批的策略与现有工具请求拒绝逻辑；子进程由 Win32 Job Object 管理。
- SQLite 参数绑定避免搜索字符串拼接为 SQL；外键、唯一键、事务、WAL、FULL 同步、版本与完整性检查保护数据一致性。数据库没有应用层加密。备份同样含个人对话，默认仅在当前 Windows 用户目录。磁盘内容的物理擦除、操作系统备份和 Codex 自身 session 文件不由本项目的软删除控制。
- Markdown 不执行 HTML、脚本或图片请求。仅允许用户点击 HTTP(S) 链接，复制使用原始文本。日志不得包含完整问答或底层异常中可能携带的凭据。
- 安装仅调用官方插件 CLI，仅移除本插件的 marketplace、缓存和自有快捷方式；历史默认保留。
