# 安装、更新和卸载

目标为 Windows 10/11 x64，要求 .NET 10 Windows Desktop Runtime x64 和已安装的 Codex Desktop。安装器按当前用户安装，无需管理员权限。0.3.0 为本机验证预览版；未签名安装器不应作为正式公开发布版本。

运行 `ContextNotesNative-0.3.0-setup.exe`。安装器部署到 `%LOCALAPPDATA%\Programs\ContextNotesNative\versions\0.3.0`，通过官方 Codex CLI 注册本插件 marketplace 与 SessionStart Hook，并建立当前用户启动监视器快捷方式。不会重写整个 Codex 配置。Hook 的运行时信任仍受 Codex 管理，若 Codex 要求用户审核，必须由用户在 Codex 界面完成；安装器不会绕过信任审核。

安装完成后进入 Codex Chat，选择助手回答中的文本，确认「提问」按钮和输入框。也可从开始菜单打开 Annotation History。安装脚本能检查二进制哈希、Hook 清单与监视器，但选区入口和 Hook 的真实派发须在交互桌面确认，不能仅凭脚本退出码认定成功。

更新使用同一安装器 AppId 和版本目录布局；历史保留，数据库打开时执行事务迁移并生成迁移前备份。部署器在停止自有进程后保存数据副本和注册来源，失败时尝试恢复。失败报告必须保留，不能假定所有外部 CLI 操作都能回滚。旧版本目录可用于人工排障；更高 schema 的数据库不能用旧版程序强行打开。

卸载通过 Windows 应用列表或开始菜单执行。默认保留历史；交互卸载明确选择 No 才删除已知本地数据库及其自动备份，静默卸载始终保留。移除只作用于本插件、自己的启动快捷方式和程序文件；其他 Codex 插件、登录状态及服务端历史保留。

构建：`scripts\Build-Release.ps1 -Compiler <ISCC.exe>`，默认先运行测试。产物位于 artifacts，附 SHA-256 与包含逐文件哈希的暂存载荷。不要把 artifacts、数据库或诊断日志提交 Git。干净 Windows 环境测试、可信代码签名、发布者法律信息与正式隐私政策未完成前，不应宣布正式发布。
