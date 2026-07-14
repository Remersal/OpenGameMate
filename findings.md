# 发现与决策

## 2026-07-14 上传前隐私清理

- 泄露路径分为三层：当前 Markdown 文档、旧 Git blob、C# 编译产物中的调试文档路径；只改当前文件不能清除后两层。
- 当前文档共有 5 处明确的用户目录或仓库绝对路径；旧历史至少由提交 `377a506`、`6a37197`、`4ed6c6e` 引入相关路径。
- 旧发布目录中的多个 OpenGameMate DLL/PDB 可直接检出本机用户名，证明需要在编译器边界配置确定性的虚拟源码前缀。
- 采用仓库根级 `PathMap`：将仓库根目录映射到 `/_/`，保留可诊断的仓库相对源文件结构，同时不泄露盘符、用户名或构建机目录。
- 历史重写后还必须过期 reflog 并立即清理不可达对象，否则直接复制整个 `.git` 目录时旧 blob 仍可能残留。
- 实测带调试信息的库 DLL 将 CodeView 路径写为 `/_/src/<project>/obj/.../<project>.pdb`，本机路径扫描为零；正式发布 DLL 使用 `DebugType=None`，重编译后不含 PDB 路径。
- 发布脚本在便携版复制文档后、安装器编译后分别执行同一个隐私验证器；旧泄露产物会失败，新预检便携版与安装包均通过。

## 2026-07-14 首次安装账号风险提示

- 风险提示必须明确区分“产品技术边界”和“平台执法风险”：不注入、不读游戏内存不能推导出零封号风险。
- 使用 Inno 自定义必选确认页；首次安装未勾选时不能继续，首次静默安装也失败关闭。
- 接受状态仅通过 Inno `RegisterPreviousData` 持久化；同一安装的后续升级不重复提示，卸载后重新安装会再次提示。
- 旧版本升级因尚无接受记录，会显示一次风险提示，之后才按已确认升级处理。
- 首次静默安装在没有历史接受记录时以退出码 7 终止，且没有创建目标安装目录，确认静默参数不能绕过风险提示。
- 交互页面和升级免重复提示没有通过自动鼠标操作验证；当前证据为 Inno 编译成功、官方 PreviousData 语义与脚本静态核对，发布前仍应人工点选一次。

## 2026-07-14 安装器简体中文支持

- 旧安装器未声明 `[Languages]`，因此 Inno Setup 只使用英文 `Default.isl`；应用本身的中英文界面不等于安装向导自动本地化。
- 官方 Inno Setup 仓库提供的 `ChineseSimplified.isl` 标明兼容 6.5.0+，本轮固定使用提交 `683ee7eabfbce807f901c5da83fc5ff1a3ecb693` 的文件，SHA-256 为 `6753BE2C5E2740D859900FD902824DB2EC568DA5C5B52486524C9762D778B0B0`。
- 安装器同时声明简体中文和英文；Inno Setup 按 Windows UI 语言自动选择，用户也可在启动安装器时手动选择。
- 桌面快捷方式和安装后启动文字改用 Inno 标准 `{cm:...}` 资源，避免主向导中文但自定义项目仍显示英文。
- 中文安装器已在全新目录编译成功；编译日志确认同时读取仓库内 `ChineseSimplified.isl` 和编译器 `Default.isl`。

## 2026-07-14 正式图标与安装版

