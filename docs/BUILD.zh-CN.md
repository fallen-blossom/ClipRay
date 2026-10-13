# 构建 ClipRay

需要 Windows x64、.NET 10 SDK。构建安装器还需要 Inno Setup 6。NuGet 包的版本固定在 `packages.lock.json` 中，使用锁定还原。

源码包不包含模型权重、Python 运行时或预编译 llama.cpp。应用工程会复制这些组件；因此，运行完整应用构建前要先准备下面的文件，不能把缺少这些文件的构建当作完整离线版。

## 两版共用的组件

| 目录 | 内容与来源 |
| --- | --- |
| `artifacts/models/rapidocr` | RapidOCR/PaddleOCR 的 PP-OCRv5 检测、方向分类、阿拉伯文、俄文及韩文识别文件与字典；可选 PP-OCRv6 文件。具体文件名见 `src/ClipRay.App/ClipRay.App.csproj`。来源：https://github.com/RapidAI/RapidOCR 和 https://github.com/PaddlePaddle/PaddleOCR 。 |
| `artifacts/machine-translation/runtime` | Python 3.14.3 embeddable，配 CTranslate2 4.8.1、NumPy 2.5.2、PyYAML 6.0.3、SentencePiece 0.2.2，保留运行时及 wheel 中的许可文件。来源：https://www.python.org/downloads/windows/ 和对应 PyPI 项目。 |
| `artifacts/machine-translation/models/translate-en_zh-1_9` | Argos 的英文转中文 1.9 模型，保留原始 README。官方索引：https://github.com/argosopentech/argospm-index 。已使用的 `.argosmodel` 压缩包 SHA-256 为 `433e7c4f034d87fbe2353161e05f18646d7999452f801a4e1f0378522b9850ab`。 |

应用会从 NuGet 的 RapidOcrNet 3.0.0 中复制拉丁文 OCR 文件。其他模型的文件名和目录必须与工程定义一致，不能随意改名。

这些说明列出了当前载荷，尚未提供从零下载、安装并组装全部组件的一键脚本。准备依赖时应核对官方来源、版本、校验值和许可；不要运行来源不明的整合包。

## Full 额外组件

- `artifacts/models/NiuTrans-LMT-60-0.6B/LMT-60-0.6B.Q4_K_M.gguf`：NiuTrans LMT-60-0.6B 的 Q4_K_M GGUF，SHA-256 为 `743c6cdc13294b5c470ec0e89e79d64f96ef0cab94ce1285c78da4dc0f56be5c`。模型来源：https://huggingface.co/NiuTrans/LMT-60-0.6B 。
- `artifacts/tools/llama-b8816-bin-win-vulkan-x64`：llama.cpp b8816 的 `llama-cli.exe` 及同发行包所需 DLL。来源：https://github.com/ggml-org/llama.cpp/releases/tag/b8816 。
- 可选 CUDA 12.8.1 运行库：工程参数 `ClipRayCudaRuntimeRoot` 指向本机目录，默认 `F:\ClipRayAssets\cuda-12.8.1\bin`。需要保留 NVIDIA 许可和组件声明。Lite 构建显式关闭此载荷。

`Get-LmtModel.ps1` 当前获取的是旧的 1.7B 开发备用模型，不能拿它替代首发 Full 所需的 0.6B 模型。首发量化文件由 `mradermacher/LMT-60-0.6B-GGUF` 发布；固定版本下载地址和已核对的发布方 LFS 摘要见 [模型来源说明](model-supply-chain.md)。

## 应用构建和检查

在仓库根目录运行：

```powershell
dotnet restore ClipRay.sln --configfile NuGet.Config --source https://api.nuget.org/v3/index.json --locked-mode
dotnet build ClipRay.sln -c Release --no-restore
./tests/Run-BackgroundTests.ps1 -SkipBuild
./tests/Run-BackgroundTests.ps1 -SkipBuild -CollectOnly
```

后台检查逐组运行九个阶段、Stage7 返修组和 Core.Tests，使用注入依赖、离屏窗口和独立临时文件；集合输出不执行测试。App.Tests 无参数、未知参数或未知用例返回 2；完整历史用例只能显式选择 `--legacy-all-tests`，其中包含实际系统入口，不能当作后台检查。故意失败用例只由 `-FailureSentinel` 选择，并将失败退出码传给调用方。

开始录屏快捷键为 `Ctrl+Win+Z`。自动测试不代表前台录屏或翻译质量已经验收；前台检查应另外记录实际版本、动作和结果。

## 安装器与公开源码包

```powershell
./scripts/Build-Installer.ps1 -Flavor Lite -IsccPath '<你的 ISCC.exe 路径>' -KeepStaging
./scripts/Build-Installer.ps1 -Flavor Full -IsccPath '<你的 ISCC.exe 路径>' -KeepStaging
```

安装器应从没有 tracked 改动和 untracked 输入的确定提交构建。公开源码由 `Export-PublicSource.ps1` 从同一个提交导出，文件选择见 `scripts/public-source-paths.txt`；不复制本机未跟踪文件、缓存或私人开发记录。`SOURCE-REVISION.txt` 记录对应提交。

安装器默认 `Lite`，`-Flavor Full` 显式保留完整载荷。真正发布必须使用新空目录，版本标识为 `1.0.6+<完整 commit>`。`release-identity.json` 记录 schema、commit、AppInformationalVersion、flavor、win-x64、自包含状态及完整文件的相对路径、大小和 SHA256；复用 `-PreparedStaging` 时先校验这些原记录，缺失、额外或变更文件一律拒绝，不重写旧 manifest 使其通过。`release-manifest.json` 保留原来的扁平数组；这两个根 metadata 文件不参与 payload 的自引用 hash，identity 另记录 flat manifest 的 hash。安装映射为 `licenses/*` → `{app}/licenses/*`，其他 payload → `{app}/app/*`。

`Publish-Local.ps1 -SkipPublish` 只处理快捷方式，不证明真正发布的身份。旧 DLL-only 录屏诊断补丁已停用，旧 `DisplayVersion=1.0.2` 不能证明兼容，需完整升级。卸载仅请求准确安装路径的实例正常收尾；旧协议、取消、超时或无法核实退出时停止并提示手动退出，不按名称强杀同名程序。自动清理默认关闭，只回收有保存完成证明且超过保留期的成品；历史、未完成和恢复材料保留，逐项结果可在设置的最近清理提示中查看。

升级 SDK 或依赖后，先对试构建的发布目录运行 `scripts/Collect-PackageNotices.ps1 -PackageCacheRoot '<NuGet 缓存目录>' -PublishedPayloadRoot '<发布目录>'`，收集实际版本的原始许可，再提交这些文件。安装器会检查实际随包运行时的许可记录，避免只更新 DLL、遗漏新版通知。

首次公开快照不含原来的开发 Git 历史，因此其中的 `SOURCE-REVISION.txt` 用于标明本地开发来源；在公开快照中重新构建时，应提交自己的修改，再导出对应源码。
