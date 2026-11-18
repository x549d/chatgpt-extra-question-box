# Phase 5I — 安装器修正与隔离验证

当前源码继续使用 Inno Setup，`PrivilegesRequired=lowest`、HKCU、当前用户目录，没有改写为 HKLM、要求管理员、修改 ACL 或采用提权方式。

## 权限问题定位

原失败位置为 `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\{72EC0AA1-761D-4894-B2B8-A1FD53C06690}_is1`，Inno 返回 RegCreateKeyEx code 5。2026-11-18 再次用独立应用/配置/数据目录执行标准 EXE，仍为退出码 4，并回滚提取文件；同次还观察到 Start Menu 子目录创建被拒绝。

只读 ACL 审计显示父键属于当前用户且允许其 FullControl，没有产品残留键；当前执行身份不是有效管理员，IsTokenRestricted=False。通过 .NET 创建并清理唯一的 `ContextNotesNative.PermissionProbe-*` 子键成功。因此不能把问题简单归因于 HKLM、普通用户没有写 HKCU 的权限、受限令牌，或声称已经确定外部安全策略的具体来源。**已定位到安装器执行路径的拒绝访问；确切外部策略来源尚未证实。**

从真实桌面通过受支持启动 API 启动安装器也被系统以 ShellExecuteW=5 拒绝。没有绕过安全警告、修改文件/注册表权限或请求无依据的管理员提升。正常桌面安装仍需用户在允许该程序运行的环境补验。

## 已完成的修正

- 安装器在提取程序文件之前检查自身对当前用户卸载键的写入能力，失败明确退出，保持已有数据与程序。
- Start Menu 快捷方式为明确的可选任务，默认关闭，避免将不必要的目录写入作为安装前提；Watcher 自有启动路径独立管理。
- `/TESTROOT` 测试升级不再发出全局 Companion / Watcher 停止信号；测试上下文保存到安装目录，隔离卸载即使未再次传参也不会落回真实用户 profile。
- 升级前备份、失败时恢复使用 SQLite Backup API，包含已提交 WAL，备份时不运行数据库迁移；恢复采用数据库事务，不复制陈旧 WAL/SHM。
- 回滚本身失败时保留恢复目录并返回路径，禁止 finally 误删最后的恢复备份。
- 活动生产 Companion 存在时拒绝以 SkipLaunch 修改生产数据；故障注入只允许隔离数据。
- 同版本 Preview 更新会刷新已验证的自有插件文件，防止 CLI 复用旧缓存。
- 现有热部署脚本可直接使用经哈希验证的 Release 载荷，继续复用 WinSta0\Default Watcher；不重建 Phase 2R 架构。

## 实际验证

修正后标准 EXE 烟测：`artifacts\setup-5i-fixed\setup.log`，退出码 1，权限预检在任何程序文件部署之前失败，`program_files_created=False`。不再先提取文件再因同一权限问题回滚。此结果证明失败路径更安全，**不代表标准安装成功**。

88/88 xUnit，新增覆盖旧 schema 原样备份、活动 WAL 备份及已有目标的事务恢复。独立安装脚本 **16/16**，保留原 13 项并增加篡改拒绝、回滚失败保留恢复库、从保留库恢复旧 schema 和内容。测试使用真实 Codex CLI，完全隔离的 profile/data/startup，未卸载真实在用插件或删除原数据库。

启动前的补充一致性备份：`%LOCALAPPDATA%\ContextNotesNative\backups\history-before-5i-20261118-215136.db`，schema 2、quick_check=ok，SHA-256 `BAED0EAA6C2ED2A628679C77522EBD44D6ADE7DB78E67577FFC91764D41915A0`。

未检测到 WindowsSandbox.exe、vmrun.exe 或 VBoxManage.exe；未自行安装虚拟化组件、要求管理员权限或在真实环境执行卸载破坏性测试。**干净 Windows 安装/首次启动/AI/升级/标准卸载仍为 NOT TESTED**。隔离配置测试不等价于干净环境。

参考：[SQLite Backup API](https://sqlite.org/backup.html)、[Inno registry helper signatures](https://jrsoftware.org/inno-setup/documentation/topic_scriptfunctions.htm)。标准 EXE 和可信签名缺失仍是正式发布限制。