- 用户从四款生成方案中选择第二款：显示器轮廓、对话气泡和方向键组合，暖白背景、深蓝与低饱和青绿/蓝色。
- 选定源图来自用户确认的第二款图标方案；仓库只保留正式的 `assets/OpenGameMate.AppIcon.png`，不记录本机生成目录。
- 当前 WPF 项目没有 `ApplicationIcon`，Inno Setup 也没有 `SetupIconFile`；现有卸载图标和快捷方式会跟随 EXE，因此需要把 ICO 编译进应用并显式用于安装器。
- 发布脚本会拒绝非空便携版或安装器输出目录，且不执行递归删除；本轮必须使用新的版本化目录。
- 正式源图已保存为 `assets/OpenGameMate.AppIcon.png`；ICO 包含 16、20、24、32、40、48、64、128、256 九个尺寸层级。
- 图标已接入 `ApplicationIcon`、托盘关联 EXE 图标和 Inno `SetupIconFile`；快捷方式与卸载项继续从带图标的 EXE 获取图标。
- Debug 构建 0 警告/0 错误，127/127 测试、全解决方案格式和发布元数据验证通过。
- 官方 Inno Setup 6.7.3 安装程序取自其不可变 GitHub Release；Windows Authenticode 校验为 `Valid`，发布者为 `Pyrsys B.V.`，随后以当前用户范围安装编译器。
- Release 构建 0 警告/0 错误，127/127 测试、全解决方案格式与发布元数据验证通过；应用 EXE 和安装器均可提取到所选图标。
- 最终安装器为 `artifacts/installer/v0.1.0-final/OpenGameMate-Setup-0.1.0.exe`，大小 6,462,453 字节，SHA-256 为 `4473B34F36C876F10EF0BF7FC186E7340B9B4B46468B23591B51A7DD2B2CB769`。
- 最终安装器和应用 EXE 当前均为 `NotSigned`；该事实已写入发布说明，不能把本地预览包表述为已签名正式发行包。
- 首次隔离 Release 验证错误地让多个项目共享同一中间目录，造成资产文件覆盖；改为只隔离最终输出、保留项目各自 `obj` 后验证通过，确认不是源码问题。


## 2026-07-14 空闲等待四档配置

- 用户已真实验证提交 `d71fe26` 的 10 秒空闲截图链路和可配置主动截图快捷键通过。
- 新需求只扩展空闲稳定时长选择为 10、15、30、60 秒四档；同一空闲段单次触发、容量 1、失败关闭、音频/页面二次检查和快捷键规则保持不变。
- 当前稳定时长是 `AutomaticPendingSend` 静态常量，并在截图前、附件后及最终提交前被多处引用；必须改为 PendingSend 实例属性，确保一次任务使用一致的配置快照。
- 配置使用整数秒字段最直观；严格验证只接受四个允许值，旧 settings.json 缺少字段时由属性默认值继续使用 10 秒。
- WPF 选择器保存失败时恢复旧设置与旧选择；保存成功只从下一个 PendingSend 生效，活动记录不包含网页或音频内容。
- Debug 完整构建为 0 警告、0 错误；新增四档边界、拒绝非法值、旧 Schema v1 默认兼容和 PendingSend 快照测试后，自动化测试 127/127 通过。
- 当前已有 Release 版 PID 10192 正在运行；为避免影响用户真实会话，没有关闭进程，而是在独立临时输出目录完成 Release 构建和测试。Release 结果为 0 警告、0 错误、127/127 通过；格式、发布元数据和差异检查也通过。


## 2026-07-14 对话空闲截图与快捷键

- 当前自动调度仍是开始后 30 秒、之后每轮完成后等待 2 分钟；用户已明确要求替换为 ChatGPT 网页音频停止且页面空闲连续稳定约 10 秒后才开始截图和输入操作。
- 可观察的“双方停止聊天”由允许的最小状态组成：当前 ChatGPT 页面可安全准备输入，且当前 WebView2 文档不再输出音频；程序仍不录音、不识别用户语音、不读取聊天正文。
- 现有 `AutomaticPendingSend`、容量 1、90 秒过期、提交前页面/音频二次检查可以继续复用；稳定门槛改为 10 秒后，第 10 秒前不会捕获或操作 composer。
- 自动触发必须锁存同一空闲段，否则持续静音可能造成重复任务。页面或音频恢复忙碌后才重新武装。
- 当前配置 Schema v1 使用严格未知字段拒绝；新增快捷键字段需要默认值、严格验证、往返测试，并保持旧配置缺字段时可加载默认值。
- 为满足游戏前台使用，快捷键使用 Windows `RegisterHotKey`，不模拟键盘、不移动鼠标；注册冲突必须显式失败并保留旧组合。
- `AutomaticSendLoop` 现只负责对新空闲窗口进行边沿触发；10 秒稳定时间仍由容量 1 的 `AutomaticPendingSend` 计量，因此稳定门槛通过前不会进入 Capture。
- 全局快捷键默认 `Ctrl+Alt+F10`，只在 `Running` 状态触发手动发送；录入时支持 Ctrl/Alt/Shift/Win 与 A-Z、0-9、F1-F12，使用 `MOD_NOREPEAT` 防止长按重复。
- Debug 完整构建为 0 警告、0 错误，自动化测试 114/114 通过；限定范围格式验证通过。
- 关闭锁定输出的单一旧版进程后，Release 完整构建 0 警告、0 错误，Release 自动化测试 114/114；全解决方案格式、发布元数据和 `git diff --check` 均通过。
- 未自动启动新版 UI，也未执行真实 ChatGPT/游戏操作；全局快捷键注册、前台游戏触发和新的 10 秒真实空闲链路仍需用户 RC 验证。


