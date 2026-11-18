# ChatGPT Context Notes Native
# 全新项目开发总需求书

你现在负责从零设计和开发一个 Windows 桌面辅助工具：

项目名称：ChatGPT Context Notes Native

项目目标：为 Codex 桌面客户端增加类似原生功能的划词追问能力。

项目路径：

<repo-root>

请将本文件作为整个项目的长期开发规范。

---

# 一、产品最终目标

开发一个能够融入 Codex 桌面客户端阅读体验的上下文追问工具。

用户在 Codex Chat 中阅读 AI 回答时，可以直接选择其中一段文字，在选区附近出现一个小型按钮：

「向 ChatGPT 提问」

用户点击按钮后，在选中文字旁边立即出现一个小型浮动旁注窗口。

用户可以直接在窗口中输入问题，调用 AI 获取回答，并连续追问。

## 1.1 最终交互流程

完整流程必须是：

1. 用户正常使用 Codex 桌面客户端。
2. 用户使用鼠标选中 Codex 助手回答中的一段文字。
3. 选区附近自动出现「向 ChatGPT 提问」按钮。
4. 用户点击按钮。
5. 立即创建一个浮动旁注窗口。
6. 自动获取并关联刚才选中的文字。
7. 不出现额外的文字预览确认页面。
8. 输入框自动获得焦点。
9. 用户直接输入问题。
10. 用户发送问题。
11. AI 实时流式返回回答。
12. 用户可以在同一个旁注中继续追问。
13. 用户可以创建多个独立旁注。
14. 关闭旁注后恢复原来的 Codex 页面。

## 1.2 必须满足的产品要求

正常使用时，屏幕上只显示 Codex 页面。

不需要：

- Edge 浏览器。
- Edge 扩展。
- Edge 扩展 ID。
- 浏览器授权。
- 常驻 Companion 主窗口。
- 常驻侧边栏。
- 独立聊天主界面。
- Ctrl+C。
- Ctrl+Shift+Q。
- 手动预览确认。
- 手动加载模型。
- 手动创建旁注。

后台可以运行必要的辅助进程，但不应该显示独立主窗口。

用户只在选中文字和点击追问按钮后看到额外 UI。

浮动按钮和旁注窗口必须按需出现。

---

# 二、开发环境

目标环境：

- Windows 10 / Windows 11。
- Microsoft Windows 桌面环境。
- Codex Windows 桌面客户端。
- .NET 8 或经验证更合适的受支持 .NET 版本。
- C#。
- WPF。
- Windows UI Automation。

技术选型需要在第一阶段进行可行性验证。

优先使用 Windows 官方公开接口。

禁止依赖未经证实的 Codex 内部实现。

---

# 三、总体技术架构

采用：

Background Companion + Selection Monitor + Floating Overlay + Codex App Server。

架构如下：

Codex Desktop
    |
    | 用户选择文字
    v
Selection Monitor
    |
    | 获取选中文字和屏幕坐标
    v
Floating Action Button
    |
    | 用户点击
    v
Annotation Overlay
    |
    | 输入问题
    v
Codex App Server Client
    |
    | JSON-RPC
    v
Codex App Server
    |
    v
AI 模型与认证体系

## 3.1 Background Companion

后台运行的 Windows 进程。

负责：

- 监测 Codex 前台窗口。
- 监测有效文本选区。
- 获取选中文字。
- 获取选区屏幕坐标。
- 创建浮动按钮。
- 管理旁注生命周期。
- 管理 App Server 连接。

要求：

- 不显示常驻主窗口。
- 不显示常驻侧边栏。
- 不显示常驻系统托盘图标。
- 不持续抢占 Codex 焦点。
- 不影响 Codex 正常鼠标和键盘操作。

## 3.2 Selection Monitor

负责获取 Codex Chat 中的真实文本选区。

优先研究：

- Windows UI Automation。
- AutomationElement。
- TextPattern。
- TextPattern2。
- TextPattern.GetSelection。
- TextRange.GetBoundingRectangles。
- TextSelectionChangedEvent。
- AutomationFocusChangedEvent。
- IAccessible。

要求同时获取：

1. 选中文字。
2. 选区屏幕矩形。
3. 当前前台窗口。
4. 选区是否仍然有效。

不能仅通过鼠标拖动距离推测选中文字。

不能将屏幕 OCR 识别结果冒充准确选区文本。

如果 UI Automation 发生阻塞：

- 放到独立后台执行单元。
- 使用严格超时。
- 不允许阻塞主 UI。
- 不允许扫描整个系统的 UI 树。
- 只访问当前 Codex 窗口相关控件。

