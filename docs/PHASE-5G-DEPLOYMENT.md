# Phase 5G — 0.3.0 部署恢复

2026-11-18，Asia/Shanghai；起点 `310ff36`，main，工作区 CLEAN，无 remote；保留全部原提交。

## 启动前保护

实际数据库 `%LOCALAPPDATA%\ContextNotesNative\data\history.db`：schema 2，3 旁注，24 消息。主库 49,152 字节，WAL 0 字节，SHM 32,768 字节，自动备份存在。先以 SQLite Backup API 创建一致性备份，再启动程序，未覆盖或删除原库。

备份：`%LOCALAPPDATA%\ContextNotesNative\backups\history-before-5g-20261118-210130.db`。
备份 `integrity_check=ok`，schema 2，3 旁注、24 消息。
SHA-256：`9F0985815B43EEF94D11591BACF0F690D7028B247871B099C3301A7EEC627BD3`。

## 部署核对

实际缓存只有 0.3.0；任务提及的 0.2.0 缓存当前不存在，未依据目录名称判断版本。程序集版本 0.3.0.0，安装目录与缓存四个运行文件哈希全部相同。

| 文件 | SHA-256 |
| --- | --- |
| SelectionProbe.dll | B49A408229FD7CE14C69BA9B71CD393F65EE9BD19C7193F51F334D5A5A151B20 |
| SelectionProbe.exe | 66F61AD1FE0C9EE80BE437AAEBE538BA679B5B6CF87949C016E5582D065EFB7C |
| SelectionProbe.deps.json | B6DC352F611946A28F4C85F289F821A39B05365B3CD7CA1B3C31325227FF3017 |
| SelectionProbe.runtimeconfig.json | 96BC2946DFF8790400D652EC7F8DE7BB043E954980D59470C112788B68A1FBD3 |

Setup 2,321,859 字节，SHA-256 `9B40E2324D373DD1A745050B9D4A803CE1245EB8D05264D99742BF3BE6999B93`。
Portable ZIP 299,465 字节，SHA-256 `23BAD0DF64EAB131DA8762536FF6998B399E07A49BD2011F1D865AC275FCD9F7`。

官方 CLI 安装状态指向 `%LOCALAPPDATA%\Programs\ContextNotesNative\versions\0.3.0\codex-plugin`，缓存为 `%USERPROFILE%\.codex\plugins\cache\context-notes-native\context-notes\0.3.0`。没有改动 Codex 认证文件。

## 运行证据

从真实交互桌面启动现有 0.3.0 EXE，复用 DesktopStartup，没有改写 Phase 2R 架构。

| 角色 | PID / 父 PID / Session | 观察 |
| --- | --- | --- |
| Companion | 22828 / 40228 / 1 | 21:02:25 启动；status=companion_running |
| Watcher | 39416 / 22828 / 1 | 21:02:26 启动；status=desktop_watcher_running |
| 项目 App Server | 40508 / 22828 / 1 | 历史窗口触发后按需启动，初始化 193ms |
| 原客户端 App Server | 33644 / 3136 / 1 | 属于 Codex 主客户端，保留，未停止 |

Companion / Watcher 均通过现有强制 WinSta0\Default 交互桌面前置校验；启动、状态检查和进程归属一致，没有重复 Companion / Watcher。UIA 日志实际收到 selection_event_received，当前 Codex 模式会正确拒绝选区入口。Ctrl+Alt+H 实际打开历史窗口，并触发本项目 App Server 初始化；完整 Chat/AI/持久化交互转入 5H 验收。

桌面控制期间收到 Esc 后停止输入。用户再次明确授权继续后重建 Computer Use JavaScript 连接；不把该连接的旧停止状态误报为再次按键，未在停止授权期间发出桌面输入。

本阶段结论：启动恢复和部署核验 PASS；完整功能与后续运行 PID 变化以 5H 最终报告为准。