## 2026-07-14 测试前发布一致性审计

- 当前产品实现与 P0 Voice 调度修复均已提交；最新验收记录提交为 `b3bd032`，工作区恢复为干净状态。
- 权威开发范围中的核心模块已经存在，剩余开发应收敛到发布文档、版本元数据、便携/安装器脚本和配置边界一致性，不重复开发功能模块。
- 用户明确要求把测试留到开发最后；本阶段先完成全部代码与文档修改，随后再统一运行格式、构建、自动化测试和发布验证。
- 本轮记忆索引未找到 OpenGameMate 专项历史条目；当前仓库、开发文档和计划文件继续作为权威证据。
- README 中文简介仍错误地概括为“每两分钟捕获一次”，而英文和运行时已是首轮 30 秒、后续每两分钟，并在页面/音频安全空闲后才提交；需要双语一致。
- CHANGELOG 仍是初始 v0.1.0 功能清单，未记录容量 1 PendingSend、最多延后 90 秒、页面/音频稳定门控、提交前二次检查和空闲主动话题。
- 正确的发布说明文件名是 `docs/RELEASE_NOTES_0.1.0.md`；初次审计使用了不存在的 `docs/V0.1_RELEASE_NOTES.md`，已改为按实际文件清单继续，不重复该路径。
- 架构说明已覆盖首轮 30 秒、调度和 Voice 安全门控，但隐私安全诊断字段描述仍停留在初始字段，需要同步新增的 PendingSend/音频布尔状态元数据。
- 测试计划仍把真实证据概括为 Phase 0 单次路径；应补充已完成的 P0 小范围真实验证，同时保留 30 分钟和 2 小时完整 RC 尚未执行的事实。
- 发布说明仍写“固定两分钟”并声称本版本从未真实启动/发送，已与后续用户真实验收矛盾；需要改为首轮 30 秒、后续两分钟，并区分“已完成小范围真实验证”与“未完成完整 RC/长时浸泡”。
- 版本号目前分散在 App csproj、manifest、便携脚本和 Inno 定义中。可由 `Directory.Build.props` 统一 .NET 程序集版本，同时让发布脚本通过显式 `-Version` 参数把同一值传给 publish 与 Inno 宏，减少发布漂移。
- `Build-Installer.ps1` 当前无输出目录参数，总会调用默认便携目录；该目录一旦已有产物就会失败。应允许调用者指定新的空目录，并把实际 source directory 和版本传给 Inno，而不是要求代理清理旧目录。
- Inno 定义当前把版本、输出文件名和便携源目录分别硬编码为 0.1.0；应改为可由命令行 `/D` 覆盖、保留 0.1.0 安全默认值的宏。
- `CheckRemoteAdapterRules` 已属于 settings schema v1 且会被默认序列化。直接删除会让已有设置因严格未知字段策略失败；在没有官方仓库与维护者公钥前，保留该兼容字段并明确其安全禁用语义，比无迁移地删除更安全。
- App、BrowserWindow 和启动错误对话框仍硬编码 `v0.1.0`。仅统一 MSBuild 属性不足以避免 UI 版本漂移；应由 App 在运行时读取程序集 `AssemblyInformationalVersion`，作为窗口标题和错误标题的单一显示来源。
- 发布脚本初版改造后仍有两个可收敛点：Release Notes 源文件名应由已解析版本派生；Build-Installer 应先确认 Inno 编译器存在，再运行 publish，避免工具缺失时留下无用的半成品便携目录。
- 仓库级 `AGENTS.md` 的“固定每 2 分钟”仍可理解为后续周期，但没有表达用户批准的首轮 30 秒例外；为避免后续代理误改回两分钟首轮，应同步仓库约束。
- 最后一轮占位扫描未发现 `TODO`、`FIXME` 或 `NotImplementedException`。隐私关键词仅命中允许的当前 composer 文本写入/回读验证、类型名和安全说明，没有发现读取回复区域、聊天历史、Cookie、Token 或全页正文的新路径。
- `DiagnosticEvent` 的实际字段与更新后的架构说明一致：只保存有界按钮属性、调度时间、稳定时长、音频布尔状态和失败阶段，不存在音频内容、截图内容、对话正文或路径字段。
- 运行时版本显示现在可由 `AssemblyInformationalVersion` 派生；静态复核发现该新文件需要显式 `System` 命名空间（App 禁用隐式 using），已在最终测试前修正。
- 项目引用复核通过：Core 无项目依赖；Diagnostics 只依赖 Core；Adapters 只依赖 Core/Browser；App 是组合根。此次发布一致性修改没有引入反向依赖。
- 现有 CI 只验证 .NET restore/build/test/format，不会解析 PowerShell 发布脚本或核对 Directory.Build.props、manifest、Release Notes 与 Inno 宏的一致性。应增加一个只读发布元数据验证脚本并纳入 CI，防止未来版本漂移。
- `ProductMetadata` 的异常回退不应再次硬编码当前版本，否则版本升级仍可能静默显示旧值；回退应明确为 `unknown`，正常构建必须由程序集信息版本提供真实值。
- 发布一致性开发已完成：版本在 `Directory.Build.props` 统一；运行时标题读取程序集信息版本；便携/安装器接受显式版本和新空目录；CI 增加只读元数据验证；远程规则设置会明确区分禁用与缺少信任锚，同时始终安全使用内置规则。
- 最终元数据校验首次运行即发现 PowerShell 插值边界错误：错误消息中的 `$Version:` 会被解析成无效变量引用。该问题发生在 publish 前，未生成任何产物；已改用 `${Version}:`。
- 修正后发布元数据校验通过；全仓 `dotnet format --verify-no-changes` 通过；Release 解决方案构建为 0 警告、0 错误；Release 自动化测试 105/105 通过。
- 便携发布脚本已在全新目录 `artifacts/verification/OpenGameMate-v0.1.0-win-x64-20260714-180840` 成功执行，没有复用、覆盖或删除旧产物。
- 本机 `ISCC.exe` 仍不存在；安装器宏、参数链和 PowerShell 语法由元数据校验覆盖，但安装器 EXE 无法在当前环境生成，不能宣称安装器编译通过。
- 便携目录文件清单和 EXE 版本验证通过；首次压缩命令因 `-LiteralPath` 不展开 `*` 而未生成 ZIP，未修改便携目录，需改用 `-Path` 重试。
- 改用 `Compress-Archive -Path` 后便携 ZIP 验证通过：31 个条目、7,161,283 字节、SHA-256 `356BFF4F523AFF8515A8760B70F2FF5B6F0A8BC8DC06BE7BBA6D8C4D53F39A7E`。
- 安装器失败关闭预检通过：指定不存在的 Inno 编译器时，`Build-Installer.ps1` 在 publish 前退出，便携与安装器测试路径均未创建；本机缺工具时不会留下半成品。
- Inno Setup 官方文档确认 ISCC 支持 `/D<name>[=<value>]`，其行为等价于预处理器 `#define public`，并给出带空格值的引号示例；当前 Build-Installer 的逐参数调用与官方语法一致。来源：https://jrsoftware.org/ishelp/topic_isppcc.htm

