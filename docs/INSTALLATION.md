# 安装、更新和卸载

5G～5J 更新：安装器仍为 `PrivilegesRequired=lowest`，不要求管理员提升；在写程序文件前检查本进程对 HKCU 自有卸载项的写入能力。拒绝时退出而不部署，不修改 ACL。Start Menu 快捷方式现在为默认关闭的可选任务；也可通过 Ctrl+Alt+H 或程序的 `--show-history` 入口访问历史。

安装器在本次受控执行环境仍被拒绝，不能把安装脚本的隔离验证视为标准 EXE 已通过。干净 Windows 环境也未就绪。可使用经哈希审计的便携载荷，在正常交互用户桌面运行其中 `scripts\Manage-Installation.ps1 -Action Install`；该流程不创建 Windows 应用列表卸载项。便携卸载用同一脚本 `-Action Uninstall`，默认保留数据，之后再移除程序目录。

升级备份位于 `%LOCALAPPDATA%\ContextNotesNative\backups\install-recovery-*`，通过 SQLite Backup API 保留 WAL 和旧 schema。回滚本身失败时保留目录，不会继续删除最后的恢复副本；确认恢复前不要清理。历史数据不进入安装包。

发布目录包含 `SHA256SUMS`，便携包内包含逐文件 `SHA256.json` 和 `release.json`。打包只包含用户说明、插件和运行文件，不包含数据库、日志、源码、测试或本机验收材料。签名流程见 [SIGNING-PLAN.md](SIGNING-PLAN.md)，当前仍未签名。

目标为 Windows 10/11 x64，要求 .NET 10 Windows Desktop Runtime x64 和已安装的 Codex Desktop。安装器按当前用户安装，无需管理员权限。0.3.0 为本机验证预览版；未签名安装器不应作为正式公开发布版本。

运行 `ContextNotesNative-0.3.0-setup.exe`。安装器部署到 `%LOCALAPPDATA%\Programs\ContextNotesNative\versions\0.3.0`，通过官方 Codex CLI 注册本插件 marketplace 与 SessionStart Hook，并建立当前用户启动监视器快捷方式。不会重写整个 Codex 配置。Hook 的运行时信任仍受 Codex 管理，若 Codex 要求用户审核，必须由用户在 Codex 界面完成；安装器不会绕过信任审核。

安装完成后进入 Codex Chat，选择助手回答中的文本，确认「提问」按钮和输入框。也可从开始菜单打开 Annotation History。安装脚本能检查二进制哈希、Hook 清单与监视器，但选区入口和 Hook 的真实派发须在交互桌面确认，不能仅凭脚本退出码认定成功。

更新使用同一安装器 AppId 和版本目录布局；历史保留，数据库打开时执行事务迁移并生成迁移前备份。部署器在停止自有进程后保存数据副本和注册来源，失败时尝试恢复。失败报告必须保留，不能假定所有外部 CLI 操作都能回滚。旧版本目录可用于人工排障；更高 schema 的数据库不能用旧版程序强行打开。

卸载通过 Windows 应用列表或开始菜单执行。默认保留历史；交互卸载明确选择 No 才删除已知本地数据库及其自动备份，静默卸载始终保留。移除只作用于本插件、自己的启动快捷方式和程序文件；其他 Codex 插件、登录状态及服务端历史保留。

构建：`scripts\Build-Release.ps1 -Compiler <ISCC.exe>`，默认先运行测试。产物位于 artifacts，附 SHA-256 与包含逐文件哈希的暂存载荷。不要把 artifacts、数据库或诊断日志提交 Git。干净 Windows 环境测试、可信代码签名、发布者法律信息与正式隐私政策未完成前，不应宣布正式发布。
