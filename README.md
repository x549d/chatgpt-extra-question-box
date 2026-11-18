# ChatGPT Context Notes Native

在 **Windows 版 Codex 桌面客户端的 Chat 模式**中实现“划词追问”：选中助手回答里的一段文字，选区旁出现一个小按钮「提问」；点击后在选区旁直接弹出一个小型提问框，自动引用选中文字，可以立即针对这段内容提问。不需要复制文字、切换窗口、打开 Edge，也没有独立的聊天界面。

完整需求见 [docs/PROJECT-SPEC.md](docs/PROJECT-SPEC.md)。

## 技术路线

- .NET 10 + WPF + Windows UI Automation（UIA）。
- UIA 读取真实 Codex 窗口里的选区文字和屏幕矩形，能保留连续空格、缩进和换行，因此**不使用剪贴板**。
- 按钮是一个不抢焦点的透明 WPF 浮层（Overlay），视觉上贴合 Chat。不注入 Codex 进程，不修改安装文件，不读取进程内存。
- UIA 调用放在可超时、可重启的独立工作进程中（两秒看门狗），WPF 界面线程不做 UIA 查询。
- 以 Codex **本地插件**形式安装，可由 `SessionStart` 拉起后台。Windows 启动监视器覆盖冷启动后直接打开已有 Chat 的场景。
- AI 后端（Phase 4）计划复用本机 Codex App Server 的现有 ChatGPT 登录，不依赖 Edge 和浏览器扩展。

## 阶段进度

| 阶段 | 内容 | 状态 | Commit / 报告 |
| --- | --- | --- | --- |
| Phase 0 | 可行性调查 | 完成 | `733bdfb` · [PHASE-0-FEASIBILITY.md](docs/PHASE-0-FEASIBILITY.md) |
| Phase 1 | 真实选区捕获 | 完成 | `f606855` · [PHASE-1-SELECTION-ACCEPTANCE.md](docs/PHASE-1-SELECTION-ACCEPTANCE.md) |
| Phase 2 | 选区旁浮动按钮（只验收了 Codex 模式） | 完成 | `53bf7c3` · [PHASE-2-OVERLAY-ACCEPTANCE.md](docs/PHASE-2-OVERLAY-ACCEPTANCE.md) |
| **Phase 2R** | **Codex Chat 模式支持 + 常驻 + 插件化** | **PASS，真实冷启动后自动拉起并显示提问入口** | [PHASE-2R-CHAT-ACCEPTANCE.md](docs/PHASE-2R-CHAT-ACCEPTANCE.md) |
| Phase 3 | 按钮 → 小型提问框（自动引用原文、自动聚焦） | 未开始 | — |
| Phase 4 | 接入本机 ChatGPT 登录 / App Server | 未开始 | — |
| Phase 5 | 流式输出、连续追问、多旁注、上下文隔离 | 未开始 | — |

另有存档分支 `wip/phase-2r-attempt`（`8bd3f77`）：上一位开发者未提交、也未验收的 Phase 2R 尝试，只作参考。

## Phase 2R 当前进度（2026-11-18）

### 诊断结论

- **Chat 模式失效的原因**：`53bf7c3` 先判断模式再读选区。模式判断只认名字为 `切换模式，当前模式：Codex` 的按钮；Chat 模式下按钮名是 `…当前模式：ChatGPT`，于是每次都直接返回 `codex-mode-unconfirmed`。而且找不到按钮时，每 180 ms 就扫描一次整棵 UI 树。
- **185 秒后自动退出**：这是 Phase 2 验收命令带的 `--seconds 185` 造成的。另外，正式产品所需的单实例、自启动和崩溃恢复当时也都没有。

### 已实现（自测 55/55 PASS，Hook 诊断 9/9 PASS）

| 项 | 实现 |
| --- | --- |
| 只支持 Chat | 模式按钮名为 `…当前模式：ChatGPT` 才工作；Codex 模式记为 `mode_codex` 并隐藏。按钮元素会缓存，每次采样只读取一次名字，找不到时最多每 3 秒重新查找一次 |
| 角色识别 | 检查选区所属的正文容器、滚动区和桌面主内容层级，再按 UIA 文本范围寻找最近的前置隐藏角色标题。选区必须留在同一条消息内。遇到 Edit 判为 Composer，遇到导航区判为 Sidebar；Assistant 显示，User / Composer / Sidebar / Unknown 隐藏 |
| 性能 | 采样路径上没有全树搜索。父级属性用 CacheRequest 批量读取；输入框元素会缓存，失效后按节流重新查找 |
| 可视区 | 以消息外层可滚动容器为可视区，跳过离屏编辑器，并扣掉当前输入框区域 |
| 按钮定位 | 右侧（与首行顶部对齐）→ 左侧 → 下方 → 上方；过滤 1×2 等异常矩形；不盖住选区，不超出可视区和屏幕工作区 |
| 按钮外观 | 小胶囊「提问」（矢量气泡图标）；不抢焦点（`WS_EX_NOACTIVATE` / `MA_NOACTIVATE`） |
| 常驻 | `--companion`：每个用户会话只有一个实例（命名 Mutex）。监控循环出错会原地重建；浮层线程的未处理异常只会隐藏按钮。`--stop` 退出；Codex 关闭 60 秒后自动退出 |
| 插件 | `codex-plugin/` 是本地 marketplace。`SessionStart` 通过 `hooks/launch.cmd` 启动后台。启动器不向 stdout 写内容，由 ShellExecute 启动子进程，并在有界等待内检查就绪状态 |
| Windows 补充启动 | Windows 登录时运行一个当前用户监视器。Codex 窗口进入前台后，监视器验证程序包身份并启动后台。监视器和 Hook 共用启动锁，退出 Codex 后 Companion 按原有超时机制退出 |
| 诊断 | `--inspect`：只读输出 Chat 的 UIA 结构（控件类型、类名、矩形、长度、角色标题），**不输出正文** |