## 需求

- 当前只完成 OpenGameMate Phase 0 技术可行性验证，不构建完整 v0.1.0。
- 交付最小可运行 PoC、启动方式、测试步骤、可行性报告和下一阶段建议。
- 第 5 项“后台 WebView2 无焦点添加图片和文字”优先级最高；失败必须如实报告并停止完整应用建设。
- 不读取 ChatGPT 回复、历史、Cookie、登录令牌或账号信息。
- 每项验证都需记录成功、失败、未验证和限制条件。

## 研究发现

- 2026-07-14 用户完成 A–D 真实定位：Capture、AttachImage、SetText 均不会停止 Voice；页面显示可发送本身也不会停止 Voice，但真正提交成功会停止仍在播放的声音。
- 确切中断阶段为 `Submit`，并与 `Voice playback tail` 叠加：ChatGPT 的 `stop-button`/`send-button` 状态不能证明 WebView2 音频已经播放完毕。
- WebView2 的 `CoreWebView2.IsDocumentPlayingAudio` 与 `IsDocumentPlayingAudioChanged` 只反映当前 WebView 文档是否正在输出音频，不是系统回环录音；本修复只保存布尔状态、状态版本与静音持续时间，不接触音频内容。
- 自动调度已改为容量严格为 1 的 PendingSend：周期到达只登记待发送；音频至少连续静音 3 秒，并且页面与音频状态连续稳定 6 秒后才捕获最新画面；超过 90 秒则跳过，不建立旧截图队列。
- 图片和文字进入 composer 后，提交前会再次检查页面结构、附件、按钮状态和同一音频状态版本；任何变化都失败关闭，不点击提交。
- 2026-07-14 首轮修复版真实日志显示 PendingSend 因 `send-button-count-idle-probe` 持续延后并在 90 秒过期：ChatGPT 的空 composer 不渲染 send-button，原实现把“内容准备完成后的提交规则”错误用于“准备前空输入区”。
- 修正规则分层：准备前允许目标 form 内 send-button 为 0，或为唯一的 disabled/可用按钮，但必须是无附件的唯一 composer、无 stop-button、无按钮歧义且音频连续静音；图片和文字准备完成后仍要求唯一、可用、位于目标 form 内的 send-button，并在点击前二次检查。
- 用户明确要求首轮自动尝试由 2 分钟改为开始后 30 秒；后续周期仍固定为 2 分钟，不新增可配置项。
- 修正版真实诊断记录了连续两次完整成功链路：`pending.ready` → `capture.succeeded` → `submission.attachment-prepared` → `submission.pre-submit-check=ok` → `adapter.submit-probe=ok` → `submission.succeeded`。第二轮期间 WebView2 音频多次切换，程序持续延后并在稳定静音后才执行。
- 用户确认本轮真实测试成功。当前小范围验收进度为连续成功 2/3；尚未取得第三次成功和“真实 Voice 忙碌超过 90 秒安全跳过”的人工证据，不能宣称完整小范围验收完成。
- 游戏声音可能通过物理麦克风串音或系统回环输入进入 ChatGPT Voice。用户决定本阶段暂不处理；保持 OpenGameMate 不录音、不处理或转发麦克风音频，并将耳机与实体麦克风作为当前运行建议。
- 自动化审计补充虚拟时钟证据：首轮只在 30 秒触发，后续只在再等待完整 2 分钟后触发；暂停状态到期会跳过且不重置节拍。慢任务即使跨过多个 2 分钟边界也不会保留追赶 tick 或在结束后立即补发。
- 提交前状态锁已扩展：最终 readiness 探测得到的 `AdapterPageState` 会传入一次性提交，控制探测和点击前脚本均要求页面状态保持一致；变化时返回 `NotReady` 且不点击。
- 真实日志显示首轮 `run.started` 到 `pending.created` 为 30.010 秒；第一轮稳定空闲 3.216 秒后捕获，第二轮在音频多次切换期间延后约 68 秒，并仅在页面稳定 3.179 秒且音频静音 3.365 秒后捕获。
- 后续真实失败日志定位出新的竞态：Voice 在约 3.2 秒静音后被判为可准备，但约 5.2 秒时再次输出；旧逻辑在附件准备后把这类正常会话恢复错误归类为 `AdapterInvalid`。修复将稳定门槛提高为 6 秒，并把附件准备期间 Voice 恢复归类为普通失败，保持不点击发送且不进入 `AdapterError`。
- 6 秒版本的真实测试仍发生中断：日志证明音频已连续静音约 6.5 秒且提交链路结构检查全部通过，但用户确认 Voice 对话仍在进行。这说明 `IsDocumentPlayingAudio` 无法覆盖 Voice 听取/识别阶段。进一步审计发现 Voice 活动控件在 composer form 外，而旧探测只在 form 内查找，导致页面被误记为普通 `ComposerWithAttachment`；现改为用精确 `data-testid` 做文档级 Voice 活动探测，并锁定准备前后的页面状态。
- 文档级 Voice 探测版的真实结果仍不满足目标：首个 PendingSend 在持续聊天期间延后 90 秒并安全过期，下一轮在页面/音频稳定后成功提交图片；Voice 没有被自动提交中断，但也没有对该图片消息作出语音回应。截图中的“语音聊天已结束”是用户之后手动结束 Voice 的结果，不能归因于自动提交。需要用 ChatGPT 网页自身的加号和发送控件做一次手动图片对照实验，区分平台行为与适配器提交差异。
- 网页原生手动图片对照已通过：用户在同一 Voice 会话中手动上传图片并发送明确请求“请根据这张图片继续和我对话”，ChatGPT 能结合图片继续语音回应。自动消息原文只是背景更新说明，没有明确要求立即回应；因此先保持附件和提交路径不变，仅把自动随图文字改为明确要求立即继续当前 Voice 对话，以单变量验证提示语是否为根因。
- 强化提示语后的自动链路总体可用。由于调度只在对话空闲时发送，最终自动随图文字改为要求 ChatGPT 根据最新画面主动发起一个自然、简短的话题并立即语音回应，同时明确避免机械复述或图片分析；附件、调度和提交规则保持不变。

