# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 格式，版本号遵循语义化版本。

## [1.0.0] - 2026-10-19

### Added

- 本机文件名/路径搜索，支持空格分隔多关键词。
- 首次运行后台建立索引，之后读取本地缓存；超过 10 分钟自动刷新，支持 `F5` 重建。
- “文件名匹配”和“AI 相关”两个独立分栏：
  - 文件名匹配只检查文件名；
  - AI 相关通过内嵌网页版 DeepSeek 扩展同义词/相关词，再在本地匹配，并排除已直接匹配的文件。
- 全局热键 `Ctrl+Alt+Space`（冲突时回退到 `Ctrl+Alt+F`）。
- 系统托盘、单实例唤醒、结果右键菜单、资源管理器定位、复制路径。
- 暗色 WinForms 界面，支持高 DPI。
- 单文件 Windows x64 发布，内置 .NET 运行时；WebView2Loader 嵌入 exe 并按需释放。
- GitHub Actions 构建与 Release 工作流。

### Known limitations

- AI 分栏依赖 DeepSeek 网页 DOM，官方改版可能导致失效；文件名匹配不受影响。
- 只扫描本机固定/移动磁盘，不扫描网络驱动器。
- 索引为文件名级别，不读取文件内容。
