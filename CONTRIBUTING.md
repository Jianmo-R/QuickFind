# 贡献指南

感谢你愿意改进闪查。这个项目是一个 Windows 桌面工具，代码量不大，欢迎提交 Issue 和 Pull Request。

## 开发环境

- Windows 10 / 11 x64
- .NET 10 SDK
- 可选：Visual Studio 2022/2026，或 VS Code + C# Dev Kit
- Microsoft Edge WebView2 Runtime（运行 AI 分栏时需要）

```powershell
dotnet restore
dotnet build .\QuickFind.csproj -c Release
dotnet run --project .\QuickFind.csproj
```

发布单文件 exe：

```powershell
dotnet publish .\QuickFind.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o .\dist
```

## 提交前检查

1. 项目可以成功构建，最好也手动运行一次搜索。
2. 不要提交以下内容：
   - `bin/`、`obj/`、`dist/`
   - `%LOCALAPPDATA%\QuickFind` 下的索引、WebView2 登录数据
   - Token、Cookie、个人文件路径、测试截图
3. 涉及 DeepSeek 网页操作的改动，请说明你验证过的页面状态；网页 DOM 可能随官方改版变化。
4. 修改 UI 时，请附一张截图，并说明是否在 100% 和 150% DPI 下检查过。
5. 保持现有代码风格：C# 可空引用开启、WinForms 代码后置、尽量少引入第三方依赖。

## Pull Request

- 每个 PR 只做一件事，标题写清楚改动。
- 关联相关 Issue（例如 `Fixes #12`）。
- 新增功能请同步更新 `README.md` 和 `CHANGELOG.md`。
- 不要修改 `LICENSE` 的版权人；新增文件默认沿用 MIT 许可证。

## 安全问题

不要通过公开 Issue 报告安全漏洞，请按 [SECURITY.md](SECURITY.md) 的方式私下联系维护者。

## 行为准则

参与本项目即表示同意遵守 [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)。