- 仓库初始只有 `.git`，尚无代码或仓库内开发文档。
- 用户提供的权威开发文档来自仓库外附件；仓库记录其范围结论，不记录附件在本机的绝对路径。
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

- 用户提供的 `OpenGameMate_v0.1.0_开发文档.md`（仓库外附件）
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
## P0 Voice 播放尾音问题（2026-07-14）

- 已确认上一轮提交就绪等待逻辑解决附件实际提交问题，但 `stop-button` 消失与 `send-button` 可用只代表 DOM 允许发送，不足以证明 WebView2 音频播放已经完全结束。
- 本轮必须先区分 Capture、AttachImage、SetText、Submit 和 Voice playback tail；在真实证据前不预设中断阶段。
- 最终调度目标固定为容量 1 的 PendingSend，最多延后 90 秒，真正发送前才捕获最新画面，过期直接跳过且不补发。
- Microsoft WebView2 官方 API 提供 `CoreWebView2.IsDocumentPlayingAudio` 与 `IsDocumentPlayingAudioChanged`：前者仅表示该 CoreWebView2 是否存在音频输出（即使 WebView 被静音也为 true），后者在该文档开始或停止播放音频时触发。来源：https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.isdocumentplayingaudio 和 https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.isdocumentplayingaudiochanged
- 该信号属于当前 WebView 实例，不是系统回环音频或默认输出设备监控，因此不会错误采集游戏、语音软件或其他应用的声音；它只提供布尔播放状态，不包含音频样本、内容或识别结果。
- 当前项目引用 `Microsoft.Web.WebView2` 1.0.4078.44，已通过程序集反射确认 `CoreWebView2` 同时包含上述属性与事件，可直接实现连续静音 3 秒门控，无需新增音频采集依赖。
