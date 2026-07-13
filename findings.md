# 发现与决策

## 需求

- 当前只完成 OpenGameMate Phase 0 技术可行性验证，不构建完整 v0.1.0。
- 交付最小可运行 PoC、启动方式、测试步骤、可行性报告和下一阶段建议。
- 第 5 项“后台 WebView2 无焦点添加图片和文字”优先级最高；失败必须如实报告并停止完整应用建设。
- 不读取 ChatGPT 回复、历史、Cookie、登录令牌或账号信息。
- 每项验证都需记录成功、失败、未验证和限制条件。

## 研究发现

- 仓库初始只有 `.git`，尚无代码或仓库内开发文档。
- 用户提供的权威文档位于 `OpenGameMate_v0.1.0 development document (external attachment)`。
- 开发文档技术基线为 C# / .NET 8 / WPF / WebView2 / Windows Graphics Capture。
- 文档将 Voice 会话图片输入和后台/隐藏 WebView2 无焦点提交列为 Go / No-Go 发布阻断项。
- 页面适配允许检查输入区、上传状态和错误，但禁止读取回复、历史、账号或凭据。
- 主显示器截图必须限制为不超过 1920×1080，并且临时文件需要稳定清理。
- 当前记忆库没有 OpenGameMate、WebView2、WPF 或本项目 Phase 0 的既有记录可复用。
- 本机 .NET Host/Windows Desktop Runtime 为 8.0.0，但 `dotnet --info` 显示没有安装任何 SDK，当前无法编译项目。
- Visual Studio Community 2022 为 17.4.4；需要进一步确认其组件，但该版本早于 .NET 8 正式支持所需工具链。
- 常见注册表位置未发现 WebView2 Evergreen Runtime 条目，需要继续检查磁盘安装位置或由 PoC 初始化结果确认。
- OpenAI Developer Docs MCP 未配置，自动添加命令因本机 `codex` 启动路径错误而失败；后续资料核对限制为 OpenAI 官方域名。
- 磁盘检查确认 WebView2 Evergreen Runtime 131.0.2903.63 位于 `C:\Program Files (x86)\Microsoft\EdgeWebView\Application\`。
- OpenAI 官方当前说明：Voice 可由登录用户在桌面网页 `chatgpt.com` 使用，首次使用需要允许浏览器访问麦克风；Voice/Live 中的文本与图片能力仍可能受账号、套餐、地区和会话影响。
- OpenAI 官方当前说明：ChatGPT 网页支持静态 PNG/JPEG/非动画 GIF 图片输入；文件上传存在按套餐变化的限制，失败上传也可能计入上传频率限制。
- Microsoft 官方说明：`dotnet-install.ps1` 可以把 SDK 安装到指定目录；省略 `-Runtime` 即安装 SDK，适合 CI/非管理员隔离安装场景。
- Microsoft 官方 WebView2 文档建议 WPF/WinForms 通过 `CoreWebView2Environment.CreateAsync` 指定可写的自定义 UDF；应用负责该目录生命周期。
- Microsoft 官方 API 支持 `CoreWebView2.CallDevToolsProtocolMethodAsync`，CDP 调用必须按顺序 `await`，因为并发调用的处理顺序不保证。
- Microsoft 官方 Win32 API 提供 `IGraphicsCaptureItemInterop.CreateForMonitor`；该互操作接口最低要求 Windows 10 1903，本机 Windows 11 22631 满足系统要求。

## 技术决策

| 决策 | 理由 |
|------|------|
| 真实登录和 Voice 权限由用户手动操作 | 避免接触凭据并满足浏览器权限的真实验证要求 |
| 后台提交实验使用无隐私测试图片和固定测试文字 | 降低误发与隐私风险，便于复现 |
| 把前台窗口与鼠标坐标作为第 5 项证据 | 直接验证“不移动鼠标、不抢焦点” |
| 将输入区/附件状态作为唯一网页侧证据 | 可验证动作，又不读取 ChatGPT 回复正文 |
| 第 5 项失败后仅完成独立 Phase 0 证据和替代方案评估 | 遵守用户的停止条件 |
| 使用仓库内 `.dotnet/` 隔离安装 .NET 8 SDK | 系统无 SDK；避免修改系统 PATH 或覆盖现有运行时 |

## 遇到的问题

| 问题 | 解决方案 |
|------|---------|
| 用户描述的仓库内 `docs/...` 文件不存在 | 使用用户明确附带的 Downloads 文件作为当前权威来源，并在计划中标明 |
| 初始记忆搜索无匹配导致组合命令返回非零 | 已识别为 `rg` 的正常“无匹配”状态，不重复同一失败方式 |
| 无 .NET SDK | 先检查 Visual Studio 组件与其他 SDK 路径；若仍缺失，使用隔离的 .NET 8 SDK 方案 |
| OpenAI Developer Docs MCP 添加失败 | 不重复原命令，使用官方网页回退并记录链接 |
| PowerShell 获取 .NET 发布元数据发生 TLS 发送错误 | 改用 `curl.exe`；仍只信任 Microsoft 官方元数据与哈希 |

## 资源

- `OpenGameMate_v0.1.0 development document (external attachment)`
- 仓库级 `AGENTS.md`
- OpenAI Voice Mode FAQ：`https://help.openai.com/en/articles/8400625-voice-chat-faq`
- OpenAI ChatGPT Voice：`https://help.openai.com/en/articles/20001274/`
- OpenAI ChatGPT Image Inputs FAQ：`https://help.openai.com/en/articles/8400551-image-inputs-for-chatgpt-faq`
- OpenAI File Uploads FAQ：`https://help.openai.com/en/articles/8555545-uploading-images-and-files-in-chatgpt`
- Microsoft .NET install scripts：`https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script`
- Microsoft WebView2 User Data Folder：`https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder`
- Microsoft CoreWebView2 CDP API：`https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.calldevtoolsprotocolmethodasync`
- Microsoft IGraphicsCaptureItemInterop：`https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nn-windows-graphics-capture-interop-igraphicscaptureiteminterop`

