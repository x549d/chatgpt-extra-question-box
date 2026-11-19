# Context Notes Native 0.3.0 Preview

Windows 10/11 x64 的 Codex Chat 划词追问工具。需要 .NET 10 Windows Desktop Runtime x64 和 Codex Desktop。

选中助手回答并点「提问」，在原文旁输入问题；Enter 发送、Shift+Enter 换行。Ctrl+Alt+H 打开本地历史，可搜索、归档和恢复。账号和模型来自官方 App Server，不收集登录凭据。

这是未签名的内部预览 / Release Candidate 交付，**不属于 OpenAI 官方认证或授权产品**。已完成三旁注多轮、最终构建完整客户端冷启动、窗口移动重定位、30.005 分钟连续稳定性验收（61 个样本），以及正常中完整性目录下的标准 EXE 隔离安装/升级/回滚/卸载（29/29）。Phase 7 已通过独立合成库的永久删除专项（23/23）。独立干净 Windows OS、严格三个浮窗同时流式请求、可信代码签名、维护者确认的分发许可、发布者隐私联系主体及公开品牌决定尚未完成，**暂不可宣称公众预览或正式稳定版公开发布通过**。

引用原文、问题及恢复对话所需的上下文会通过本机 Codex App Server 进入对应 AI 服务处理流程；本机通信不等于完全离线。永久删除当前历史不会自动擦除旧备份、旧副本或服务方历史；恢复旧备份可能重新出现其中的记录。详细范围见 PRIVACY.md。

- [安装/升级/卸载](docs/INSTALLATION.md)
- [使用与恢复](docs/USER-GUIDE.md)
- [故障排查](docs/TROUBLESHOOTING.md)
- [安全说明](docs/SECURITY.md)
- [后续签名方案](docs/SIGNING-PLAN.md)
- [第三方许可清单](docs/THIRD-PARTY-NOTICES.md)

普通安装器按当前用户安装（`PrivilegesRequired=lowest`，仅写 HKCU），无需管理员权限。若直接从沙箱或带有 `Low Mandatory Level` 的本地构建目录运行 EXE，Windows MIC 会拒绝写入 Temp/HKCU；请将安装器复制到正常用户目录（如「下载」或「桌面」）后运行，切勿修改系统 ACL 或提权。

便携包须先解压到将长期保留的用户目录，然后从该目录的正常交互式 PowerShell 运行 `scripts\Manage-Installation.ps1 -Action Install`。该管理器通过官方 CLI 注册插件，创建自有启动路径；不会提供 Windows 应用列表中的卸载项。移除便携注册用同一管理器的 `-Action Uninstall`，默认保留历史；卸载注册后可自行移除该程序目录。不要删除位于 `%USERPROFILE%\.context-notes-native\data` 的用户数据库。

使用发布目录的 SHA256SUMS 验证 EXE / ZIP；ZIP 内的 SHA256.json 可验证逐文件内容。哈希用于完整性核对，不等于可信发布者签名。release.json 记录源码提交和预览状态。
