# Third-party notices

ClipRay's own code uses PolyForm Noncommercial 1.0.0. The components listed below retain their upstream licenses; ClipRay's noncommercial restriction does not replace those licenses.

## .NET dependencies

The following versions were checked against `src/ClipRay.App/packages.lock.json` and the corresponding cached NuGet package metadata on 2026-10-06. Their original packaged license/notice files and NuGet metadata are retained under `licenses/nuget`. For MIT expression-only packages, the permission text is accompanied by the package's original copyright metadata. `scripts/Collect-PackageNotices.ps1` reproduces this collection from the pinned package cache.

| Component | Version | License | Upstream |
| --- | --- | --- | --- |
| NAudio.Core / NAudio.Wasapi | 2.3.0 | MIT | https://github.com/naudio/NAudio |
| RapidOcrNet | 3.0.0 | Apache-2.0 | https://github.com/BobLd/RapidOcrNet |
| Microsoft.ML.OnnxRuntime / Managed | 1.27.0 | MIT | https://github.com/Microsoft/onnxruntime |
| Clipper2 | 2.0.0 | Boost Software License 1.0 | https://www.angusj.com/clipper2/Docs/Overview.htm |
| SharpGen.Runtime / COM | 2.4.2-beta | MIT | https://github.com/SharpGenTools/SharpGenTools |
| SkiaSharp / NativeAssets.Win32 / NativeAssets.Linux | 3.119.1 | MIT; bundled native notices must also be retained | https://github.com/mono/SkiaSharp |
| System.Numerics.Tensors | 9.0.0 | MIT | https://github.com/dotnet/runtime |
| Vortice.Direct3D11 / DirectX / DXGI / MediaFoundation | 3.8.3 | MIT | https://github.com/amerkoleci/Vortice.Windows |
| Vortice.Mathematics | 2.1.0 | MIT | https://github.com/amerkoleci/Vortice.Mathematics |

## Bundled .NET and Windows Desktop runtime

Both installers are self-contained and include Microsoft.NETCore.App.Runtime.win-x64 10.0.10 and Microsoft.WindowsDesktop.App.Runtime.win-x64 10.0.10. Their original package license files and metadata are retained under `licenses/nuget` in the matching version directories. The NETCore runtime's original `THIRD-PARTY-NOTICES.TXT` is retained there; the WindowsDesktop distribution package contains a `LICENSE` file and no separate notice file.

These .NET components use Microsoft's MIT license and retain any applicable upstream subcomponent terms in the original notices. Sources: https://github.com/dotnet/runtime, https://github.com/dotnet/windowsdesktop.

Both installers also include unmodified WinRT runtime assets from Microsoft.Windows.SDK.NET.Ref 10.0.26100.57, subject to the Microsoft Windows SDK license rather than MIT. The original document linked by the package metadata is retained as `licenses/nuget/Microsoft.Windows.SDK.NET.Ref-10.0.26100.57/LICENSE.rtf`. Its provenance note links the official redistribution list, which explicitly covers these files as part of a Windows application: https://learn.microsoft.com/en-us/legal/windows-sdk/redist#microsoftwindowssdknetref.

`scripts/Collect-PackageNotices.ps1 -PublishedPayloadRoot <payload>` reads the actual runtime pack names and versions from the published dependency metadata. The installer build rejects payloads whose runtime pack license records are absent.

## Inno Setup Chinese Simplified Translation

`installer/Languages/ChineseSimplified.isl` is a third-party translation maintained by Zhenghan Yang (Kira).

Source: https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation

License: MIT. Copyright (c) 2019 - 2020 kirakira. The original license is included as `licenses/inno-chinese-MIT.txt`.

## SMaLL-100 common-language machine translation

Some development builds use SMaLL-100 model data converted to CTranslate2. The current `artifacts/machine-translation` installer payload contains the Argos English-to-Chinese package described below; this notice does not mean SMaLL-100 is included in every build.

Original model: https://huggingface.co/alirezamsh/small100

Converted data: https://huggingface.co/entai2965/small100-ctranslate2 (revision 82ec33f406a6d58fdb73fa699a6fa5309fc20b09).

License: MIT. Copyright (c) 2022 Alireza Mohammadshahi. The original license is included as `licenses/small100-MIT.txt`.

The additional Arabic and Cyrillic PP-OCRv5 recognition models originate from RapidAI/RapidOCR and PaddlePaddle/PaddleOCR under Apache-2.0; the included `licenses/Apache-2.0.txt` applies.

