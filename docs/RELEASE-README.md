# Context Notes Native 0.3.0 Preview

Windows 10/11 x64 的 Codex Chat 划词追问工具。需要 .NET 10 Windows Desktop Runtime x64 和 Codex Desktop。

选中助手回答并点「提问」，在原文旁输入问题；Enter 发送、Shift+Enter 换行。Ctrl+Alt+H 打开本地历史，可搜索、归档和恢复。账号和模型来自官方 App Server，不收集登录凭据。

这是未签名的内部预览交付，尚未完成完整工具浮窗 UI 和干净 Windows 安装验收；LICENSE 未获新增分发授权，PRIVACY 为实现说明草案。请不要据此声称正式发布已经通过。

- [安装/升级/卸载](docs/INSTALLATION.md)
- [使用与恢复](docs/USER-GUIDE.md)
- [故障排查](docs/TROUBLESHOOTING.md)
- [安全说明](docs/SECURITY.md)
- [后续签名方案](docs/SIGNING-PLAN.md)

普通安装器按当前用户安装，不需要本项目额外请求管理员权限。若当前执行环境阻止写入当前用户卸载项，安装器会在部署前退出；不要修改系统安全策略来绕过。

便携包须先解压到将长期保留的用户目录，然后从该目录的正常交互式 PowerShell 运行 `scripts\Manage-Installation.ps1 -Action Install`。该管理器通过官方 CLI 注册插件，创建自有启动路径；不会提供 Windows 应用列表中的卸载项。移除便携注册用同一管理器的 `-Action Uninstall`，默认保留历史；卸载注册后可自行移除该程序目录。不要删除位于 `%LOCALAPPDATA%\ContextNotesNative\data` 的用户数据库。

使用发布目录的 SHA256SUMS 验证 EXE / ZIP；ZIP 内的 SHA256.json 可验证逐文件内容。哈希用于完整性核对，不等于可信发布者签名。release.json 记录源码提交和预览状态。
