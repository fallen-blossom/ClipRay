# ClipRay（截光）

ClipRay 是一个 Windows x64 截图和原位翻译工具。遇到看不懂的内容，框选后就能在原来的位置看译文；也可以固定一块区域，持续翻译里面的文字。截图、长截图和录屏也都放在同一个工具里。

目前是预览版。截图翻译和实时翻译可以试用，但复杂页面的排版、小字识别和漏译还有问题，正在根据实际使用情况继续修。

## 下载与开始使用

安装包放在 GitHub 的 Releases 页面。一般先选 **Lite 轻量版**；需要额外本地模型再选 **Full 完全版**。

从 `v1.0.6-preview.2` 起支持在原目录直接覆盖升级，无需卸载。升级保留设置、API Key、开机启动与自动清理选择，以及已有截图和录屏。升级前程序会尝试正常退出；旧版无法自动退出时，从托盘退出后重试即可。

| 版本 | 包含内容 |
| --- | --- |
| Lite | 基础离线机翻、DeepSeek，不带额外的 Local LMT 模型和 llama.cpp |
| Full | Lite 的功能，加 NiuTrans LMT-60-0.6B Q4_K_M 和 llama.cpp |

轻量版仍包含基础机翻和 OCR 所需的模型，所以它并非完全不带模型。两个版本都默认使用基础离线机翻；DeepSeek 需要自行填写 API Key。Full 的 Local LMT 可在单次区域翻译中选择，目前不用于实时翻译。

安装后启动 ClipRay，程序会驻留系统托盘。按 `Ctrl+Win+A` 框选，点击翻译；实时翻译使用 `Ctrl+Win+T` 开始、`Ctrl+Win+Y` 停止。保存目录和快捷键都可以在设置中调整。更多操作见 [使用教程](docs/ClipRay-使用教程.html)。

## 主要功能

- 区域截图：窗口/控件边界吸附、拖选与八向调整，双击 `Esc` 退出（第一次只提示）。
- 编辑工具：实时画笔、直线、由细到粗的箭头、矩形、圆形、椭圆、文字框，共用颜色与撤回；另有马赛克和取色。
- 静默截图：当前屏幕与完整虚拟桌面，完成后仅显示不进入截图的屏幕边缘光效。
- 长截图：手动滚动或自动滚动；停止后可缩放、拖拽查看并保存结果，关闭、取消、保存完成或异常退出后清理不再使用的临时分帧。
- 区域翻译：OCR 识别框内文字，再通过基础离线机翻、DeepSeek 或 Full 版的 Local LMT 翻译，译文显示在原来的区域。
- 实时翻译：持续翻译框选区域，支持基础离线机翻和 DeepSeek。当前先以英文转简中网页内容为主要测试场景。
- 录屏：`Ctrl+Win+Z` 进入录屏选区，Enter 确认、Esc 取消；输出 H.264/AAC MP4，只录默认系统输出，不访问麦克风；`F8` 或托盘菜单停止。
- 设置：开机自启、快捷键、默认保存目录、剪贴板、边框反馈、打开目录、Provider/Model 与安全 API Key 管理。

基础离线机翻和 Local LMT 在本机处理文字。选择 DeepSeek 时，待翻译文字会发送给 DeepSeek 服务，调用费用由其收取。API Key 保存在当前 Windows 用户的 Windows Credential Manager 中，不写入普通设置文件。

诊断记录可能包含原文、译文和排版信息。反馈问题前请检查截图、录屏和日志中有没有私密内容，不要上传 API Key。

## 默认快捷键

| 功能 | 快捷键 |
| --- | --- |
| 区域截图 | `Ctrl+Win+A` |
| 当前屏幕，不含任务栏 | `Insert` |
| 当前屏幕，含任务栏 | `Ctrl+Insert` |
| 完整虚拟桌面 | `Alt+/` |
| 开始录屏 | `Ctrl+Win+Z` |
| 停止录屏 | `F8` |
| 开始实时翻译 | `Ctrl+Win+T` |
| 停止实时翻译 | `Ctrl+Win+Y` |

截图与录屏入口快捷键可在设置中修改；冲突时 ClipRay 不会抢占其他应用的注册。截图编辑固定使用 `Ctrl+Z` 撤回、`Ctrl+Shift+Z` 重做，不注册为系统全局快捷键。

## 构建与发布

```powershell
Set-Location '<你的 ClipRay 克隆目录>'
dotnet restore ClipRay.sln --configfile NuGet.Config --source https://api.nuget.org/v3/index.json
dotnet build ClipRay.sln -c Release --no-restore
dotnet run --project tests\ClipRay.Core.Tests\ClipRay.Core.Tests.csproj -c Release --no-build --no-restore
dotnet run --project tests\ClipRay.App.Tests\ClipRay.App.Tests.csproj -c Release --no-build --no-restore
.\scripts\Publish-Local.ps1
```

发布目录为 `artifacts/app/`，脚本会创建或更新桌面快捷方式 `ClipRay 截光.lnk`，不会覆盖属于其他程序的同名快捷方式。

## 构建安装包

源码构建需要 .NET 10 SDK。安装器使用 Inno Setup，脚本为 `scripts/Build-Installer.ps1`，通过 `-Flavor Lite` 或 `-Flavor Full` 选择版本。打包前还需要准备 OCR、基础机翻和 Python 运行组件；Full 额外需要 GGUF 权重及 llama.cpp。模型和预编译运行库不放进 Git 仓库。

构建前先阅读 [构建说明](docs/BUILD.zh-CN.md)，准备其中列出的 OCR 和机翻组件。模型来源与校验方式见 [model-supply-chain.md](docs/model-supply-chain.md)。

## 安装与卸载

运行所选版本的安装包，可自行选择安装目录；桌面快捷方式为可选项。截图、长截图和录屏的保存目录可在设置中修改。卸载时默认保留已经保存的成品；清理输出目录前请仔细阅读提示。

## 许可证与源代码

ClipRay 源码公开，应用自写代码采用 [PolyForm Noncommercial 1.0.0](LICENSE)：允许非商业使用、修改和分享，不授权商业使用。具体条件以许可原文为准。第三方代码、图标、模型和运行库保留各自的许可，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 和 [源码说明](SOURCE.md)。

## 目前的不足

- 复杂网页、图片里的小字和混合字体，仍可能出现字号不协调、错位或漏译。
- 实时翻译的首帧和内容更新需要等待；DeepSeek 在网络或服务异常时可能更慢。
- 游戏、视频和长时间连续使用尚未完成充分测试。
- 基础机翻当前主要支持英文转简中，其他语言不要据此认为已经验证。

如果遇到问题，欢迎在 Issues 中写出版本、翻译路线、触发步骤，并附上去除私密信息后的原图和翻译结果。实时翻译问题最好补一段短录屏，方便判断是识别、翻译还是显示环节出错。
