# Phase 4 · Codex App Server 接入与流式追问验收报告（`PHASE-4-ACCEPTANCE.md`）

验收日期：2026-11-18（UTC+08:00）
基线提交：`04b1d35`（生产浮层修复版，基于 Phase 3 `56b4b6c`）
目标环境：Windows 11 24H2（`26100.7623`）、Codex Desktop `26.1111.2228.0`、本机 `codex-cli 0.169.0-alpha.11.1`、单屏 144 DPI（150%）、.NET 10 + WPF。

---

## 一、阶段验收总览（Phase 4A – 4E）

| 阶段 | 核心验收项 | 状态 | 证据摘要 |
| --- | --- | --- | --- |
| **Phase 4A** | App Server 启动、`initialize`、`account/read`、`model/list`、`thread/start`、无 Edge 授权、零明文凭证 | **PASS** | 真实本机 `codex.exe app-server` 初始化耗时 `161 ms`（`platform=windows`），读取现有 ChatGPT Plus 登录（作用域指纹 `acct-154d33b69e88`），加载 10 个模型（默认 `gpt-6.2-sol`），成功创建 `ephemeral=True` 独立线程。 |
| **Phase 4B** | `AnnotationService` 真实 AI 请求、自动携带引用原文与问题、6 种请求状态流转 | **PASS** | 真实请求包含 `<<<QUOTED_SELECTION` 原文与用户问题，状态依次经历 `Connecting` → `Generating` → `Completed`，无需额外登录或打开外部窗口。 |
| **Phase 4C** | `item/agentMessage/delta` 流式回答显示、30 ms 增量合并、完成与失败状态区分 | **PASS** | 真实请求收到多次流式 UI 增量回调（首轮 8 次刷新、次轮 6 次刷新），仅在收到 `turn/completed`（`status=completed`）后标记为 `IsCommittedHistory=true`。 |
| **Phase 4D** | 连续追问上下文保持（5 轮回归 + 真实账号连续追问）、多旁注独立 `ThreadId` 隔离、账号切换隔离 | **PASS** | 真实账号下第 1 轮引用 `def calculate(a, b): return a * b + 10`，第 2 轮仅问 `如果调用 calculate(3, 4)，返回值是多少？`，复用同一 `ThreadId` 准确返回 `22`；第二个旁注 `ann-live-2` 获得不同 `ThreadId` 且消息数互不污染；xUnit 验证 5 轮连续追问、3 旁注并发流式隔离与账号切换阻断。 |
| **Phase 4E** | `turn/interrupt` 服务端取消生成、迟到 `delta` 丢弃、App Server 崩溃重连恢复、Win32 Job Object 零孤儿进程 | **PASS** | 取消单个旁注通过 `turn/interrupt` 获服务端确认且不影响并发运行的其他旁注；App Server 模拟崩溃后保留局部内容并可在下一轮自动重连恢复；真实测试结束后 `server_pid=28196` 确认退出（`server_exited=True`）。 |

---

## 二、真实本机 Codex App Server 验证记录（`--verify-app-server`）

通过 `dotnet SelectionProbe.dll --verify-app-server` 直接调用本机已安装的 `%USERPROFILE%\AppData\Local\OpenAI\Codex\bin\c38161ad8494f88c\codex.exe app-server`，使用当前 Windows 用户的现有 ChatGPT Plus 登录完成端到端真实推理验证：

```text
event=app_server_initialized pid=28196 platform=windows latency_ms=161
event=app_server_thread_started model=gpt-6.2-sol ephemeral=True
event=annotation_thread_bound annotation_id=ann-live-1 model=gpt-6.2-sol
event=app_server_turn_started status=inProgress input_length=167
event=annotation_turn_completed annotation_id=ann-live-1 response_length=28
live_turn1 state=Completed server_pid=28196 model=gpt-6.2-sol account_scope=acct-154d33b69e88 thread_len=36 ui_updates=8 reply_len=28
event=app_server_turn_started status=inProgress input_length=46
event=annotation_turn_completed annotation_id=ann-live-1 response_length=2
live_turn2 state=Completed same_thread=True mentions_22=True message_count=4 ui_updates=6 reply_len=2
event=app_server_thread_started model=gpt-6.2-sol ephemeral=True
event=annotation_thread_bound annotation_id=ann-live-2 model=gpt-6.2-sol
event=app_server_turn_started status=inProgress input_length=154
event=annotation_turn_completed annotation_id=ann-live-2 response_length=29
live_annotation_b state=Completed distinct_threads=True a_messages=4 b_messages=2 b_reply_len=29
live_cleanup server_pid=28196 server_exited=True
verify_exit=0
```

### 关键证据解析
1. **复用当前登录且零凭证泄露**：`account/read` 识别到本机 ChatGPT Plus 账号，仅生成不可逆 SHA-256 作用域标识 `acct-154d33b69e88`，全程未调用 Edge、未发起 OAuth、未读取或记录明文 Token / Cookie / 邮箱。
2. **真实引用理解与流式返回**：`ann-live-1` 第 1 轮引用代码 `def calculate(a, b): return a * b + 10` 并提问，触发 8 次流式 UI 更新后完成（`state=Completed`）。
3. **真实同线程连续追问**：`ann-live-1` 第 2 轮仅发送 46 字符后续问题（未重复发送代码原文），`same_thread=True` 证实复用同一服务端 Thread，模型结合上一轮代码上下文准确算出 `3 * 4 + 10 = 22`（`mentions_22=True`）。
4. **真实多旁注独立 Thread**：`ann-live-2` 创建了完全独立的第二个 `ThreadId`（`distinct_threads=True`），完成后 `ann-live-1` 保持 4 条消息（2 问 2 答）、`ann-live-2` 保持 2 条消息（1 问 1 答），互不干扰。
5. **进程清理**：`AnnotationService.Dispose()` 后，子进程 `PID 28196` 立即退出（`server_exited=True`），无残留 `codex.exe` 孤儿进程。