## 视觉/浏览器发现

- OpenAI 官方当前说明：已登录用户可在 `chatgpt.com` 桌面网页使用 Voice；首次使用时浏览器可能请求麦克风权限，用户可在 Voice 界面静音/取消静音并退出会话。
- Microsoft WebView2 官方建议为应用指定可写的自定义用户数据目录；该目录保存浏览器配置、权限及会话数据，因此 v0.1 必须持久保留目录但不得由应用读取 Cookie 或令牌。
- 一个 WebView2 Environment 与一个用户数据目录和一组浏览器进程关联；关闭会话时需先释放控件和事件订阅，若未来清理目录还必须等待相关浏览器进程退出。
- Phase 0 的“任意 HTTPS”只用于登录链故障排查，不可进入正式 v0.1 运行路径；正式策略应默认拒绝未知来源，只允许 OpenAI 官方来源和明确列出的身份提供商来源。

- WebView2 真实窗口已经启动，标题为 `OpenGameMate Phase 0 - ChatGPT`。
- 隔离 UDF 的首次官方页面访问出现 Cloudflare“验证您是真人”；该步骤必须由用户本人完成，不能自动绕过。
- 尚未执行 ChatGPT 登录、Voice 麦克风和真实第 5/6 项实验。
- 已完成一次官方网页资料搜索；网页内容只作为能力边界参考，不能替代本机账号实测。

## 本机运行证据

