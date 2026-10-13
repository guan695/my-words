# 参与拾词 / Contributing

欢迎中文或英文 Issue 与 Pull Request。优先解决真实使用中的问题，让收藏和学习的流程保持简单。

## 本地开发

需要 Windows、.NET 8 SDK 和 PowerShell。插件检查额外需要 Node.js 18 或更高版本。没有第三方 NuGet 或 npm 依赖。

```powershell
dotnet run --project .\src\MyWords\MyWords.csproj
.\scripts\build.ps1
```

默认构建精简版。加 `-SelfContained` 生成包含运行环境的完整版；`-DotnetPath` 可指定 SDK。两种版本输出到不同文件夹。重新构建前退出输出目录中的程序，或用 `-OutputRoot` 指定新的打包目录。

## 针对性检查

```powershell
# 插件请求、查重反馈与重试行为
node .\manggo-plugin\plugin.test.mjs

# 使用已构建的输出验证数据逻辑和本机 HTTP 接口
dotnet .\release\MyWords-Windows-x64-framework-dependent\MyWords.dll --self-test "$PWD\artifacts\test-results.txt"
Get-Content .\artifacts\test-results.txt

# 用示例数据离屏渲染界面
dotnet .\release\MyWords-Windows-x64-framework-dependent\MyWords.dll --preview "$PWD\artifacts\preview"
```

`--self-test` 使用独立临时词库，不读取真实词库、不调用外部 AI；失败会返回非零退出码。涉及托盘交互、DPI、Manggo 插件加载的改动，仍需在桌面环境中手动检查对应流程。

## 代码位置

| 位置 | 用途 |
| --- | --- |
| `src/MyWords/WordStore.cs` | 收藏、查重、每日词表与本地保存 |
| `src/MyWords/AppController.cs` | AI 任务与界面之间的协调 |
| `src/MyWords/AiClient.cs` | DeepSeek 请求与词卡解析 |
| `src/MyWords/ManggoBridge.cs` | 需要连接口令的本机 HTTP 接口 |
| `src/MyWords/MainWindow.*` | WPF 界面 |
| `manggo-plugin/` | Manggo 插件与针对性检查 |
| `scripts/` | 构建与资源生成 |
| `docs/` | 用户指南与示例截图 |

提交时说明解决的问题、使用者看到的变化和做过的验证。不要提交 API Key、连接口令、个人词库、发布产物或本机路径。提交的代码按本项目 MIT 协议发布。

## English

Small, focused contributions are welcome. Develop on Windows with the .NET 8 SDK and PowerShell. Node.js 18+ is only needed for plugin checks. The commands above build the app, run isolated checks and render sample screenshots without calling the AI API.

Describe the user-visible problem and the verification you performed. Keep credentials and personal vocabulary out of commits. Contributions are made under the project's MIT license.
