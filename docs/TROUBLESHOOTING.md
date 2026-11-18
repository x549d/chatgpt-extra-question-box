# 故障排查

- 未出现选区按钮：检查 Codex 为 Chat 模式且选中助手回答，确认 `SelectionProbe.dll --status` 和 `--watcher-status`。非交互桌面会明确拒绝启动；应由 WinSta0\Default 中的启动监视器重新拉起。
- 历史窗口：Ctrl+Alt+H 被其他程序占用时，用旁注按钮、选区入口右键菜单或开始菜单入口。
- 账号不匹配：旧账号历史保留，请在官方 Codex 客户端自行切换账号；工具不会操作登录对话框、提取 Token 或复制认证文件。
- Thread 恢复失败：保留历史并用「新 Thread 恢复」明确确认新上下文。不要伪造旧 Thread 可用，也不要直接修改数据库 ThreadId。
- 模型不可用：刷新模型目录并重新选择；用量缺失表示服务端未提供，不能根据本地消息数推算账单。
- 数据库被锁、不可写或磁盘空间不足：保持程序运行并修复原因，等待重试；写入失败会显示未保存提示，关键输入不落盘时不会开始新请求。
- 数据库损坏/版本过高：停止程序，保留数据库与 WAL/SHM 整套原文件，按使用说明恢复一致性备份。不要删除数据库来掩盖故障。
- 安装失败：先确认 .NET 10 Desktop Runtime 和 Codex CLI 存在；从载荷目录用 PowerShell 执行 `scripts\Manage-Installation.ps1 -Action Check`。部署器回滚失败时先恢复备份与原 marketplace 路径，再启动旧版。不要覆盖整个用户 config.toml。
- Hook 信任：插件已注册不代表 Hook 已获运行时信任，按 Codex 官方界面完成审核；安装器不自动点击权限确认。
- 普通日志位于 `%LOCALAPPDATA%\ContextNotesNative\logs`，只含元数据。分享前仍应检查路径和账号作用域指纹；不要分享含对话的数据库或 Codex 认证文件。