- 独立 UDF：`%LocalAppData%\OpenGameMate\Phase0\UserData` 已创建。
- 主显示器：2560×1600；输出：1728×1080；PNG：978,113 字节。
- JSONL 已记录 `primary-display-capture=Passed` 和 `webview2-initialization=Passed`。
- 首版 WebView2 初始化挂起的原因是 WPF 宿主尚未显示、HWND 未建立；先显示窗口后修复。
- 系统 WebView2 131.0.2903.63 因 `edgeupdate` 被禁用而长期未更新；ChatGPT 登录提供商在该版本中持续加载。
- 没有 EdgeUpdate 组策略要求禁用更新；恢复服务需要管理员权限，当前会话未获授权。
- 隔离 WebView2 Fixed Runtime 150.0.4078.65 的 CAB 和 `msedgewebview2.exe` 均通过 Microsoft Authenticode 签名校验。
- 使用 Fixed Runtime 150 后，同一登录弹窗在 5 秒内完整出现 Google、Apple、手机号、邮箱和继续按钮，故障已复现并修复。
- 用户点击邮箱“继续”后，ChatGPT 导航到微软官方身份端点 `login.microsoftonline.com`；旧导航守卫按“仅 OpenAI 域名”规则取消该请求，表现为按钮持续加载。
- OpenAI SSO 文档说明登录可能转交外部身份提供商；Microsoft 身份平台文档将 `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize` 列为官方授权端点。
- 用户已明确授权最小例外：仅精确 HTTPS 来源 `login.microsoftonline.com`、默认 443 端口、首次跳转人工确认、仅本次运行有效。HTTP、子域名、伪造后缀和非默认端口仍失败关闭。
- 新版 PoC 已通过 21/21 测试并实际重新打开 ChatGPT 首页；外部身份提供商弹窗与最终登录仍待用户本人触发和验证。
- 用户实测确认微软弹窗能够出现并点击同意，但随后未进入登录页。主机级日志表明第一跳 `login.microsoftonline.com` 已获准，紧接着的官方 Microsoft Account 主机 `login.live.com` 被守卫取消，结果为 `OperationCanceled`。
- Google 登录的主机级日志表明 `accounts.google.com` 被守卫取消，结果同为 `OperationCanceled`；不是按钮或 Fixed Runtime 再次失效。
- Microsoft 官方 MSA 参考同时列出 `login.microsoftonline.com` 与 `login.live.com`；Google 官方 OpenID Connect 参考将 `https://accounts.google.com/o/oauth2/v2/auth` 列为授权端点。新增来源仍需用户明确授权。
- 用户随后明确要求暂不实施域名限制，优先完成 Phase 0 可行性验证。运行代码已切换为临时 HTTPS 导航诊断模式：任意 HTTPS 主机可导航，无效 URI、HTTP、FTP 和本地文件 URI 仍被阻断。
- 诊断模式不改变隐私边界：日志仍只记录主机和协议；不读取路径、查询参数、表单、Cookie、Token、账号、聊天记录或回复。
- HTTPS 诊断版构建为 0 警告、0 错误，测试 24/24；真实 PoC 已重新启动并在 Fixed Runtime 150 中加载 ChatGPT 首页，等待用户本人完成登录。
- 用户人工确认已登录。第 5 项第一次有效离开 OpenGameMate 前台后，日志记录 `Unhandled failure: KeyNotFoundException`；图片和文字均未出现。
- 截图与代码核对确认 ChatGPT 自动隐藏是实验设计，不是故障：隐藏后才能验证后台无焦点；实验后必须由用户手动显示，避免 PoC 抢回焦点。
- `KeyNotFoundException` 根因位于 `ChatGptWebAdapter.EvaluateAsync`：标准 `Runtime.evaluate` 响应的值位于顶层 `result.value`，旧代码错误读取 `result.result.value`。该错误发生在任何文件输入或文字修改之前，不能作为网页能力失败证据。
- 用户在 2026-07-14 使用修复后的版本完成两次后台倒计时实验；日志均显示 `focusAndCursorStable=True`，但 `fileSelected=False`、`previewDetected=False`、`textInserted=False`、`code=file-input-count`。这证明窗口/鼠标约束已满足，当前阻断在网页上传入口适配。
- 现有适配器假定页面初始 DOM 中恰好有一个可用 `input[type=file]`。当前 ChatGPT 页面不满足该假定；PoC 将保留文件输入路径，并增加只向 `#prompt-textarea` 派发带本地测试 PNG 的粘贴事件作为窄范围回退，不打开原生文件选择器、不读取页面内容。
- 2026-07-14 00:31 用户真实复测证明粘贴回退可行：图片预览与固定文字同时出现在 ChatGPT 输入区，日志为 `previewDetected=True`、`textInserted=True`、`focusAndCursorStable=True`，路径为 `composer-paste`。第 5 项现为单次通过；第 6 项提交仍未验证。
- 2026-07-14 00:35 第 6 项真实提交通过：日志为 `triggerInvoked=True`、`composerCleared=True`、`attachmentCleared=True`、`focusAndCursorStable=True`、`code=ok`，用户截图显示固定测试消息与图片进入对话。测试提示明确要求不回复，因此没有助手回复不影响提交结论；PoC 也不得读取回复正文。
- 用户人工确认 ChatGPT 网页 Voice 麦克风可正常工作，因此验证项 3 的麦克风部分通过。
- OpenAI 官方 ChatGPT Voice 说明要求使用 Voice 界面的退出控件结束语音；麦克风按钮只负责静音/取消静音。PoC 的 `BrowserWindow.Window_Closing` 原先取消窗口关闭并调用 `Hide()`，导致右上角关闭看似生效但 WebView2 与 Voice 会话仍存活。
- Phase 0 的窄范围修复是：隐藏仍仅用于后台实验并明确提示不会结束 Voice；用户关闭 ChatGPT 子窗口或点击主界面“关闭 ChatGPT（结束语音）”时，真正关闭窗口、Dispose WebView2，并允许使用相同独立 UDF 重新初始化。