## Bundled offline machine translation runtime

The current machine translation payload contains the Python 3.14.3 embeddable runtime and Python packages CTranslate2 4.8.1, NumPy 2.5.2, PyYAML 6.0.3, and SentencePiece 0.2.2. Python's license is in `machine-translation/runtime/LICENSE.txt`. NumPy's license and bundled subcomponent notices are in `machine-translation/runtime/Lib/site-packages/numpy-2.5.2.dist-info/licenses/`; PyYAML's MIT license is in its corresponding `pyyaml-6.0.3.dist-info/licenses/` directory.

CTranslate2: https://github.com/OpenNMT/CTranslate2/tree/v4.8.1 — MIT, with the upstream copyright and license in `licenses/ctranslate2-MIT.txt`. SentencePiece: https://github.com/google/sentencepiece/tree/v0.2.2 — Apache-2.0, with the license text in `licenses/Apache-2.0.txt`.

The English-to-Chinese package is `translate-en_zh-1_9.argosmodel` from the Argos Open Tech package index. Its included README credits Jörg Tiedemann and Santhosh Thottingal, “OPUS-MT — Building open translation services for the World,” EAMT 2020, and states that the original OPUS model is licensed CC-BY 4.0 (https://creativecommons.org/licenses/by/4.0/). ClipRay extracts and repackages the model data for local inference; its source note is `machine-translation/SOURCE-EXPLANATION.md`. The original model README is retained with the model; the CC-BY 4.0 legal text is included as `licenses/CC-BY-4.0.txt`.

## NiuTrans LMT-60-0.6B

The Full installer bundles a quantized form of the NiuTrans LMT-60-0.6B translation model. The Lite installer does not include this model.

Source: https://huggingface.co/NiuTrans/LMT-60-0.6B

The bundled Q4_K_M file was published by https://huggingface.co/mradermacher/LMT-60-0.6B-GGUF at revision `86942b9f925bfccd4196d4982578b46413a1f73f`. The publisher's LFS SHA-256 matches ClipRay's pinned artifact. ClipRay distributes that file without further model modifications. Details are in `docs/model-supply-chain.md`.

License: Apache License 2.0. A complete copy is distributed in the `licenses` directory.

The quantized packaging does not change the upstream model license.

## LLVM OpenMP runtime

The Full llama.cpp payload also includes `libomp140.x86_64.dll`. LLVM's Apache-2.0 license with LLVM exceptions is retained in `licenses/LLVM-Apache-2.0-with-exceptions.txt`.

Source: https://github.com/llvm/llvm-project

## Optional NVIDIA CUDA runtime

Full builds may include CUDA 12.8.1 cuBLAS and CUDA runtime DLLs used by CTranslate2. These components remain subject to NVIDIA's software license and CUDA supplement, not ClipRay's license. Their original license and subcomponent notices accompany the DLLs under `machine-translation/gpu`. Lite builds omit this payload.

Source and distribution terms: https://docs.nvidia.com/cuda/archive/12.8.1/eula/index.html

## NVIDIA cuDNN

The Full CTranslate2 runtime contains the optional `cudnn64_9.dll` loader (file version 9.10.2.21). It remains subject to NVIDIA's cuDNN SDK agreement and supplement, including the NVIDIA GPU scope. The original agreement and third-party notices are retained in `licenses/NVIDIA-cuDNN.txt`. Lite builds omit this DLL and use the CPU translation route.

Source and distribution terms: https://developer.download.nvidia.com/compute/cudnn/redist/cudnn/LICENSE.txt
## llama.cpp

The Full installer bundles the `llama-cli` runtime and its required libraries from llama.cpp.

Source: https://github.com/ggml-org/llama.cpp

License: MIT. A complete copy is distributed as `licenses/llama.cpp-MIT.txt`.

## Lucide Icons

ClipRay incorporates adapted vector geometry from the Lucide `pipette` and `languages` icons.

Source: https://github.com/lucide-icons/lucide

ISC License

Copyright (c) 2026 Lucide Icons and Contributors

Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, provided that the above copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

## Microsoft Fluent UI System Icons

ClipRay incorporates adapted vector geometry from the Fluent UI System Icons `video` icon.

Source: https://github.com/microsoft/fluentui-system-icons

MIT License

Copyright (c) 2020 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
