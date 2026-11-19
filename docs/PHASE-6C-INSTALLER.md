# Phase 6C 安装、升级和隔离验收

最终补充（2026-11-19，提交 `88be709`）：**已定位并修复标准 EXE 此前遇到的 Temp/HKCU 拒绝访问（Error 5）根因，并在隔离 `/TESTROOT` 下完成 29/29 项真实标准 EXE 安装、升级、回滚和卸载全流程验收。**

## 最终根因定位（Temp Error 5 与 HKCU Error 5）

- **真实根因**：工作区根目录 `<local-workspace>` 带有 Codex Windows Sandbox 设置的继承 SACL `Mandatory Label\Low Mandatory Level:(OI)(CI)(NW)`。直接编译输出在 `artifacts\` 下的 `ContextNotesNative-0.3.0-setup.exe` 继承了 `Mandatory Label\Low Mandatory Level:(I)(NW)`。
- Windows 强制完整性控制（MIC）会自动将带有 `Low Mandatory Level` SACL 的可执行文件降级为**低完整性进程（`S-1-16-4096`）**运行——即使普通用户从资源管理器直接双击。低完整性进程被 Windows 内核拒绝写入任何中完整性目标：
  1. `%LOCALAPPDATA%\Temp\is-*.tmp`（Error 5，发生在 Inno 自解压阶段、早于 `[Code]`）。
  2. `HKCU64\Software\Microsoft\Windows\CurrentVersion\Uninstall\...`（`RegCreateKeyEx` Error 5）。
  3. `%APPDATA%\Microsoft\Windows\Start Menu\Programs\...`（Error 5）。
  4. `%LOCALAPPDATA%\Programs\ContextNotesNative`（Error 5）。
- **验证证据**：将同一字节完全未改动的 `ContextNotesNative-0.3.0-setup.exe`（SHA-256 一致）复制到正常中完整性用户目录（如 `%LOCALAPPDATA%\Temp\...` 或下载目录）后，文件不再携带 `Low Mandatory Level`，**无需管理员权限、不修改 `%TEMP%` 或系统 ACL、不写入 HKLM**，标准 EXE 的 Temp 解压、HKCU64 预检、注册表写入、文件部署和卸载全部通过。

## `88be709` 修复内容

1. **安装脚本与回滚修正（`installer/ContextNotes.iss`）**：
   - 修复 `ssPostInstall` 中插件管理器失败时仍返回退出码 `0` 且不回滚全新安装的缺陷：新增 `GetCustomSetupExitCode` 返回非零退出码 `1`，全新安装失败时调用 `{uninstallexe} /ROLLBACK=1` 自动清理已部署文件和 HKCU 卸载项；升级失败时保留现有安装并由管理器恢复旧版注册与数据库。
   - 缩短 `/TESTROOT` 派生 AppId 为 16 位十六进制（`ContextNotesNative.Test.<16hex>`），消除此前 88 字符导致 Inno 截断键名与预检键名不一致的问题。
   - 新增 Temp 目录写入预检、Low-IL 诊断提示、`IsolatedTest` 防止隔离卸载误回退到生产配置，以及仅显式传入 `/REMOVEHISTORY=1` 时生效的静默删除历史测试开关（默认始终保留历史）。
2. **部署管理器修正（`scripts/Manage-Installation.ps1`）**：
   - 全新安装时若 `$ProfileRoot`（`CODEX_HOME`）尚不存在则自动创建，修复 `codex plugin marketplace list` 在全新用户配置下报错的问题；卸载时若配置目录不存在则安全跳过插件 CLI。
   - 隔离测试回滚备份目录按 `$DataRoot` 隔离，不再写入用户真实 `%USERPROFILE%\.context-notes-native\backups`。
   - 启动 Watcher 后增加 5 秒轮询等待 `--watcher-status`，避免异步拉起竞态误触发安装回滚。
3. **构建与验证门禁（`scripts/Build-Release.ps1`、`Test-InstallationPermissions.ps1`、`Test-StandardInstaller.ps1`、`Test-Installation.ps1`）**：
   - `Build-Release.ps1` 新增 `-OutputDir`、Low-IL 输出告警及编译器 Low-IL 检查；`Test-InstallationPermissions.ps1` 扩充至 6 项（新增进程完整性级别与可选安装包 Low-IL 校验）；`Test-Installation.ps1` 补齐 Codex 可执行文件回退查找。
   - 新增 `scripts/Test-StandardInstaller.ps1`（29 项真实标准 EXE 端到端测试）。

## `88be709` CLEAN 产物实测结果

- **权限探针（`Test-InstallationPermissions.ps1 -InstallerPath`）**：**6/6 PASS**（HKCU32、HKCU64、Programs 目录、Temp 目录、进程 Medium+ 完整性、安装包无 Low-IL；`effective_admin=False`）。
- **标准 EXE 端到端（`Test-StandardInstaller.ps1`，中完整性目录 + 隔离 `/TESTROOT`）**：**29/29 PASS**，覆盖：
  - 全新安装（退出码 0、Temp 预检 ok、HKCU64 预检 ok、精确测试卸载键、全新 `CODEX_HOME` 插件注册、启动快捷方式、SQLite schema 2 / `integrity_check=ok`）。
  - 原地重装/升级（退出码 0、保留既有历史）。
  - 全新安装注入失败回滚（退出码 `1`、清除 HKCU 卸载键、回滚程序文件和 `installation-context.ini`）。
  - 升级注入失败回滚（退出码 `1`、恢复迁移前 schema v1 与历史数据）。
  - v1→v2 真实 EXE 升级迁移（退出码 0、生成 `.pre-v2.db`、保留数据）。
  - 默认标准卸载 `unins000.exe`（退出码 0、移除卸载键/插件注册/启动快捷方式、**保留本地 `history.db` 及内容**）。
  - 显式删除历史卸载 `unins000.exe /REMOVEHISTORY=1`（退出码 0、删除本地 `history.db` 并移除卸载键）。
- **便携 ZIP 隔离管理器（`Test-Installation.ps1`，Windows PowerShell 5.1，解压自实际便携 ZIP）**：**19/19 PASS**。
- **Hook 诊断回归（`Test-HookDiagnostic.ps1`）**：**9/9 PASS**；PowerShell 静态解析：**11/11 脚本 0 错误**。
- **干净 Windows OS（Windows Sandbox / 独立虚拟机 / 全新 Windows 账户）**：**NOT TESTED**。复核确认本机无 `WindowsSandbox`、`vmrun`、`VBoxManage`、`vmconnect`、`hyperv`，启用可选功能或新建系统账户需要管理员权限，未擅自提权或虚构结果。

---

以下保留早期诊断记录（2026-11-19 13:24 前）：

后续补充（2026-11-19 13:24）：共享数据目录修正见 [存储记录](PHASE-6B-STORAGE.md)。新增显式旧库导入到新目标及源备份不变两项，当前中间载荷 `payload-8ee3308d5a454077be914022467ee9cf` 的包内管理器在 PowerShell 7 / Windows PowerShell 5.1 各 **19/19**，失败 0、跳过 0，保留全部原 16 项。它仍不等同于干净 Windows 或标准 EXE 验收。最终 CLEAN 包核验见 [最终报告](PHASE-6-ACCEPTANCE.md)。

2026-11-19，接续 `f7fbe71`。不提升管理员权限，不修改 ACL、全局环境或其他用户配置。

## 当前用户权限与标准 EXE

原产品卸载键：`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\{72EC0AA1-761D-4894-B2B8-A1FD53C06690}_is1`。没有需要机器级 HKLM 注册的功能，Inno 继续 `PrivilegesRequired=lowest`。实际当前有效管理员为 False。

新增权限探针分别在 HKCU Registry32、Registry64、本项目当前用户 Programs 目录和配置的 Temp 目录创建唯一的元数据文件/键，回读并仅清理自己创建的探针。不覆盖任何已有键。父进程上述位置可写；这不代表安装器子执行环境拥有相同能力。

用户从资源管理器实际启动 EXE 后提供错误截图：安装器无法创建 `%LOCALAPPDATA%\Temp\is-W2OWJESQ8Q.tmp`，Error 5 拒绝访问，发生在项目初始化脚本之前。代理同方式启动返回“操作已被用户取消”，没有 setup.log 或部署文件。只读 Temp ACL 检查显示当前用户 FullControl，父进程在普通及 is- 前缀的独有临时目录创建/写入均成功。

仅为单次隔离安装进程设置 TEMP/TMP 到工作区独有临时目录（通过 Start-Process 的 Environment 参数，不改变调用者或用户全局设置）后，安装器成功启动并创建临时目录，随后在 HKCU64 卸载键写入预检失败，exit 1、未部署程序文件。

证据：`artifacts\setup-6c-scoped-temp-5033e7dfecb745ad9376f101529fc8ef\setup.log`。日志实际记录 HKCU64 目标为独立测试 AppId，并明确未尝试机器级注册。生产卸载键在测试前后均不存在，生产 Companion/Watcher 未被停止。

因此目前有**临时目录创建与安装器执行路径的 HKCU 写入拒绝**两个具体障碍；不是笼统的“需要管理员”。父进程测试正常，无法据此确定导致安装器权限差异的具体外部机制，不猜测安全产品或断言签名能解决它。没有绕过权限、关闭安全策略或要求用户提权。**标准 EXE 全新安装/升级/卸载仍 BLOCKED**。

## 本轮修正

1. CLI stderr 与 JSON stdout 分离，保留并检查实际退出码；避免 Windows PowerShell 5.1 把非失败诊断转换为终止错误或污染 JSON。
2. 隔离测试改用临时 Python 脚本文件，避免 Windows PowerShell 5.1 原生 -c 引号丢失，并检查每次 Python 退出码；升级夹具 JSON 显式 UTF-8 无 BOM，符合官方 CLI 解析要求。这两项是测试夹具兼容问题，不伪称生产历史损坏。
3. 新增真实 CLI stderr 诊断回归；从便携 ZIP 中的实际管理器执行测试，不以仓库副本代替已打包脚本。
4. `/TESTROOT` 原先复用生产卸载 AppId，即使 profile/data 隔离，成功的 EXE 仍可能覆盖生产卸载注册。本轮改为由规范化测试根路径导出独立测试 AppId；未传 TESTROOT 时继续返回原产品 GUID。初始化预检也使用相同 ID。编译及实际预检日志验证了测试目标隔离。动态 AppId 需要 Inno `UsePreviousLanguage=no`，当前安装器只有原英文语言，未增加新语言或重写框架。
5. 注册预检记录准确的 HKCU64 目标与布尔结果，不把内置 helper 的返回值误解为可靠 Win32 LastError。

依据：[Inno AppId 与卸载键规则](https://jrsoftware.org/inno-setup/documentation/topic_setup_appid.htm)。

## 实际测试

当前中间载荷 `artifacts\payload-b5c63bb9c26d4ea8bb168b2989299512`（本轮最终发行前还会从 CLEAN 提交重建）：

- PowerShell 7：包内安装管理器 **17/17**，原有 16 项全部保留。
- Windows PowerShell 5.1：真实解压 portable ZIP 后，包内管理器 **17/17**。
- 各自覆盖隔离全新安装、同版重装、诊断 stderr、篡改拒绝、升级迁移、故障回滚、回滚失败保留/恢复、默认保留历史卸载、显式删除合成数据库及无关配置保留。
- 两次运行各 17 个断言，失败 0、跳过 0；不把它们相加冒充 34 个不同功能测试。全部操作使用独立 profile/data/startup 和真实官方 CLI，不卸载在用插件、不删除用户原库。
- 安装器编译成功；标准 EXE 未能完成安装，不将以上脚本测试等同于 EXE PASS。

未发现 WindowsSandbox.exe、vmrun、VBoxManage 或 vmconnect。没有擅自启用虚拟化或下载安装系统镜像。干净 Windows 的首次启动、真实划词、AI、升级与标准卸载均 **NOT TESTED**。

便携包已验证从实际 ZIP 独立提取并通过安装/升级/卸载管理器隔离流程。真实生产后台、划词、历史及 AI 另见 6A/6B；隔离脚本使用 SkipLaunch，不能证明干净机器的 UI 启动。

## 数据与备份异常的核实

恢复任务时发现 `history-before-6c-20261119-103819.db` 是 schema 2、0/0 的 40,960 字节备份。此前据它提醒“当前路径为空”，随后直接只读原库确认实际仍为 **9/48**；本轮所有原记录保留。不能凭一份空备份推断原库被清除。

该异常备份保留，原因未能回溯、当前相同原生 BackupFile API 无法复现。没有用它恢复原库。2026-11-19 12:33:47 再用原生 Backup API 和 Python Backup API 各创建新的一致性备份，均 9/48、integrity_check=ok；Python 副本 `history-before-6c-python-20261119-123347.db` 的 SHA-256 为 `C2AA1286BEB97300AB8FCDCD073FA45FA978871FBBD26FA73DF6F11A9B3A814F`。60 分钟观察使用同时间的 `history-before-6c-verified-20261119-123347.db` 作为原记录基线，结尾继续核对内容与标识差异。异常备份不被作为可靠恢复点。