如果需要，可以使用独立辅助进程隔离可能卡死的 UIA 调用。

## 3.3 选区获取降级方案

如果 UI Automation 无法直接获取文本，研究自动复制方案。

要求：

- 用户不需要手动按 Ctrl+C。
- 只在 Codex 前台窗口中检测到真实文本选择后触发。
- 仅在一次有效选择结束后执行一次性复制。
- 读取复制内容后立即处理。
- 尽可能恢复原剪贴板的格式和内容。
- 不进行后台持续剪贴板监控。
- 不将剪贴板内容写入日志。
- 不持久化无关剪贴板内容。

必须实际评估自动复制对其他剪贴板软件、剪贴板历史和云同步的影响。

如果无法安全实现，暂停并报告，不得静默启用。

---

# 四、浮动按钮

当用户完成有效文字选择后，在选区附近显示：

「向 ChatGPT 提问」

要求：

1. 只在 Codex 前台窗口中出现。
2. 只有存在有效文本选区才出现。
3. 尽可能贴近选区。
4. 不遮挡选中文字。
5. 选区取消后隐藏。
6. Codex 失焦后隐藏。
7. 选区变化后更新位置。
8. 页面滚动后更新位置。
9. 支持中文、英文和多行代码。
10. 支持多显示器和不同 DPI。

按钮采用轻量、简洁的设计，视觉上接近 Codex 原生上下文操作。

优先评估是否存在 Codex 公开支持的界面扩展机制。

如果不存在，采用独立透明 WPF Overlay 模拟原生按钮。

不修改 Codex 安装文件。

不注入 Codex 进程。

不修改 Codex 内部 DOM。

不读取 Codex 进程内存。

---

# 五、浮动旁注窗口

用户点击「向 ChatGPT 提问」后立即创建旁注。

不需要任何额外确认操作。

## 5.1 窗口功能

- 自动引用选中文字。
- 输入框自动获得焦点。
- 支持直接发送问题。
- 支持 Markdown。
- 支持代码块。
- 支持 SSE 或 App Server 增量事件。
- 支持连续追问。
- 支持取消生成。
- 支持拖动。
- 支持折叠。
- 支持关闭。
- 支持调整大小。
- 支持多个旁注同时存在。

## 5.2 窗口定位

优先放在选区右侧。

空间不足时：

1. 左侧。
2. 下方。
3. 屏幕内最合适的位置。

考虑：

- 多显示器。
- Windows DPI。
- Codex 最大化。
- Codex 窗口移动。
- Codex 页面滚动。
- 选区消失。

旁注不能出现在屏幕之外。

## 5.3 窗口生命周期

- 旁注创建后独立存在。
- 选区变化不能修改已有旁注的原始引用。
- 一个旁注关闭不影响其他旁注。
- Codex 失焦时可以保留已打开的旁注，但不得继续显示选区按钮。
- 关闭所有旁注后恢复纯 Codex 页面。

---

# 六、AI 接入：优先 Codex App Server

优先使用 OpenAI 官方 Codex App Server。

官方参考：

https://github.com/openai/codex

https://developers.openai.com/blog/codex-as-a-platform

重点研究：

- Codex App Server。
- JSON-RPC。
- stdio 通信。
- initialize。
- account/read。
- model/list。
- thread/start。
- thread/resume。
- turn/start。
- item/agentMessage/delta。
- turn/completed。
- turn/interrupt。

以上方法名称需要根据实际安装版本和官方协议核对，不得直接假设所有方法在当前版本均可用。

## 6.1 认证要求

首选：

直接使用当前 Windows 用户已经登录的 Codex 账号，由官方 Codex App Server 管理认证。

但必须先验证：

1. 独立启动的 App Server 是否能读取当前 Codex 登录状态。
2. account/read 返回的账号是否与 Codex 桌面端一致。
3. model/list 是否正常。
4. 真实推理是否成功。
5. 是否需要独立授权。
6. 账号限额和用量是否正常。

禁止：

- 读取 Codex 内部 auth.json。
- 提取 Codex OAuth Token。
- 读取进程内存。
- 复制内部 Refresh Token。
- 模拟 Codex 内部认证协议。
- 绕过账号验证。

如果无法直接复用 Codex 登录状态：

暂停开发并向用户报告。

列出官方支持的替代方案：

A. 使用 Codex App Server 官方账号登录流程。

B. 使用官方 Sign in with ChatGPT 集成流程。

C. 使用用户明确授权的 OpenAI API Key（单独 API 计费）。

未经用户确认，不得自动选择 B 或 C。

不允许将 ChatGPT 订阅和 API Key 计费混为一谈。

## 6.2 App Server 通信

