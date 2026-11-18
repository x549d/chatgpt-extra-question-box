# Phase 5G～5J 部署与发布准备报告

2026-11-18，Asia/Shanghai。基线 `310ff36`，main，无 remote；不 push、不创建 Tag。此报告记录本轮实际证据，正式发布仍受桌面、安装和签名门禁限制。

## 分阶段交付

- `347127c`：5G，数据库一致性备份、0.3.0 启动、缓存及进程验证。
- `3e08f86`：5H，历史更新时间修正、原生选择控件对比度修正，真实桌面证据及未测项。
- `c0229a9`：5I，SQLite 在线备份/恢复、回滚失败保留、安装器权限预检及隔离升级。
- `7b49853`：5J，包审计脚本、用户发布材料和签名计划。最终验证另作 `docs: finalize phase 5G-J verification` 仅验收文档提交；其最终哈希见交付回复和 git log -1。包内 release.json 指向构建源码提交，不使用事后文档提交冒充源码版本。

## 部署和数据

5G 实际恢复 Companion 22828、Watcher 39416（Session 1，WinSta0\\Default），按需启动项目 App Server 40508。原客户端 App Server 33644 保留。启动详细哈希、时间和首次备份见 [5G](PHASE-5G-DEPLOYMENT.md)。最后构建部署结果追加在本文末。

数据库 `%LOCALAPPDATA%\ContextNotesNative\data\history.db`，schema 2；最初 3 旁注/24 消息。本轮真实交互后 6 旁注/26 消息，原三条仍各 8 消息。没有覆盖或删除原库。原跨进程/三旁注 AI 验证仍是前一轮证据；本轮完整 Codex 退出重启未测。

一致性备份均通过 SQLite Backup API：

| 备份名（位于当前用户 backups） | SHA-256 |
| --- | --- |
| history-before-5g-20261118-210130.db | 9F0985815B43EEF94D11591BACF0F690D7028B247871B099C3301A7EEC627BD3 |
| history-before-5i-20261118-215136.db | BAED0EAA6C2ED2A628679C77522EBD44D6ADE7DB78E67577FFC91764D41915A0 |

备份 quick_check/integrity_check=ok，schema 2。新增原始备份接口不会迁移源库，包含已提交 WAL，允许事务恢复已有目标。回滚自身失败会保留 `%LOCALAPPDATA%\ContextNotesNative\backups\install-recovery-*` 并提供路径；卸载不默默清除最后恢复副本。

## 真实桌面验收

完整证据与最少人工补验见 [5H](PHASE-5H-DESKTOP.md)。已实际完成助手划词、按钮显示、历史列表、引用/问题搜索、日期筛选、空旁注归档/恢复、回收站恢复、真实模型/用量窗口。用户亲自点击浮窗发送合成问题并回复“回答了”；实际新回答落库 2 消息。明确区分用户确认和代理亲自观察的结果。

两套受支持接口都不暴露工具浮窗，跨进程点击被拒绝；没有伪造窗口 ID 或绕过输入边界。**5H 部分通过，完整验收 NOT TESTED**：三个旁注各三轮、完整 Codex 重启续问、中文输入法、长代码/表格/代码复制和不完整流式 Markdown 仍需真实操作。未对用户原记录执行永久删除。

## 安装器

根因与日志见 [5I](PHASE-5I-INSTALLER.md)。Inno 仍为当前用户 lowest/HKCU，不需要管理员。父键 ACL 和同身份 .NET 创建探针均正常；Inno 子执行路径 RegCreateKeyEx=5，真实桌面启动 ShellExecuteW=5，确切外部策略来源尚未证实。没有改 ACL、提权或换框架。

修正后的 EXE 在写程序文件前权限预检失败（exit 1，未部署程序文件），这是安全失败路径通过，**标准安装仍受阻**。脚本隔离安装覆盖新装、同版重装、升级迁移、故障回滚、回滚失败保留/恢复、篡改拒绝、卸载默认保留和显式删除合成数据；不等价于真实 EXE 安装。无可用 Windows Sandbox/独立 VM，干净 Windows 安装、首次启动、AI、升级、标准卸载为 **NOT TESTED**。

