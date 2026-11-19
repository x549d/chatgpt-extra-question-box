# Context Notes Native 0.3.0 (MIT Open Source)

Windows 10/11 x64 原生 Codex Desktop Chat 划词旁注追问工具。
开源许可证：**MIT License**（见 [LICENSE](LICENSE)；第三方组件声明见 [docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md)）。

> **独立第三方声明**：本项目为独立开源工具，与 OpenAI 无关联，未获 OpenAI 官方认证、赞助或背书。

## 系统要求

- Windows 10（2004+）或 Windows 11（x64）
- [.NET 10 Windows Desktop Runtime x64](https://dotnet.microsoft.com/)
- 已安装的 Codex Desktop

## 下载物与校验

| 文件 | 用途 | 签名状态 |
| --- | --- | --- |
| `ContextNotesNative-0.3.0-setup.exe` | 标准 Windows 当前用户安装器（无需管理员权限） | 未签名（`NotSigned`） |
| `ContextNotesNative-0.3.0-portable.zip` | 绿色便携包（解压后运行管理脚本注册） | 未签名（`NotSigned`） |
| `SHA256SUMS` | 发布包 SHA-256 完整性校验清单 | 文本校验文件 |

> **Windows SmartScreen / 未知发布者提示说明**：本版本暂未配备受信任的 Authenticode 代码签名证书，下载运行 `setup.exe` 时 Windows 可能弹出“Windows 已保护你的电脑 / 未知发布者”提示。请在运行前使用 `SHA256SUMS`（`Get-FileHash <文件> -Algorithm SHA256`）核对文件哈希是否一致，并可查阅 [docs/SIGNING-PLAN.md](docs/SIGNING-PLAN.md)。文件哈希仅验证完整性，不等同于可信代码签名。

## 安装与首次启动

- **标准安装**：将 `ContextNotesNative-0.3.0-setup.exe` 放在普通用户目录（如「下载」或「桌面」）后直接运行。安装器采用 `PrivilegesRequired=lowest`（仅写当前用户 HKCU 与 `%LOCALAPPDATA%\Programs\ContextNotesNative`），无需管理员权限。卸载通过 Windows「已安装的应用」执行，**默认保留本地历史**。
- **便携使用**：将 `ContextNotesNative-0.3.0-portable.zip` 解压到长期保留的用户目录，在交互式 PowerShell 中执行 `scripts\Manage-Installation.ps1 -Action Install` 完成插件注册；卸载注册执行 `scripts\Manage-Installation.ps1 -Action Uninstall`（默认保留历史）。
- **首次启动与 Codex Desktop 兼容性**：
  1. 启动 Windows 版 **Codex Desktop** 并确认已登录；若 Codex 首次提示是否信任本插件 `SessionStart` Hook，请在界面手动确认。
  2. 切换到 **Chat 模式**（Codex 编码工作台视图不会误触发旁注）。
  3. 选中任意一段助手回答 → 点击选区旁「提问」→ 在旁注小框中输入问题并按 `Enter` 发送（`Shift+Enter` 换行）；按 `Ctrl+Alt+H` 打开本地历史窗口。
  4. 如遇新版本 Codex Desktop 界面变动导致选区未触发，可通过 `SelectionProbe.exe --status` 排查并在仓库提交 **Compatibility Report** Issue。
- **数据备份与恢复**：
  - 手工在线备份：`SelectionProbe.exe --history-backup "%USERPROFILE%\.context-notes-native\data\history.db" ".\history-backup.db"`
  - 校验备份完整性：`SelectionProbe.exe --history-check ".\history-backup.db"`
  - 恢复数据：先执行 `SelectionProbe.exe --stop-watcher` 与 `SelectionProbe.exe --stop` 彻底停止后台，再将已核验的备份复制回 `%USERPROFILE%\.context-notes-native\data\history.db`（并清理旧 `-wal`/`-shm` 临时文件）。

## 隐私、构建洁净度与测试边界说明

- **AI 云端处理**：本地 Codex App Server 是官方桥接进程，选中的引用原文、提问及恢复对话所需的上下文会进入对应 AI 服务流程并发送至 OpenAI 云端处理，不承诺完全离线、零保留或不训练。
- **本地存储与零遥测**：本地历史保存于 `%USERPROFILE%\.context-notes-native\data\history.db`（无应用层加密），无任何遥测、无 Cookie/Token 读取。详见 [PRIVACY.md](PRIVACY.md)。
- **构建产物无本机路径**：`SelectionProbe.dll` 采用 `-p:DebugType=none` 发布，PE 调试目录不含 CodeView RSDS / `.pdb` 本机构建路径，发布包不含 `.pdb`、日志或个人数据。
- **测试边界**：本版本已在开发机通过 Release 0 警告/0 错误构建、106 项 xUnit、80 项内置自测、13 项发布包审计、29 项标准 EXE 隔离安装/升级/回滚/卸载、19 项便携安装管理、23 项独立合成库永久删除、三浮窗并发与重启恢复及 30 分钟连续稳定性验收；**独立干净 Windows OS（虚拟机/全新主机）全流程测试当前为 `NOT TESTED`**。

## 随包文档

- [安装、更新与卸载](docs/INSTALLATION.md)
- [使用与恢复指南](docs/USER-GUIDE.md)
- [故障排查](docs/TROUBLESHOOTING.md)
- [安全与数据处理](docs/SECURITY.md)
- [隐私说明](PRIVACY.md)
- [代码签名计划](docs/SIGNING-PLAN.md)
- [第三方许可清单](docs/THIRD-PARTY-NOTICES.md)
