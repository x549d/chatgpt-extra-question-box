# 故障排查

- HKCU 预检拒绝 / ShellExecuteW=5：安装器没有要求 HKLM 或管理员权限。父键可写并不证明当前安装器执行路径允许同样的写入；不要据此修改 ACL、关闭保护或绕过安全提示。保留安装日志，在允许执行该程序的正常桌面或独立测试环境补验；便携管理器是独立的已验证路径。
- 升级回滚也失败：错误会给出 `%LOCALAPPDATA%\ContextNotesNative\backups\install-recovery-*` 路径。保留该目录；修复原注册来源与启动条件后，用明确校验过的一致性备份恢复。新 `--history-backup <source> <destination>` 命令不迁移源 schema；指向现有目的库会替换其内容，必须先停止自有后台并保护目的库，不要对用户原库盲试。
- 自动验收接口无法选择工具浮窗：不改变 WS_EX_TOOLWINDOW 产品行为来伪造可操作性，也不构造窗口 ID。请人工完成该部分，记录实际结果；单元测试不能替代 UI 验收。

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
