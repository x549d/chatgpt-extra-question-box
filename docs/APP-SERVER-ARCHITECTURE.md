# Codex App Server 接入架构（`APP-SERVER-ARCHITECTURE.md`）

> Phase 5 更新：生产旁注使用 durable Thread（ephemeral=false）、本地 SQLite 历史和 thread/resume；不可用的旧 Thread 必须明确确认新上下文。下面的 ephemeral=true 描述保留为 Phase 4 历史设计，当前实现见 [Phase 5](PHASE-5-PRODUCT.md)。

本文档说明 `ChatGPT Context Notes Native` 在 Phase 4 中如何复用本机已登录的 Codex Desktop 账号，通过官方 `codex.exe app-server`（stdio JSON-RPC 2.0）为划词旁注小框提供实时流式 AI 问答与连续追问能力。

---

## 一、总体分层架构

```text
Codex Desktop (Chat 模式)
       │  Windows UI Automation 读取选中文字与屏幕矩形
       ▼
ProbeWorker / ProbeMonitor
       │  生成不可变 SelectionSnapshot
       ▼
OverlayHost (STA Dispatcher)
       │  点击「提问」创建或激活旁注窗口
       ▼
AnnotationWindow (WPF UI 层：仅负责展示、输入与焦点)
       │  SubmitDraft / CancelGeneration / CloseByUser
       ▼
AnnotationService (业务协调层：管理会话、流式合并、多旁注与账号隔离)
       │  EnsureClientAsync / StartThreadAsync / StartTurnAsync / InterruptTurnAsync
       ▼
CodexAppServerClient (协议层：stdio JSON-RPC 2.0 异步多路复用客户端)
       │  stdin / stdout (UTF-8 行分隔 JSON) + Win32 Job Object 生命周期绑定
       ▼
codex.exe app-server -c allow_managed_hooks_only=true
```

### 核心职责边界
1. **`AnnotationWindow`（UI 层）**：
   - 仅负责展示引用原文、多轮问答气泡、流式文本与状态提示，以及处理键盘/IME 输入（`Enter` 发送、`Shift+Enter` 换行、`停止` 按钮触发取消）。
   - 不直接触碰进程、管道或 JSON-RPC 协议。
2. **`AnnotationService`（会话与状态协调层）**：
   - 管理 `CodexAppServerClient` 连接池与异常重连。
   - 维护 `AnnotationId -> ThreadId` 的一对一独立映射。
   - 校验账号状态与 `AccountScopeId`，防止跨账号混用 `ThreadId` 或历史记录。
   - 路由并合并（30 ms 窗口）高频 `item/agentMessage/delta` 流式增量，安全投递到 WPF Dispatcher。
   - 处理用户取消（`turn/interrupt`）、窗口关闭时的回调解绑与服务端中断。
3. **`CodexAppServerClient` & `StdioAppServerTransport`（传输与协议层）**：
   - 启动 `codex.exe app-server` 子进程，绑定 Windows Job Object（`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`），确保宿主退出或崩溃时绝不残留孤儿进程。
   - 维护单写锁（`SemaphoreSlim`）与单异步读取循环，按 `\n` 边界重组任意分片/粘包的 UTF-8 流，按请求 `id` 并发分发响应，按 `method` 分发服务端通知。

---

## 二、JSON-RPC 协议流程

当前本机安装版本为 `codex-cli 0.169.0-alpha.11.1`（桌面包 `26.1111.2228.0`）。完整交互序列如下：

1. **初始化握手（`initialize` → `initialized`）**
   - 请求：`{"id":1,"method":"initialize","params":{"clientInfo":{"name":"context-notes-native","title":"ChatGPT Context Notes Native","version":"0.4.0"}}}`
   - 响应：返回 `codexHome`、`platformFamily`、`platformOs`、`userAgent`。
   - 通知：客户端立即发送 `{"method":"initialized"}` 完成握手。
2. **账号状态读取（`account/read`）**
   - 请求：`{"id":2,"method":"account/read","params":{"refreshToken":false}}`
   - 响应：包含 `requiresOpenaiAuth`、`userId` 与 `account`（`type: "chatgpt"`、`planType: "plus"` 等）。
   - 客户端在内存中对 `accountType|userId|email|workspaceName` 计算不可逆 SHA-256 前缀指纹（如 `acct-154d33b69e88`），不保存、不输出任何明文邮箱、User ID 或 Token。
3. **模型目录读取（`model/list`）**
   - 请求：`{"id":3,"method":"model/list","params":{}}`
   - 响应：返回可用模型数组（本机实测返回 10 个模型，默认模型为 `gpt-6.2-sol`）。若查询失败，自动回退为服务端默认模型，不阻断旁注问答。
4. **独立旁注线程创建（`thread/start`）**
   - 请求参数：
     - `cwd`：隔离的空工作目录（`%LOCALAPPDATA%\ContextNotesNative\empty-workspace`），避免自动读取用户工程文件。
     - `ephemeral: true`：标记为临时线程，避免污染 Codex Desktop 左侧主聊天历史列表。
     - `approvalPolicy: "never"`、`sandbox: "read-only"`：禁止未授权命令执行或文件写入。
     - `developerInstructions`：限定仅针对引用原文与当前旁注历史进行纯文本解答，不调用外部工具。
5. **发起追问轮次（`turn/start`）**
   - 请求参数：`threadId`、`input: [{"type": "text", "text": "..."}]`、`approvalPolicy: "never"`、`sandboxPolicy: {"type": "readOnly", "networkAccess": false}`。
   - 首轮（或 App Server 重连后重建线程的首轮）自动封装 `<<<QUOTED_SELECTION ... QUOTED_SELECTION` 原文与用户问题；同一线程后续追问直接发送后续问题，由服务端 Thread 保持完整上下文。
6. **流式增量与完成通知**
   - `item/agentMessage/delta`：携带 `threadId`、`turnId`、`itemId`、`delta`，实时追加至当前轮次缓冲区。
   - `item/completed`：当 `item.type == "agentMessage"` 时校验完整文本。
   - `turn/completed`：携带 `turn.status`（`completed` / `interrupted` / `failed`）。仅当 `status == "completed"` 时将该轮回答标记为 `IsCommittedHistory = true`。
7. **取消生成（`turn/interrupt`）**
   - 请求：`{"id":N,"method":"turn/interrupt","params":{"threadId":"...","turnId":"..."}}`
   - 服务端确认中断后返回 `{}` 并推送 `turn/completed`（`status: "interrupted"`）。客户端同时将该 `turnId` 加入已取消集合，丢弃任何网络迟到的残余 `delta`。

---

## 三、多旁注与账号隔离设计

- **多旁注隔离**：每个 `AnnotationItem` 拥有独立的 `ThreadId` 与 `Messages` 列表。`AnnotationService` 使用 `activeByAnnotation`（按 `AnnotationId`）与 `activeByThread`（按 `ThreadId`）双字典路由流式事件，支持 3 个及以上旁注同时生成而互不串流。
- **账号切换隔离**：每次发起 `AskAsync` 时核对当前 `account.ScopeFingerprint` 与旁注绑定的 `item.AccountScopeId`。一旦检测到账号切换，立即清空旧 `ThreadId` 并阻止将旧账号上下文发往新账号。
- **窗口关闭与进程防泄漏**：关闭旁注窗口时立即调用 `session.DetachUi()` 切断 UI 回调，并在后台触发 `turn/interrupt`；`OverlayHost.Dispose()` 时销毁 `AnnotationService` 并关闭 `StdioAppServerTransport` 与 Win32 Job Object。
