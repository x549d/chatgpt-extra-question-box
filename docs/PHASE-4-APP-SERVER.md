# Phase 4 · Codex App Server 集成与实现说明（`PHASE-4-APP-SERVER.md`）

本文档记录 Phase 4A～4E 各子阶段的实现细节、接口契约、错误处理策略与安全边界。架构全景请参阅 [APP-SERVER-ARCHITECTURE.md](APP-SERVER-ARCHITECTURE.md)，验收结果请参阅 [PHASE-4-ACCEPTANCE.md](PHASE-4-ACCEPTANCE.md)。

---

## 一、子阶段交付概览（Phase 4A – 4E）

| 子阶段 | 范围 | 核心文件 | 状态 |
| --- | --- | --- | --- |
| **Phase 4A** | `CodexAppServerClient` 与 stdio JSON-RPC 传输层、分段读取、并发 ID 隔离、账号与模型读取、Win32 Job Object 绑定 | `src/SelectionProbe/CodexAppServerClient.cs` | **PASS** |
| **Phase 4B** | `AnnotationService` 真实 AI 问答接入、引用原文与问题拼装、请求状态机（`Idle` / `Connecting` / `Generating` / `Completed` / `Cancelled` / `Failed`） | `src/SelectionProbe/AnnotationService.cs`, `src/SelectionProbe/AnnotationItem.cs` | **PASS** |
| **Phase 4C** | `item/agentMessage/delta` 流式增量合并（30 ms 节流窗口）、`AnnotationWindow` 对话流展示、自动滚动与 `Shift+Enter` 换行 | `src/SelectionProbe/AnnotationWindow.xaml`, `src/SelectionProbe/AnnotationWindow.xaml.cs` | **PASS** |
| **Phase 4D** | 同一旁注多轮连续追问、多旁注独立 `ThreadId` 并发流式隔离、`ScopeFingerprint` 账号切换检测与隔离 | `src/SelectionProbe/AnnotationService.cs`, `src/SelectionProbe/AnnotationItem.cs` | **PASS** |
| **Phase 4E** | `turn/interrupt` 服务端取消生成、迟到 `delta` 过滤、App Server 崩溃自动重连恢复、窗口关闭解绑与子进程零残留 | `src/SelectionProbe/AnnotationService.cs`, `src/SelectionProbe/OverlayHost.cs` | **PASS** |

---

## 二、关键技术实现细节

### 1. 传输与协议鲁棒性（Phase 4A）
- **分段与粘包处理**：`StreamAppServerTransport` 基于 UTF-8 无 BOM 流按行读取，不假设单次系统读取对应单条 JSON 消息；已通过把单条响应拆成 4 段写入以及把多条 `delta` 与响应合并单次写入的单元测试验证。
- **服务端反向请求拦截**：当收到同时携带 `id` 与 `method` 的服务端请求（如非预期的审批请求）时，客户端立即返回 JSON-RPC 错误码 `-32601` 拒绝执行，确保只读追问回合绝不挂起。
- **递归 Hook 隔离**：启动 `codex.exe app-server` 时附加 `-c allow_managed_hooks_only=true`，防止旁注后台启动的 App Server 再次触发用户插件的 `SessionStart` Hook。

### 2. 引用上下文与多轮追问（Phase 4B & 4D）
- **原文不可变**：`AnnotationItem.SelectedText` 自选区快照创建后只读不可变。
- **首轮锚定与后续轻量追问**：
  - 当旁注首次在 `ThreadId` 上发起提问（`ThreadAnchoredWithQuote == false`）时，`BuildTurnPrompt` 将引用原文包裹在 `<<<QUOTED_SELECTION ... QUOTED_SELECTION` 块内，与 `【用户当前问题】` 明确分隔后发送。
  - 首轮收到 `turn/completed`（`status == "completed"`）后，标记 `ThreadAnchoredWithQuote = true` 并将回答记为 `IsCommittedHistory = true`。
  - 同一旁注的后续第 2..N 轮追问直接复用同一 `ThreadId` 发送后续问题；若期间 App Server 发生过重启，则在新 `ThreadId` 首轮自动带上引用原文与此前所有 `IsCommittedHistory == true` 的问答历史，实现无感恢复。

### 3. 流式输出合并与 UI 响应（Phase 4C）
- **首包即刻刷新 + 后续 30 ms 合并**：每个 `ActiveTurnSession` 在收到首个 `item/agentMessage/delta` 时立即刷新 UI（保证首字延迟最低），随后的高频增量通过 30 ms 计时器合并后再触发 `Dispatcher.BeginInvoke(DispatcherPriority.Render)`，避免每个 token 都触发整窗体布局计算。
- **未完成内容保护**：若轮次因用户取消或异常中断结束，已生成的局部文本仍保留在气泡中（标注 `AI（已停止）：` 或 `AI（未完成）：`），但其 `IsCommittedHistory` 设为 `false`，不会伪装成完整回答污染后续历史。

### 4. 取消生成、异常分类与资源释放（Phase 4E）
- **服务端取消确认**：点击小框中的红色「停止」按钮时，`CancelTurnAsync` 调用 `turn/interrupt`（携带 `threadId` 与 `turnId`），并根据服务端响应区分「服务端已确认中断」与「仅客户端停止显示」，同时将该 `turnId` 记入 `cancelledTurnIds` 以屏蔽后续迟到的 `delta`。
- **用户可读错误映射**：
  - 未找到 `codex.exe` → `未找到本机 Codex App Server 可执行文件（codex.exe）。`
  - 未登录 / 登录失效 → `当前 Codex 未登录或登录已失效，请在 Codex 客户端检查登录状态。`
  - 账号切换 → `检测到当前 Codex 登录账号已切换，为保护账号上下文隔离，旧账号旁注已停止继续追问。请重新划词创建新旁注。`
  - 进程意外退出 → `Codex App Server 连接中断，已保留当前已生成内容，请重试。`（下次发送时自动拉起新进程恢复）。