---

## 三、构建、自动化测试与生产部署核验

| 检查项 | 结果 | 详细数据 |
| --- | --- | --- |
| `dotnet build -c Release` | **PASS** | `ChatGptContextNotesNative.sln` 0 警告、0 错误。 |
| 项目内置自测（`--self-test`） | **PASS（80 / 80）** | 保留 Phase 3 原有 78 项自测，新增 2 项 Phase 4 自测，80/80 全部通过。 |
| xUnit 单元测试（`dotnet test`） | **PASS（39 / 39）** | 保留 Phase 3 原有 27 项 xUnit 测试，新增 12 项 Phase 4 专项 xUnit 测试（覆盖 `initialize`、分段/粘包流读取、多请求并发乱序响应、脱敏账号指纹与模型解析、断连与超时、真实引用与流式问答、5 轮连续追问、3 旁注并发流式隔离、账号切换阻断、单旁注取消与迟到 `delta` 过滤、App Server 崩溃重连恢复、UI 状态切换），发现 39 项，通过 39 项，失败 0 项。 |
| 插件缓存部署与 SHA-256 校验 | **PASS** | `codex-plugin\plugins\context-notes\bin\` 与 `%USERPROFILE%\.codex\plugins\cache\context-notes-native\context-notes\0.2.0\bin\` 下的 `SelectionProbe.dll`（`5DEF54FF2EBF1464A56A75E021DAFA6F8DC44B879F5ABD15CB643776EE29FD43`）和 `SelectionProbe.exe`（`72577884B4CD74669C9A8D2F9FD1D9C6DD141A7A78400DC60F91E646A4A139EF`）逐字节一致。 |
| `WinSta0\Default` 后台运行状态 | **PASS** | 通过交互桌面 Watcher（PID `38868`）热重启生产 Companion（PID `40464`），`--status` 返回 `companion_running`，`--watcher-status` 返回 `desktop_watcher_running`，无孤儿进程。 |
| 凭证与敏感信息扫描 | **PASS** | `git diff --check` 无格式错误，全仓与暂存区扫描 0 凭证命中。 |

---

## 四、桌面场景测试矩阵（含人工 UI 复核说明）

| 场景 | 状态 | 验证方式与依据 |
| --- | --- | --- |
| 1. 选中普通助手文本并点击提问 | **PASS** | Phase 3/04b1d35 实机已验收通过，Phase 4 保留完整选区与快照链路。 |
| 2. 检查引用内容与输入问题 | **PASS** | 原文逐字绑定在 `QuoteTextBlock`，输入框支持中文 IME、`Enter` 发送、`Shift+Enter` 换行。 |
| 3. 发送问题并获取真实 AI 流式回答 | **PASS** | `AnnotationService` 实连本机 `codex.exe app-server`（ChatGPT Plus）完成真实流式生成，STA 测试验证 `AnnotationWindow` 实时渲染气泡与自动滚动。 |
| 4. 连续追问（多轮上下文保持） | **PASS** | 本机真实账号实测 2 轮上下文计算追问通过；xUnit 实测同一旁注连续 5 轮追问保持 `ThreadId` 与 10 条有序历史。 |
| 5. 创建多个旁注并同时运行 | **PASS** | 本机真实账号实测双旁注独立 `ThreadId` 通过；xUnit 实测 3 旁注并发流式生成零串流。 |
| 6. 取消其中一个旁注的生成 | **PASS** | `CancelTurnAsync` 发送 `turn/interrupt` 并获服务端确认，仅停止目标旁注并屏蔽迟到 `delta`，其他旁注继续完成。 |
| 7. 关闭其中一个旁注 | **PASS** | `NotifyAnnotationClosed` 立即解绑已关闭窗口的 UI 回调并在后台中断其活动回合，其余旁注不受影响。 |
| 8. 重启 Companion 与清理 App Server 资源 | **PASS** | 实测停止旧 Companion 并通过 Watcher 重启新 Companion（PID `40464`）成功；`AnnotationService` 释放后 `codex.exe` 子进程立即退出（`server_exited=True`）。 |
| 9. 在真实 Codex 窗口内由用户手敲长回答并手点「停止」按钮 | **NOT TESTED（待用户物理交互最终体验）** | 协议层 `turn/interrupt` 与 WPF `StopButton` 均已通过自动化测试；在真实桌面窗口上手工点击红色「停止」按钮的视觉体验留给用户按下方最小步骤一键验证。 |
| 10. 多显示器、混合 DPI、Windows 重新登录 | **NOT TESTED** | 当前主机为单显示器 150% DPI 环境。 |

---

## 五、用户最小实机体验步骤

当前最新 Phase 4 生产后台（Companion PID `40464`、Watcher PID `38868`）已在你的交互桌面 `WinSta0\Default` 运行。你只需在已打开的 Codex Desktop（Chat 模式）中：

1. **划词提问与流式回答**：用鼠标选中任意一段助手回答，点击右侧出现的「**提问**」，直接输入问题并按 `Enter`，观察小框内实时流式显示 AI 回答。
2. **连续追问**：待回答完成后，在同一个小框底部「输入后续问题……」中继续输入第 2 个问题并按 `Enter`，验证连续追问。
3. **停止生成与多旁注**：输入一个较长的问题（如“请详细列举 10 个例子”），在生成过程中点击输入框右侧红色的「**停止**」按钮；或同时在另一段文字旁打开第二个小框独立提问。
