# 安装、更新和卸载

Phase 8 更新（2026-11-19）：本项目已以 **MIT 开源许可证**公开发布 `0.3.0` 源码与 Windows 安装包 / 便携包。当前安装器与运行文件仍为**未签名（`NotSigned`）**，Windows SmartScreen 可能弹出“未知发布者”警告，请先对照 `SHA256SUMS` 校验文件完整性再运行（见 [SIGNING-PLAN.md](SIGNING-PLAN.md)）。独立干净 Windows OS（虚拟机/全新主机）全流程仍标记为 `NOT TESTED`。

## 标准安装（Windows EXE 安装器）

目标环境为 Windows 10（2004+）/ Windows 11 x64，要求已安装 **.NET 10 Windows Desktop Runtime x64** 与 **Codex Desktop**。安装器采用 `PrivilegesRequired=lowest`（仅写当前用户目录和 HKCU 卸载项），**无需管理员权限**，不写入 HKLM，不修改系统 ACL。

1. 从 Release 页面下载 `ContextNotesNative-0.3.0-setup.exe` 与 `SHA256SUMS`，并使用 `Get-FileHash .\ContextNotesNative-0.3.0-setup.exe -Algorithm SHA256` 核对哈希。
2. 请确保安装器位于普通用户目录（如「下载」或「桌面」）。若直接从带有 `Low Mandatory Level` SACL 的沙箱或本地受控构建目录运行，Windows MIC 会阻止其写入 `%TEMP%` 与 HKCU（Error 5），请先复制到普通用户目录后运行，切勿提权或修改系统 ACL。
3. 运行 `ContextNotesNative-0.3.0-setup.exe`：
   - 程序部署至 `%LOCALAPPDATA%\Programs\ContextNotesNative\versions\0.3.0`；
   - 通过官方 Codex CLI 注册本插件 marketplace 与 `SessionStart` Hook（不重写整个 Codex 配置）；
   - 创建当前用户启动监视器快捷方式（Start Menu 快捷方式为默认关闭的可选任务）；
   - Hook 运行时信任仍由 Codex 管理，如 Codex 提示审核，请用户在 Codex 界面手动完成。
4. 安装完成后进入 Codex Chat，选中助手回答确认「提问」按钮与旁注小框可用；也可通过 `Ctrl+Alt+H` 或 `--show-history` 打开历史窗口。

## 便携版安装与卸载（Portable ZIP）

1. 下载 `ContextNotesNative-0.3.0-portable.zip` 并对照 `SHA256SUMS`（以及包内逐文件 `SHA256.json`）校验完整性。
2. 解压至将长期保留的用户目录。
3. 在该目录打开交互式 PowerShell 运行：
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Manage-Installation.ps1 -Action Install
   ```
   便携模式不向 Windows「已安装的应用」注册卸载项。
4. 取消便携注册（默认保留本地历史）：
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Manage-Installation.ps1 -Action Uninstall
   ```
   卸载完成后可自行删除便携程序目录；不要删除 `%USERPROFILE%\.context-notes-native\data` 中的历史库。

## 升级、备份与旧数据迁移

- 当前活动数据目录为 `%USERPROFILE%\.context-notes-native\data\history.db`。
- 升级沿用同一安装器 AppId 与版本目录布局；部署器在停止自有进程后先使用 SQLite Backup API 备份当前数据库（含已提交 WAL）至 `%USERPROFILE%\.context-notes-native\backups\install-recovery-*`，失败时自动回滚；若回滚本身失败，将保留恢复副本并报告路径。
- 如检测到早期预览版遗留的 AppData 旧库而新目录尚无数据库，部署器会明确停止，避免静默创建空库覆盖旧历史。可在确认一致性备份后运行：
  ```powershell
  scripts\Manage-Installation.ps1 -Action Install -LegacyHistoryBackup <已核验的备份路径>
  ```
  该操作仅向新目的库导入，不覆盖旧源库或已有目的库。

## 完整退出与卸载

- **完整退出**：先停止 Watcher，再停止 Companion（`SelectionProbe.exe --stop-watcher`，随后 `SelectionProbe.exe --stop`）。若仅停止 Companion，前台运行的 Codex 会触发 Watcher 自动恢复它。
- **标准卸载**：通过 Windows「已安装的应用」或开始菜单卸载项执行。**默认保留本地历史**；仅在交互式卸载明确选择删除（或静默卸载显式传入 `/REMOVEHISTORY=1`）时才删除已知本地数据库与自动备份。卸载仅移除本插件、自有启动快捷方式和程序文件，不会删除其他 Codex 插件、登录状态或服务端历史。

## 从源码打包构建

```powershell
scripts\Build-Release.ps1 -Compiler <ISCC.exe 路径> [-OutputDir <中完整性输出目录>]
```

默认会先运行单元测试，发布产物含 `ContextNotesNative-0.3.0-setup.exe`、`ContextNotesNative-0.3.0-portable.zip` 与 `SHA256SUMS`（便携包内附逐文件 `SHA256.json` 与 `release.json`）。请勿将 `artifacts/`、本地数据库或诊断日志提交至 Git。
