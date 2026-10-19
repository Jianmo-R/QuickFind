# GitHub Actions 模板

这里的 `build.yml` 和 `release.yml` 是可用的 GitHub Actions 工作流模板。它们没有直接放在 `.github/workflows/`，因为发布本仓库所用的 GitHub OAuth 授权不包含 `workflow` scope；拥有仓库写权限且带 `workflow` scope 的维护者可以启用它们。

启用方式：

1. 在仓库中创建 `.github/workflows/` 目录。
2. 把本目录下的两个文件复制进去。
3. 提交并推送。

启用后：

- 每次 push 到 `main` 或创建 PR，会构建单文件 exe 并上传为 Actions artifact。
- push `v*` 标签时，会自动构建并创建 GitHub Release，附带 `QuickFind.exe` 和使用说明。

如果不需要 CI，可以忽略本目录；手动构建命令见根目录 [README.md](../../README.md)。
