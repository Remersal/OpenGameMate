# OpenGameMate 仓库工作约束

## 当前范围

- Phase 0 已完成并由提交 `377a506` 固化；当前按开发文档实施 **OpenGameMate v0.1.0 Early Preview**。
- 权威范围依据是用户提供的 `OpenGameMate_v0.1.0_开发文档.md` 和已完成的 `docs/PHASE0_FEASIBILITY_REPORT.md`。
- 技术基线：C#、.NET 8、WPF、Microsoft Edge WebView2、Windows Graphics Capture、JSON 配置、本地结构化日志、xUnit。
- 只实现文档明确列出的 v0.1.0；不得扩展到账号系统、商业化、游戏注入、内存读取、攻略引擎、角色商城或绕过平台限制。

## 分阶段与提交纪律

- 严格按顺序实施：基础设施 → 浏览器 → 截图 → 调度 → 网页适配 → WPF 界面与集成。
- 每个阶段只修改该阶段及必要的公共契约；不得一次性铺开后续模块。
- 每个阶段必须先运行相关单元测试和完整构建，再创建一个独立 Git 提交。
- 编译或单元测试通过不等于真实网页、登录、Voice、焦点或上传行为通过；运行证据必须单独标注。
- 保留 Phase 0 结果、失败记录和历史，不覆盖既有证据。

## 架构边界

- `OpenGameMate.Core`：状态、状态机、领域契约和固定运行策略；不依赖其他项目。
- `OpenGameMate.Configuration`：安装版/便携版路径、JSON 设置、Schema 与验证；不包含 UI 或网页逻辑。
- `OpenGameMate.Diagnostics`：隐私安全的结构化日志；可依赖 Core，不记录自由文本网页内容。
- `OpenGameMate.Browser`：WebView2 生命周期、官方导航与权限；不读取 Cookie、Token 或账号资料。
- `OpenGameMate.Capture`：只捕获主显示器，保持比例且不超过 1920×1080；不分析画面。
- `OpenGameMate.Adapters`：只定位允许的输入/附件/提交/错误状态；不得读取回复、历史或完整 HTML。
- `OpenGameMate.App`：WPF 组合根、窗口、托盘和用户确认；业务规则应留在对应模块。

## 产品与安全边界

- 登录、Cloudflare、麦克风授权、Voice 开启和真实消息发送必须由用户知情参与。
- 不收集账号密码、Cookie、Token、聊天记录、模型回复、音频或遥测。
- 不注入游戏、不读取游戏内存、不模拟全局键鼠、不绕过反作弊、验证码、额度或保护措施。
- 截图默认不持久化；只写 OpenGameMate 专用临时位置，使用后删除。
- 自动发送固定每 2 分钟一次；v0.1.0 不提供修改该周期的设置。
- 同时最多一个提交任务；手动优先并跳过冲突的自动轮次；普通失败不立即重试。
- 检测到额度限制进入 `VoiceOnly`；适配失效进入 `AdapterError` 并停止随机尝试。
- 当前 Phase 0 的任意 HTTPS 导航仅是诊断模式；正式 v0.1.0 浏览器阶段必须恢复受控导航边界。

## 日志与配置

- 日志允许：版本、环境、状态、尺寸、文件大小、布尔结果、稳定错误码和异常类型。
- 日志禁止：截图、网页 HTML、对话/回复、账号、Cookie、Token、音频、用户输入正文和带用户名的完整路径。
- 配置必须有 Schema、大小限制、严格字段验证和安全默认值；未知字段或未知版本失败关闭。
- 远程适配规则默认只从官方 GitHub 仓库检查，并必须经过 HTTPS、Schema、大小、字段允许列表和数字签名验证；该能力在网页适配阶段实现。

## 文件与命令安全

- 禁止批量删除文件或目录。
- 不得使用 `del /s`、`rd /s`、`rmdir /s`、`Remove-Item -Recurse`、`rm -rf`。
- 删除时一次只能处理一个明确、已核对的文件路径。
- 如果需要批量删除，立即停止并请用户手动处理。
- 不得执行 `git reset --hard`、破坏性 checkout 或覆盖用户已有修改。
