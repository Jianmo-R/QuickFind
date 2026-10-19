# 闪查 QuickFind

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D4.svg)](#系统要求)

一个轻量的 Windows 本机文件定位工具：输入文件名，立即看到文件位置。可选的“AI 相关”分栏会通过网页版 DeepSeek 扩展同义词，再在本地匹配相关文件，并与文件名精确匹配严格分开。

> 界面目前为简体中文。完整英文说明见 [README.en.md](README.en.md)。

![界面预览](assets/preview.png)

## 功能

- **本地文件名/路径搜索**：空格分隔多关键词，不区分英文大小写。
- **本地索引**：首次后台建索引，之后读取缓存；`F5` 可随时重建。
- **两个独立分栏**：
  - **文件名匹配**：文件名中包含全部搜索词的文件；
  - **AI 相关**：把搜索词交给网页版 DeepSeek 扩展为相关词，再匹配本地文件名/路径，并排除已直接匹配的文件。
- **快速操作**：`Enter` 打开、`Ctrl+Enter` 资源管理器定位、`Ctrl+C` 复制路径、右键菜单。
- **全局唤起**：`Ctrl+Alt+Space`（冲突时自动回退 `Ctrl+Alt+F`），支持托盘常驻和单实例唤醒。
- **单文件发布**：Windows x64，自带 .NET 运行时，不需要安装。

## 下载与运行

1. 打开 [Releases](https://github.com/Jianmo-R/QuickFind/releases/latest)。
2. 下载 `QuickFind.exe`。
3. 双击运行。首次启动会扫描本机磁盘建立索引，界面左下角显示进度。

程序没有数字签名，Windows SmartScreen 可能提示“未知发布者”；选择“更多信息 → 仍要运行”即可。

### 系统要求

- Windows 10 / Windows 11，64 位。
- 单文件已包含 .NET 运行时，无需安装 .NET。
- AI 分栏需要 Microsoft Edge WebView2 Runtime。Windows 11 通常已自带；缺失时可安装微软官方 Evergreen Runtime。
- 不扫描网络驱动器。

## 使用

| 操作 | 说明 |
| --- | --- |
| `Enter` | 打开选中文件 |
| `Ctrl+Enter` | 打开所在位置并选中文件 |
| `Ctrl+C` | 复制完整路径 |
| `Esc` | 隐藏到托盘 |
| `F5` | 重新扫描并建立索引 |
| `Ctrl+Alt+Space` | 显示 / 隐藏窗口 |

搜索技巧：

- 多个关键词用空格分隔，例如 `下载 报告`。
- 文件名匹配只检查文件名；AI 相关会同时检查路径。
- 文件名匹配最多展示 240 条，AI 相关最多展示 120 条，底部显示总匹配数。

## 网页版 DeepSeek

- 点击右上角“连接 DeepSeek”。应用会内嵌打开 `chat.deepseek.com`。
- 如果网页直接进入对话页（已登录或访客模式），会自动完成连接；需要账号时在窗口内登录一次，登录状态保存在本机。
- 每次 AI 搜索只发送**搜索词**，不会上传文件列表、路径或文件内容。
- AI 结果是“关键词扩展 + 本地匹配”，不是把每个文件都交给 AI；结果仍可能有宽泛匹配。
- DeepSeek 网页改版、断网或额度受限时，AI 分栏可能失败；**文件名匹配不受影响**。

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```powershell
git clone https://github.com/Jianmo-R/QuickFind.git
cd QuickFind
dotnet build .\QuickFind.csproj -c Release
dotnet run --project .\QuickFind.csproj
```

发布单文件 exe：

```powershell
dotnet publish .\QuickFind.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o .\dist
```

输出：`dist\QuickFind.exe`。

> `WebView2Loader.dll` 已作为资源嵌入 exe，首次使用 AI 时释放到可写数据目录，因此发布目录只需要这一个 exe。

仓库还提供 GitHub Actions 构建/Release 模板，位于 [`docs/github-actions/`](docs/github-actions/)；复制到 `.github/workflows/` 即可启用。

## 数据与隐私

- 本地索引：`%LOCALAPPDATA%\QuickFind\index.bin`
- DeepSeek 登录数据：`%LOCALAPPDATA%\QuickFind\WebView2`
- 错误日志：`%LOCALAPPDATA%\QuickFind\error.log`
- 程序不联网建立本地索引；只有 AI 分栏会把搜索词发给 DeepSeek。
- 卸载：退出程序，删除 `%LOCALAPPDATA%\QuickFind` 和 `%LOCALAPPDATA%\Programs\QuickFind` 即可。

## 项目结构

```text
QuickFind/
├─ Program.cs              启动、单实例、异常处理
├─ MainForm.cs             主界面、分栏、索引调度
├─ FileIndex.cs            索引快照、直接匹配与 AI 相关匹配
├─ IndexBuilder.cs         磁盘遍历
├─ DeepSeekSession.cs      网页版 DeepSeek 问答与关键词解析
├─ DeepSeekWindow.cs       内嵌 DeepSeek 登录/对话窗口
├─ WebView2Bootstrap.cs    单文件版 WebView2Loader 释放与加载
├─ Ui.cs                   暗色控件与绘制
├─ NativeMethods.cs        全局热键、窗口圆角等 Win32 调用
├─ app.manifest            长路径与权限声明
├─ docs/github-actions/    CI / Release 工作流模板
└─ assets/                 图标、预览图、生成脚本
```

## 已知限制

- AI 分栏依赖 DeepSeek 官方网页的 DOM，网页改版可能导致自动化失效；欢迎提交修复。
- 索引只保存文件名，不读取文件内容。
- 首次索引耗时取决于磁盘文件数量；之后启动只读取缓存。
- 目前只支持 Windows x64。

## 贡献

欢迎 Issue 和 Pull Request。请先阅读 [CONTRIBUTING.md](CONTRIBUTING.md) 和 [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)。

- 安全问题请按 [SECURITY.md](SECURITY.md) 私下报告。
- 变更记录见 [CHANGELOG.md](CHANGELOG.md)。
- 第三方组件声明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## License

[MIT](LICENSE) © 2026 Jianmo-R and QuickFind contributors