---
*每执行2次查看/浏览器/搜索操作后更新此文件*

## v0.1.0 集成结论（2026-07-14）

- WPF 组合必须把自动调度回调重新派发到 WPF Dispatcher；否则 WebView2 CDP 调用可能发生在线程错误的上下文中。当前组合根已显式派发。
- 首次启动只显示主界面并读取设置；浏览器、Voice、角色文字、整屏捕获、真实发送和诊断导出均由用户动作触发，异常重启不恢复运行态。
- 上传额度可能在图片准备阶段而非提交阶段出现；适配器现同时在准备/提交允许状态中检查受限错误选择器，并优先映射到 `VoiceOnly`。
- 远程规则验证链已实现，但仓库没有官方 GitHub remote 和维护者公钥；安全行为是 `RemoteDisabled`、不发请求、回退内置规则。客户端仓库不得生成或保存签名私钥。
- 开发文档的一键清除 WebView2 数据与仓库禁止批量删除目录规则冲突；产品提供准确目录和“完全退出后手动删除”的说明，不递归删除。
- 本机未安装 Inno Setup 6；便携发布成功，安装器仅交付可复现定义，不能标记为已生成安装包。
*防止视觉信息丢失*

## v0.1.0 实施决策

- Phase 0 已以提交 `377a506` 冻结，后续真实网页能力结论继续保留为回归基线。
- 开发文档要求的状态集合固定为：`Idle`、`BrowserReady`、`Ready`、`Running`、`Sending`、`Paused`、`VoiceOnly`、`AdapterError`、`Stopped`。
- 自动截图周期固定为 2 分钟，v0.1.0 不提供修改设置；同一时间最多一个发送任务，手动优先，普通失败不立即重试。
- 配置系统必须同时支持 `%LocalAppData%\OpenGameMate\` 安装模式和 `OpenGameMate-Portable\data\` 便携模式，并严格验证 JSON Schema/字段/大小。
- 诊断日志使用允许字段模型，不接受自由文本网页内容或任意路径，从结构上降低写入回复、账号、Token 或截图内容的风险。
- 首个正式阶段只实现公共契约、配置、诊断和状态机；浏览器、截图、调度、网页适配及 WPF 产品功能保持未修改。
- 配置文件最大 64 KiB，使用严格 camelCase JSON、字符串枚举、未知字段拒绝和 SchemaVersion=1；自动截图周期刻意不进入设置模型。
- 诊断事件最大 8 KiB，按 UTC 日期写 JSONL；模型只有固定布尔/数值/状态/代码字段，不存在任意 `message`、`detail` 或 `path` 字段。
- 状态机的无效触发不会改变状态；额度错误仅允许从 `Sending` 进入 `VoiceOnly`，适配恢复必须由 `AdapterRecovered` 显式进入 `BrowserReady`。
