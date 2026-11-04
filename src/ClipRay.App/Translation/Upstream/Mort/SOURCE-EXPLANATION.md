# MORT 取色模块

来源：https://github.com/kmonkeyhead/MORT

固定提交：`e8adc8c82168b4875c91b0ed6a38f1fca4e3634f`。原文件：`MORT/Service/Overlay/OverlayColorAnalyzer.cs`。

原始文件 SHA256：`a02d332a1e8d18a40b7274209d11263cc69a937e05e108c20caca997edcbaea9`。根 MIT LICENSE 原样保存在本目录及 `licenses/mort-MIT.txt`。

算法主体保留，仅增加来源注释、调整 namespace；没有引入上游应用、native 子项目、更新器、网络或包管理器。`MortSourceColorAdapter` 将区域像素转成紧密 BGRA，再把真实词框交给算法；`SourceVisualStyleEstimator.Estimate` 使用有效的非回退前景色。原有多色 span 分析仍按各自词框独立执行。

该模块只估计区域主色及背景，不恢复任意字体，也不保证所有复杂背景均能安全擦字。低对比回退不覆盖已观察到的源颜色。