优先使用本机 App Server 子进程和 stdio JSON-RPC。

要求：

- 正确初始化。
- 维护单一消息读取循环。
- 处理请求响应和通知。
- 正确处理进程退出。
- 支持重连。
- 支持超时。
- 支持取消。
- 不在日志中输出认证数据。

如果 App Server 支持当前账号的 ChatGPT 计划授权，优先使用该授权。

## 6.3 旁注与线程映射

每个旁注对应一个独立 AI Thread。

数据关系：

annotation_id -> thread_id

同一个旁注的连续追问使用同一个 thread。

不同旁注使用不同 thread。

用户切换账号后，不得将旧账号的旁注历史混入新账号。

## 6.4 AI 上下文

只发送：

- 用户选中的文字。
- 用户明确提出的问题。
- 当前旁注自己的历史。

如果能够可靠获得选区附近上下文，可以作为可选内容。

禁止自动上传整个 Codex 会话。

禁止自动读取项目文件。

禁止在没有用户授权的情况下调用 Shell、MCP、文件修改或其他工具。

此产品定位是阅读理解和上下文追问，不是代码执行 Agent。

---

# 七、旁注数据管理

每个旁注保存：

- annotation_id
- selected_text
- anchor_rect
- codex_window_id
- app_server_thread_id
- account_id
- conversation_history
- created_at
- updated_at

要求：

1. 不同旁注历史独立。
2. 账号隔离。
3. 支持关闭和删除。
4. 支持恢复必要的本地历史。
5. 不记录选中文字到普通诊断日志。
6. 不保存不必要的 Codex 全文。
7. 不将认证凭证保存在旁注数据库中。

---

# 八、开发阶段

必须分阶段开发。

每个阶段完成后运行测试、更新报告并暂停，等待用户验收。

不允许一次性开发所有阶段。

## Phase 0：需求和技术可行性调查

只读检查环境和公开接口。

任务：

1. 检查 Windows 版本。
2. 检查 Codex 客户端版本。
3. 检查 Codex 进程和窗口结构。
4. 检查 .NET SDK。
5. 检查 UI Automation 可访问性。
6. 检查 TextPattern。
7. 检查选区文本获取。
8. 检查选区坐标获取。
9. 检查选区变化事件。
10. 检查 Codex App Server。
11. 检查账号复用可行性。
12. 检查是否存在官方 UI 扩展接口。

生成：

docs/PHASE-0-FEASIBILITY.md

每项标记：

PASS / FAIL / PARTIAL / NOT TESTED

如果无法获取真实选区，不能继续声称可以实现目标。

本阶段不修改旧项目。

## Phase 1：选区捕获原型

只实现：

- Codex 窗口识别。
- 选区检测。
- 选中文字获取。
- 选区矩形获取。
- 选区变化检测。

不开发 AI。

不开发完整旁注。

必须在真实 Codex 页面测试。

生成：

docs/PHASE-1-SELECTION-ACCEPTANCE.md

## Phase 2：原生风格浮动按钮

实现：

- 选区旁自动出现按钮。
- 按钮跟随选区位置。
- 选区取消后消失。
- Codex 失焦后消失。
- 不影响 Codex 操作。

按钮文字：

向 ChatGPT 提问

不接入 AI。

生成：

docs/PHASE-2-OVERLAY-ACCEPTANCE.md

## Phase 3：直接浮动旁注

点击按钮后：

- 立即创建旁注。
- 自动关联原文。
- 不出现预览确认。
- 输入框自动获得焦点。
- 支持多个独立旁注。
- 支持拖动、折叠、关闭。

不要求快捷键。

不要求手动复制。

生成：

docs/PHASE-3-ANNOTATION-ACCEPTANCE.md

## Phase 4：Codex App Server 接入

实现：

- App Server 启动与连接。
- 初始化。
- 账号状态查询。
- 模型目录。
- 真实模型请求。
- 流式回答。
- 取消生成。
- 独立 Thread。
- 连续追问。

首先验证真实账号认证。

如果需要独立授权，暂停并等待用户决定。

生成：

docs/PHASE-4-APPSERVER-ACCEPTANCE.md

## Phase 5：完整功能

实现：

- 多旁注。
- 历史管理。
- 账号隔离。
- 选区锚点管理。
- 旁注位置管理。
- 错误恢复。
- 服务重启恢复。
- 用户设置。

不增加独立聊天主界面。

## Phase 6：稳定性、安全和发布

测试：

- 中文文本。
- 英文文本。
- 多行代码。
- Markdown 列表。
- 长回答。
- 快速连续选区。
- 页面滚动。
- Codex 窗口失焦。
- Codex 窗口移动。
- 多显示器。
- DPI 缩放。
- 多旁注。
- SSE 中断。
- App Server 崩溃。
- 网络异常。
- 账号切换。
- 进程重启。