## 安全和发布状态

源码日志审查显示普通诊断记录事件、长度、延迟及脱敏作用域，不写完整聊天；发布包排除数据库、日志、源码、测试产物、认证配置及开发验收报告。全 Git 历史和中间载荷 gitleaks 均 exit 0/no leaks；最终包将再次检查。自动扫描不能证明不存在所有秘密；未读取 Cookie、提取 OAuth Token 或上传用户历史。

当前用户/本机未发现有效且有私钥的代码签名证书；EXE/DLL 为 NotSigned。没有购买或生成虚假可信签名。[签名方案](SIGNING-PLAN.md) 说明身份核验、SHA-256/RFC3161、签名后重建哈希及复验。LICENSE 仅记录现有 Proprietary 状态，没有虚构开放许可；公开分发前仍须维护者确认权利、正式条款和隐私联系方式。

内部受控使用和内部预览交付可继续；公开预览须先明确分发权利；**正式发布条件不满足**。未推送、未创建 Release Tag 或公开发布。

## 最终回归及构建记录

发布准备保留原 85 个 xUnit、80 个自测、13 个安装断言，并新增 3 个 xUnit 和 3 个安装断言。对源码提交 `7b4985347be14bdd598673f7b31a751b82b28fe3` 重新执行：

| 项目 | 实际数量和结果 |
| --- | --- |
| dotnet build -c Release | PASS，0 警告、0 错误 |
| dotnet test | 总计/执行 88，通过 88，失败 0，跳过 0；原有 85 全部保留 |
| 数据库/历史测试 | 19/19，为上述 88 的子集，不重复计数 |
| --self-test | 80/80 PASS，失败 0，跳过 0 |
| Test-Installation.ps1 | 16/16 PASS，失败 0，跳过 0；真实 CLI、隔离 profile/data/startup |
| Test-Release.ps1 | 9/9 PASS；哈希、版本、ZIP 路径和内容、来源和预览门禁 |
| PowerShell 静态解析 | 全部脚本 0 解析错误 |
| gitleaks | 全 Git 历史、最终载荷 exit 0/no leaks；提交前差异扫描通过 |
| 日志抽检 | 合成测试唯一短语/占位符命中 0；仅计数，无聊天全文输出 |
| 本轮完整真实桌面 | 部分通过，完整 5H NOT TESTED |
| 干净 Windows / 标准 EXE 全流程 | NOT TESTED / BLOCKED |

首次普通沙箱构建因读取当前用户 NuGet.Config 被拒绝而失败；没有改权限或配置，改在已获授权的当前用户执行环境重跑，上表是实际成功运行结果。证据位于工作区外临时测试目录 `<local-temp>\context-notes-phase5-results`，未提交测试日志。最终产物位于忽略的 artifacts 目录，不提交 bin/obj/数据库。

### 最终构建与交付文件

`release.json`：version 0.3.0，channel preview，source_dirty=false，signature NotSigned，formal_release_ready=false。源码为上述完整 7b49853；之后的提交仅修改本地验收报告，不改变打包文件。载荷 `artifacts\payload-82b679c130b44fa0bb887e6d95b07dcb` 共 22 个受哈希清单保护的文件（另有 SHA256.json）。

| 文件（artifacts） | 字节 | SHA-256 |
| --- | ---: | --- |
| ContextNotesNative-0.3.0-setup.exe | 2,282,709 | E9EA822D5EE68B746700BBD2F0B300DA045E89062F81853267DD51602CD7CF7C |
| ContextNotesNative-0.3.0-portable.zip | 237,326 | DD9436303412C277F746540FAAF19C2AA74B36E4DC81B46DA075C4633AD6D3F7 |

