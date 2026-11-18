# SessionStart 冷启动专项

基线为 `e4785b3`，客户端为 Windows Codex Desktop `26.1111.2228.0`，本地插件为 `context-notes@context-notes-native` `0.2.0`。测试时间均为 UTC+08:00。

## 配置与生命周期

| 项目 | 核对结果 |
| --- | --- |
| 注册 | 插件 manifest 显式指向 `./hooks/hooks.json`。生产清单、Hook 定义与 `launch.cmd` 均与 `%USERPROFILE%\.codex\plugins\cache\context-notes-native\context-notes\0.2.0\` 下的文件逐字节一致，缓存内的 `bin/SelectionProbe.exe` 存在。 |
| 用户层 | 用户目录没有独立 `hooks.json`。`config.toml` 启用本插件，没有内联 `SessionStart` 或全局 Hook 禁用，也没有本条 Hook 禁用。 |
| 信任 | 已安装版的本地 Codex 运行时通过只读 `hooks/list` 返回 `source=plugin`、`enabled=true`、`trust=trusted`，无配置错误。桌面代理连接未开放，运行时核对使用同一安装版的独立只读连接。 |
| Matcher | 生产 `SessionStart` 省略 matcher。该写法匹配受支持的所有 source，包括 `startup` 和 `resume`。诊断模式显式匹配 `^(startup|resume)$`。 |
| Windows 启动 | `cmd.exe /d /c call "%PLUGIN_ROOT%\hooks\launch.cmd"`。其中 `PLUGIN_ROOT` 由 Codex 指向已安装缓存。脚本由绝对路径定位程序，手动诊断时可用 `%~dp0..`。Hook 自身工作目录来自会话，启动器启动子进程时使用程序目录。 |

`SessionStart` 属于运行会话生命周期，`startup` 对应新会话启动，带历史恢复运行会话使用 `resume`，matcher 对 source 应用正则。打开桌面历史页面是否启动或恢复运行会话，需要实测。文档没有承诺单纯打开已有 Chat 会派发 `SessionStart`。插件 Hook 需要单独信任，修改定义或相关脚本会触发重新审核。[官方 Hooks 说明](https://learn.chatgpt.com/docs/hooks)

## 独立诊断

`hooks/diagnostic-only.json` 是临时验收清单。用它替换已安装插件的 `hooks/hooks.json` 前备份原文件，将 `diagnose.cmd` 和 `diagnose.ps1` 随它部署。审核该命令后，以 `hooks/list` 核对 `trusted`。诊断期间正常停止伴随后台。诊断结束后恢复原生产清单，重新核对信任状态。

诊断通过 `async: true` 调用 Windows PowerShell，超时为 5 秒。它与 SelectionProbe 进程独立。`%LOCALAPPDATA%\ContextNotesNative\logs\hook-diagnostic.log` 每行只记录 `timestamp`、`hook_event_name`、`source`、`command_started` 和 `command_exit_code`。事件及来源使用允许名单，其他输入字段只在解析期间短暂存在。成功为 `0`，无效事件为 `2`，写日志失败为 `3`，输入等待超时为 `124`。标准输出和错误输出均被丢弃。

`scripts/Test-HookDiagnostic.ps1` 在隔离的临时目录用相同 Windows 命令运行结构化输入夹具，验证两个 source、无效输入、输入等待超时、静默运行、字段脱敏和写入失败。夹具结果用于验证工具本身。

## 诊断实测

临时清单加载后，运行时先报告 `trust=modified`。用户在桌面客户端审核后重新查询，得到 `enabled=true`、`trust=trusted`、`async=true` 和正确的 matcher。独立诊断期间 Companion 已正常停止。

| 用例 | 当前证据 |
| --- | --- |
| A，冷启动后新建空 Chat | 16:20:41 原桌面所有进程退出，Windows 重开后主 PID 由 `28004` 变为 `33200`。16:21:00.157 诊断记录 `SessionStart`、`source=resume`、`command_started=true`、`command_exit_code=0`。用户确认已打开空 Chat。此窗口内只有 `resume` 证据，无法将它归因于新建空 Chat。 |
| B，冷启动后打开已有 Chat | 16:26:16 观测到原客户端启动时的 13 个 PID 全部退出，新主 PID `24876`。16:26:31.534 再次记录 `resume`，诊断退出码 0。16:26:58 通过桌面客户端导航接口打开一个已有对话，未发送任何消息，随后至 C 前没有新增日志。 |
| C，保持进程并切换已有 Chat | 16:27:50 导航至另一个已有对话，同一客户端保持运行。16:28:12 只读检查报告 `mode=chat`，至 16:28:26 检查结束无新增日志。此用例没有实际 source 可记。 |

两次冷重启证明这台客户端可以派发 `resume`，也证明独立诊断脚本能从真实 Hook 收到输入。两次事件均发生在明确操作目标页面之前或无法与该操作建立先后关系。已有 Chat 页面的打开和切换本身没有观察到新的事件。此前在 `e4785b3` 上已有完全重启、打开已有 Chat 且后台未自动启动的实测记录。已确认不存在只匹配 `startup` 的配置。

## Windows 启动补充路径

生产 Hook 仍走原有事件配置。`scripts/Set-WindowsStartup.ps1` 可在当前用户的 Windows 启动文件夹注册一个指向已安装插件的快捷方式，并在当前会话启动监视器。使用 `-Disable` 停止并删除该快捷方式。[微软 Windows 启动说明](https://support.microsoft.com/zh-cn/windows/experience/startup-boot/configure-startup-applications-in-windows)

监视器每秒读取前台窗口所属 PID。只有 PID 变化时才验证它属于 Codex 桌面程序包。每个已验证的 Codex 进程成功启动一次后台。启动失败时，同一 PID 间隔 10 秒重试。平时只驻留一个监视器，关闭 Codex 后 Companion 按原有机制退出，监视器在当前用户会话中等待下次打开。对同一 PID 手动停止已启动的 Companion 后，监视器等待下一个 Codex PID。

Hook 和 Windows 监视器使用同一启动锁及原有 Companion 单实例锁。启动器验证到后台状态事件后才报告成功。`launch.log` 分别使用 `hook_fired source=SessionStart:*` 和 `desktop_startup source=windows_foreground`，两条路径可独立验收。

## 最终部署与冷启动结果

16:29:21 恢复生产清单和启动脚本，移除安装缓存里的临时诊断文件，确认四个 Release 运行文件完全一致。DLL 的 SHA-256 前缀为 `FFAB933C4FE5`。用户在桌面完成审核后，只读检查确认恢复的生产 Hook 为 `trusted`。

启动文件夹的快捷方式已指向当前插件的 `SelectionProbe.exe --watch-codex`。重复启动监视器和 Companion 都以状态码 0 退出。再次调用启动器记录 `companion=already_running`。停止 Companion 后，监视器保留运行但未在同一 Codex PID 上拉起第二个后台。现场监视器私有内存约 8.5 MiB，运行 245 秒累计 CPU 时间约 0.16 秒。Windows 重新登录时由系统启动快捷方式的流程未进行实机复验。

| 项目 | 结果 | 依据 |
| --- | --- | --- |
| 生产 Hook 配置加载与信任 | PASS | 本地同版本运行时返回 `source=plugin`、`enabled=true`、`trust=trusted`。 |
| `startup` 派发 | FAIL | A 的空 Chat 用例未记录 `startup`。该结果仅适用于本轮测试场景。 |
| `resume` 派发 | PASS | A、B 两次冷重启各有一次真实 `resume`，退出码均为 0。 |
| 已有 Chat 恢复 | PASS | 最终冷启动后在真实已有 Chat 中观察到 `mode_detection=chat` 及助手选区。 |
| 后台自动启动 | PASS | 停止旧后台后完全退出 Codex，首个新桌面 PID 为 `15744`。16:32:45 Windows 监视器自动启动后台 PID `17264`。最终用例无新的 `SessionStart` 启动记录。 |
| 后台单实例 | PASS | 同机重复监视器与伴随进程正常退出，重复启动器命中 `already_running`。 |
| 后台持续运行 | PASS | 自动启动的 PID `17264` 在 16:37:22 复核时仍为 `companion_running`，已持续 278 秒。 |
| 自动启动后划词与点击 | PASS | 16:33:03 助手选区为 `eligible` 并显示「提问」，16:33:05.124 记录点击且 Codex 保持前台。用户确认按钮出现并保留前台。 |
| Release 构建与自测 | PASS | 0 警告、0 错误。项目自测 55/55，独立 Hook 诊断 9/9。`dotnet test` 发现 0 项。 |
| 凭证扫描 | PASS | 检查跟踪文件和本次差异，未发现匹配的凭证内容。 |

最终用例未向测试 Chat 发送消息。程序包旧 PID 全部退出后由 Windows 正常打开新客户端，后台未通过手动脚本启动。
