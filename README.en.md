# QuickFind

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A lightweight file locator for Windows. Type a file name and jump to its location instantly. An optional **AI related** tab asks the DeepSeek web chat to expand the query into related keywords, then searches local file names/paths — kept strictly separate from exact file-name matches.

> The UI is currently Simplified Chinese. This document is a short English guide; see [README.md](README.md) for the full Chinese documentation.

![Screenshot](assets/preview.png)

## Features

- Local, case-insensitive file-name/path search with space-separated terms.
- Persistent local index; first run builds it in the background, later runs load the cache.
- Two separate result tabs:
  - **File-name matches** — files whose names contain all query terms.
  - **AI related** — local matches for DeepSeek-expanded related keywords, excluding files already shown in the first tab.
- `Enter` to open, `Ctrl+Enter` to reveal in Explorer, `Ctrl+C` to copy the full path.
- Global hotkey `Ctrl+Alt+Space` (falls back to `Ctrl+Alt+F` if taken), tray support, single instance.
- Single-file Windows x64 executable with the .NET runtime bundled.

## Download

Get `QuickFind.exe` from [Releases](https://github.com/Jianmo-R/QuickFind/releases/latest), then double-click it. No installation is required.

Requirements: Windows 10/11 x64 and, for the AI tab, the Microsoft Edge WebView2 Runtime (normally included with Windows 11). The executable is unsigned, so SmartScreen may show an "Unknown publisher" warning.

## Build from source

Requires the .NET 10 SDK.

```powershell
git clone https://github.com/Jianmo-R/QuickFind.git
cd QuickFind
dotnet build .\QuickFind.csproj -c Release
dotnet run --project .\QuickFind.csproj
```

Publish a self-contained single file:

```powershell
dotnet publish .\QuickFind.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o .\dist
```

## Privacy

- The file index is local (`%LOCALAPPDATA%\QuickFind`).
- AI mode sends only the search query to `chat.deepseek.com`; it does not upload file names, paths, or file contents.
- No API key is required. DeepSeek login cookies are stored in the local WebView2 profile.

## License

[MIT](LICENSE) © 2026 Jianmo-R and QuickFind contributors. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for bundled components.
