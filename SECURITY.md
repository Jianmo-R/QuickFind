# 安全策略

## 支持版本

当前维护 `main` 分支和最新 Release。旧版本不保证收到安全修复。

## 报告漏洞

请使用 GitHub 的私密漏洞报告功能：

https://github.com/Jianmo-R/QuickFind/security/advisories/new

不要在公开 Issue 中粘贴 Token、Cookie、个人文件路径或完整日志。请提供：

- 受影响的版本/提交
- 复现步骤
- 影响范围
- 你建议的修复方式（如果有）

## 安全模型

- 程序只读取本机文件路径，建立本地索引；索引保存在 `%LOCALAPPDATA%\QuickFind`。
- 使用“AI 相关”时，只有**搜索词**会发送给网页版 DeepSeek；不会上传文件列表、路径或文件内容。
- DeepSeek 登录状态保存在本机 WebView2 用户数据目录；程序不读取浏览器 Cookie，也不要求 GitHub/DeepSeek API Key。
- 程序不会自动执行搜索到的文件；只有用户按 `Enter` 或双击时才交给系统默认程序打开。
- 索引文件是程序自己的压缩二进制格式，不包含可执行代码；删除 `index.bin` 后会自动重建。
- Release 中的 exe 未做代码签名，请从本仓库 Release 下载并自行校验 SHA-256。

## 依赖

- Microsoft.Web.WebView2 SDK：BSD-3-Clause，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
- .NET Runtime：MIT
