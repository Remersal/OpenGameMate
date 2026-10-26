# OpenGameMate v0.1.1 Early Preview

## 中文

- 修复截图发送失败和 ChatGPT 网页适配问题，包括附件准备和提交期间本地 PNG 预览来源变化导致的误拦截。
- 用户已在修复版中手动验证真实截图发送成功；保留图片身份校验、草稿/上下文检查、Voice 忙碌延迟及最多一次提交保护。
- ChatGPT 后续界面更新仍可能导致兼容性失效，需要更新适配器。本次确认不代表所有账号、页面版本或长期 Voice 场景均已验证。

下载 Windows x64 安装包或便携 ZIP，并可用 `SHA256SUMS.txt` 校验。仍为未签名 Early Preview，需要 .NET 8 Desktop Runtime 和 WebView2 Runtime。发布包从干净构建目录生成，不含用户登录资料、草稿、截图或日志。

## English

- Fixed screenshot sending and ChatGPT web adapter compatibility, including local PNG preview source changes during attachment preparation and submission.
- The user manually confirmed successful real screenshot sending on the repaired build. Attachment identity checks, draft/context guards, Voice-busy deferral, and at-most-once submission remain enforced.
- Future ChatGPT interface updates may break compatibility and require another adapter update. This confirmation does not cover every account, page revision, or long-running Voice scenario.

Download the Windows x64 installer or portable ZIP and verify it with `SHA256SUMS.txt`. This remains an unsigned Early Preview requiring .NET 8 Desktop Runtime and WebView2 Runtime. Packages are built in a clean directory and contain no user profiles, drafts, screenshots, or logs.

## Validation

The adapter repair is based on `94db4366e460769d4927da81ee24f30a3590ffb1`, with 131 .NET tests, 72 isolated Edge tests, and 87 production-adapter WebView2 assertions passing. The v0.1.1 source changes only release metadata and documentation. The real screenshot-send confirmation was supplied separately by the user; synthetic tests do not establish live-site compatibility by themselves.