完成安全审计和安装包制作。

生成：

docs/FINAL-ACCEPTANCE.md

README.md

CHANGELOG.md

---

# 九、视觉和交互设计原则

最终产品不能看起来像一个独立桌面软件。

视觉要求：

- 与 Codex 界面保持协调。
- 按钮小巧。
- 旁注紧凑。
- 不使用大面积工具栏。
- 不出现常驻侧边栏。
- 不出现独立聊天主界面。
- 不出现不必要的设置窗口。

旁注应优先展示 AI 内容，而不是复杂设置。

用户主要操作路径：

选中文字 -> 点击提问 -> 输入问题 -> 查看回答。

尽可能减少交互步骤。

---

# 十、安全要求

1. 不注入 Codex 进程。
2. 不修改 Codex 安装文件。
3. 不读取进程内存。
4. 不提取内部 Token。
5. 不读取未授权的会话内容。
6. 不持续监控剪贴板。
7. 不上传整个 Codex 对话。
8. 不在日志中输出用户选中文字。
9. 不自动使用 API Key 计费。
10. 不自动触发额外 OAuth 授权。
11. 只监听本地必要的通信。
12. 不向其他设备暴露本地服务。

---

# 十一、Git 和项目管理

创建全新工程：

<repo-root>

如果该目录已经存在：

先检查内容，不得直接删除或覆盖。

旧项目只允许只读参考：

<local-workspace>\chatgpt-context-notes

<local-workspace>\chatgpt-context-notes-codex-overlay

不得修改旧项目。

新项目独立初始化 Git。

每个 Phase 使用独立 Commit。

不创建 GitHub 远程。

不推送。

不创建正式版本 Tag。

不得强制重置或删除已有历史。

---

# 十二、执行纪律

本项目必须遵守以下工作流程：

1. 先理解需求。
2. 检查开发环境。
3. 阅读当前阶段需要的官方技术文档。
4. 进行可行性验证。
5. 输出阶段计划。
6. 等待用户确认后进行代码修改。
7. 完成测试。
8. 生成验收报告。
9. 提交独立 Commit。
10. 暂停等待用户验收。

不能把模拟环境测试当成真实 Codex 测试。

不能把窗口显示成功当成选区捕获成功。

不能把模型列表加载成功当成真实推理成功。

不能把 HTTP 200 当成完整 AI 任务成功。

不能将未经测试的功能标记为 PASS。

遇到不确定的官方接口，必须检查当前版本协议和官方文档，不得编造接口字段或方法。

每个阶段必须报告：

- 已完成内容。
- 修改的文件。
- 测试结果。
- 未完成事项。
- 已知风险。
- Git Commit。
- 下一阶段计划。

---

# 十三、最终验收标准

只有在真实 Codex 桌面客户端中完成以下全部操作，才算项目成功：

1. 启动 Codex。
2. 后台 Companion 自动运行。
3. 屏幕上只有 Codex 页面。
4. 鼠标选中一段回答。
5. 选区旁自动出现「向 ChatGPT 提问」按钮。
6. 不需要 Ctrl+C。
7. 不需要快捷键。
8. 点击按钮后直接出现旁注。
9. 不需要文字预览确认。
10. 输入框自动获得焦点。
11. 使用真实授权账号发送问题。
12. AI 实时流式回答。
13. 可以连续追问。
14. 可以同时创建多个独立旁注。
15. 各旁注历史互不干扰。
16. 关闭旁注后恢复纯 Codex 页面。
17. 不依赖 Edge 扩展。
18. 不需要 Edge 扩展授权。
19. 不需要用户手动加载模型。
20. 不需要用户打开额外的桌面主窗口。

最终产品必须以真实使用体验为验收依据，而不是仅以代码测试通过为依据。

---

# 十四、本次启动任务

现在正式启动全新项目。

第一轮只执行 Phase 0。

具体要求：

1. 检查本机环境。
2. 检查 Codex 版本。
3. 阅读 Codex App Server 当前官方文档。
4. 检查 Windows UI Automation 对 Codex 的真实支持。
5. 研究如何获取真实选区文本和屏幕矩形。
6. 验证 App Server 账号复用条件。
7. 评估是否能完全不依赖 Edge。
8. 生成 Phase 0 报告。

不要直接开始完整开发。

不要修改旧项目。

不要清除任何已有凭证。

不要重新授权。

不要重启当前正在使用的 Codex 或旧 Companion。

完成 Phase 0 后暂停，等待用户确认 Phase 1。