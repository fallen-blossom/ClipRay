# ClipRay 本地命令行

命令直接调用同一安装目录、当前 Windows 用户的运行实例。没有 HTTP 服务或远程控制端口，不需要管理员权限。普通截图、热键与设置界面保持原行为。

先启动托盘程序，再执行命令。PowerShell 用管道接收 JSON，这也会等待 GUI 子程序退出：

```powershell
$clip = 'F:\Clip\app\ClipRay.App.exe'
Start-Process -FilePath $clip -WindowStyle Hidden
& $clip --cli help | ConvertFrom-Json
& $clip --cli status | ConvertFrom-Json
& $clip --cli settings-open | ConvertFrom-Json
& $clip --cli translation-open | ConvertFrom-Json
& $clip --cli recording-settings | ConvertFrom-Json
```

`settings-open` 和 `translation-open` 仅打开或激活对应的现有窗口，返回进程和窗口句柄。打开翻译窗口不会提交原文、调用模型或修改默认翻译设置。

修改录屏偏好只影响下一次录制，可以只指定需要改变的字段；已有录制使用冻结的参数。更改会保存，界面中未保存的草稿或磁盘上被其他程序改动的设置会阻止覆盖。

```powershell
& $clip --cli recording-settings --fps 60 --quality Balanced --cursor true --audio true | ConvertFrom-Json
```

帧率可选15、24、30、60；画质为Compact、Balanced、High。High保留原高码率与较高编码质量。声音为系统声音，不包含麦克风。

区域坐标为物理像素，顺序X、Y、宽、高；区域须在同一显示器内，编码会按原规则裁为偶数尺寸。以下只是命令示例，执行前应换成自己要录的区域。

```powershell
$started = & $clip --cli recording-start --region 320 260 1920 1080 --no-copy | ConvertFrom-Json
if (-not $started.Success) { throw $started.Status }
$runId = $started.Data.ActiveRunId
& $clip --cli recording-stop --run-id $runId | ConvertFrom-Json
```

默认保留选区提示框，不弹控制条或预览，不抢输入焦点；声音失败会返回错误，不弹“无声继续”确认。附加`--show-ui`可使用原控制条、预览和声音恢复流程。`--no-copy`禁用本次剪贴板写入，不修改保存的偏好；省略时沿用当前“保存后复制”设置。`--output`可指定绝对输出目录，省略时使用保存的录屏目录。

`recording-start`返回的ActiveRunId属于这一次录制；`recording-stop`必须提供该ID，旧ID不能停止新录制。`status`的Recording同时区分当前State/ActiveRunId和LastResult，不能把上一段成片的保存结果当作当前录制已完成。保存耗时较长时会返回saving，应查询status等待对应RunId的LastResult，而非重复开启录制。结果中的Saved、PhysicalCompleted和ResourcesReleased分别表示保存、物理收尾和资源释放。

`--cli settings-close`只关闭没有未保存草稿的设置窗口，不弹确认框。

`--cli settings-open`打开当前安装实例的设置窗口，保留已打开窗口里的草稿，返回该进程和窗口句柄。它不保存设置，也不修改录制参数。

所有回复包含Protocol、RequestId、Success、Status、ExitCode、Data。退出码0表示完成或开始成功，1表示操作失败，2表示参数/协议无效，3表示没有可连接的CLI实例（可能尚未启动或仍是旧版本），4表示忙碌、保存中或当前不可接受，5表示请求可能已经执行但结果未知。5不能当成“没有执行”。

可用`--request-id GUID`固定设置、关闭设置或开始录制的请求身份。同一进程中，同ID、同参数返回原请求结果；换参数会被拒绝。不要换ID自动重试未知写请求。每次托盘进程最多保留128项这类请求结果，满时拒绝新的同类写入，重新正常启动程序后重新建立控制会话；普通GUI不受该缓存限制。停止录制按RunId幂等处理，不占用这份缓存，缓存满仍可停止已开始的录制；重复停止会查询同一录制的收尾结果，旧RunId不能停止新录制。