### 已在本机完成的安装

- 已通过官方 CLI 注册本地插件源 `context-notes-native`（指向本仓库 `codex-plugin/`），并安装、启用了 `context-notes@context-notes-native`（插件缓存目录：`~\.codex\plugins\cache\context-notes-native\context-notes\0.2.0\`）。
- 已停止旧实验留下的常驻进程，删除了旧的开始菜单快捷方式。

### 真实桌面进度

本机插件 `0.2.0` 已部署并确认生产 Hook 受信任。诊断实测收到 `resume`，打开或切换已有 Chat 时没有观察到新的派发。最终冷启动由 Windows 监视器自动拉起后台，随后真实划选、按钮点击和前台保留通过。[验收报告](docs/PHASE-2R-CHAT-ACCEPTANCE.md) [SessionStart 专项](docs/SESSIONSTART-HOOK-DIAGNOSTICS.md)

### 已知风险

- 角色标题、模式按钮名、类名等都来自 Codex 当前版本（26.1111.2228.0，简体中文界面）的可访问性结构，客户端更新后需要复测。英文界面的名称已加入匹配规则，但没有实测。
- 插件文件更新后，Codex 可能要求重新信任 hook。
- 后台需要与 Codex 处于同一交互桌面。`launch.log` 区分启动来源与结果，`companion.log` 记录后台状态，`desktop-startup.log` 记录 Windows 监视器状态。
- Windows 启动快捷方式绑定已安装插件的版本路径。升级插件后重新运行启动配置脚本以刷新目标。使用 `-Disable` 可注销该快捷方式并停止监视器。

## 目录结构

```text
docs/                       需求规范与各阶段验收报告
src/SelectionProbe/         .NET 10 WPF 后台进程（选区采样、浮层、诊断）
  UiaSelection.cs           Chat 模式判断、角色识别、可视区
  UiaSelection.Inspect.cs   只读结构诊断（--inspect）
  ProbeMonitor.cs           前台 / 窗口 / 鼠标状态与采样调度
  ProbeWorker.cs            可超时的 UIA 工作进程
  OverlayHost.cs            WPF 浮层线程与按钮显示/隐藏
  OverlayPlacement.cs       按钮定位与异常矩形过滤
  Companion.cs              常驻、单实例、--launch / --stop / --status
  Diagnostics.cs            只记录元数据的日志
  DesktopStartup.cs         当前用户的 Codex 前台启动监视器
codex-plugin/               Codex 本地 marketplace 与插件（bin/ 由安装脚本生成，不入库）
scripts/Install-Plugin.ps1  发布程序到插件并注册 marketplace
scripts/Set-WindowsStartup.ps1  注册或注销当前用户的 Windows 启动补充路径
scripts/Test-HookDiagnostic.ps1  独立 Hook 诊断回归
scripts/Uninstall-Plugin.ps1
```

## 常用命令

```powershell
# 构建
dotnet build src/SelectionProbe/SelectionProbe.csproj -c Release -p:NuGetAudit=false
# 脱机自测（在无交互桌面的环境里用 dotnet 宿主运行）
dotnet src/SelectionProbe/bin/Release/net10.0-windows/SelectionProbe.dll --self-test
# 构建插件并注册本地 marketplace（之后在 Codex 中安装并信任 hook）
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-Plugin.ps1
# 安装包含监视器的插件版本后，启用当前用户的 Windows 启动补充路径
.\scripts\Set-WindowsStartup.ps1
# 移除启动项并停止监视器
.\scripts\Set-WindowsStartup.ps1 -Disable
# 独立 Hook 诊断测试
.\scripts\Test-HookDiagnostic.ps1
# 后台进程状态 / 停止
SelectionProbe.exe --status
SelectionProbe.exe --stop
```

## 安全约束

不注入 Codex 进程，不修改其安装文件，不读取进程内存、Cookie、密码或 Token。日志只记录状态、角色、矩形数量与坐标、长度、耗时和错误类型，**不记录选中文字**。测试时不在真实 Chat 中输入或发送内容。
