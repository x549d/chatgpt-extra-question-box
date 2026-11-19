# Phase 6C 安装、升级和隔离验收

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