SHA256SUMS 实测匹配两个文件；包内逐文件清单全部匹配，版本与程序集均为 0.3.0。Inno Setup 6.7.4 官方编译器编译成功，其输出标记 Non-commercial use only；商用发布前须由维护者核实所用工具的相应许可，未购买或虚构商业授权。

最后对该 EXE 的独立 TESTROOT 烟测在进程启动时返回“操作已被用户取消”，没有生成 setup.log 或程序文件，没有继续绕过。前一份相同安装器源码的预检失败证据仍见 5I；不能把最终 EXE 的编译/包审计通过改写为安装成功。生产 Companion/Watcher 在烟测后仍正常。

### 最终部署与恢复

部署前一致性备份：`%LOCALAPPDATA%\ContextNotesNative\backups\history-before-final-20261118-221709.db`；schema 2、integrity_check=ok，SHA-256 `BAED0EAA6C2ED2A628679C77522EBD44D6ADE7DB78E67577FFC91764D41915A0`。内容未变，故与 5I 备份哈希相同。

停止旧 Companion 22828 后，通过现有 Watcher 的交互桌面热启动路径加载最终载荷；旧项目 App Server 40508 随所属 Companion 结束，未操作主客户端 App Server。最终状态：

| 角色 | PID / 父 PID / Session | 启动时间与结果 |
| --- | --- | --- |
| Companion | 24972 / 39416 / 1 | 22:17:34，companion_running |
| Watcher | 39416 / 22828 / 1 | 21:02:26，desktop_watcher_running；按原设计复用已运行 Watcher |
| 项目 App Server | 39672 / 24972 / 1 | 22:17:56，实际历史窗口按需初始化 196 ms |
| UIA worker | 32368 / 24972 / 1 | 22:19:21，uia_subscription_ready |
| 原客户端 App Server | 33644 / 3136 / 1 | 保留，未停止 |

Companion 和 Watcher 各一个，UIA worker 是独立角色。沿用强制 WinSta0\\Default 的 DesktopStartup；未重写架构。Watcher 进程复用的是此前已加载的镜像，未声称该进程重新加载了新代码；磁盘部署文件已全部更新，Watcher 源码本轮没有修改。

已验证安装目录清单 22 文件均与载荷一致；缓存与载荷四个运行文件 SHA-256 全部相同：

| 文件 | SHA-256 |
| --- | --- |
| SelectionProbe.dll | 0271B0DAF7D5C9528D48F6E01388D9987A51268FF92E75B31196D41617CDDF6C |
| SelectionProbe.exe | 71830C4598758328FB93E58ABA93C0DB8434D49FB046E64E3948F35B4A2FE924 |
| SelectionProbe.deps.json | B6DC352F611946A28F4C85F289F821A39B05365B3CD7CA1B3C31325227FF3017 |
| SelectionProbe.runtimeconfig.json | 96BC2946DFF8790400D652EC7F8DE7BB043E954980D59470C112788B68A1FBD3 |

插件注册状态保持版本 0.3.0，marketplace 指向当前用户 Programs\ContextNotesNative\versions\0.3.0\codex-plugin，缓存指向 .codex\plugins\cache\context-notes-native\context-notes\0.3.0；没有改认证文件。

重启后真实 Ctrl+Alt+H 打开最终构建历史窗口，六条记录恢复；原生筛选文字对比度可读，模型和用量来自实际服务端。随后正常关闭这两个管理窗口，后台保留运行。只读数据库复核：schema 2、integrity_check=ok、6 旁注/26 消息、回收站 0；原三条各 8 消息，其 ThreadId、账号和引用与 5G 备份一致；原消息差异 0，部署前后消息增减均 0。证明本轮 Companion 跨进程恢复既有数据，仍不等价于本轮三个新旁注各三轮及完整 Codex 重启验收。

最终变更覆盖基线后的 23 个源码/脚本/文档文件；保持 main，未 push、无 Tag。最终验收提交后要求 git status --short 为空，并再次扫描全历史；交付回复记录实际最终提交与 CLEAN 状态。
