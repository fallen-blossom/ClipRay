#ifndef StagingDir
  #error StagingDir must be defined
#endif
#ifndef OutputDir
  #error OutputDir must be defined
#endif
#ifndef SetupFlavor
  #define SetupFlavor "Lite"
#endif
#define AppVersion "1.0.6"
#define AppExeName "ClipRay.App.exe"
#define AppUninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{C5C7914B-7566-4912-A9E0-4D09E724BDA1}_is1"

[Setup]
AppId={{C5C7914B-7566-4912-A9E0-4D09E724BDA1}
AppName=ClipRay 截光
AppVersion={#AppVersion}
AppVerName=ClipRay 截光 {#AppVersion}
AppPublisher=落花
UninstallDisplayName=ClipRay 截光
DefaultDirName={localappdata}\Programs\ClipRay
DefaultGroupName=ClipRay 截光
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=ClipRay-{#SetupFlavor}-Setup-{#AppVersion}-win-x64
SetupIconFile={#StagingDir}\Assets\ClipRay-v2.ico
UninstallDisplayIcon={app}\app\Assets\ClipRay-v2.ico
LicenseFile={#StagingDir}\LICENSE
Compression=lzma2/fast
SolidCompression=no
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=落花
VersionInfoProductName=ClipRay 截光
VersionInfoDescription=ClipRay 截图、长截图、录屏与本地翻译工具

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "{#SourcePath}\Languages\ChineseSimplified.isl"

[CustomMessages]
en.DesktopShortcut=Create a desktop shortcut
chinesesimp.DesktopShortcut=创建桌面快捷方式
en.ShortcutsGroup=Shortcuts:
chinesesimp.ShortcutsGroup=快捷方式：
en.StartupTask=Start ClipRay when signing in to Windows
chinesesimp.StartupTask=登录 Windows 后自动启动 ClipRay
en.StartupGroup=Startup settings:
chinesesimp.StartupGroup=启动设置：
en.CleanupTask=Enable Windows scheduled cleanup (after 30 days, daily at 21:00)
chinesesimp.CleanupTask=启用 Windows 自动清理（30 天、每天 21:00）
en.CleanupGroup=Cleanup settings:
chinesesimp.CleanupGroup=清理设置：
en.LaunchSettings=Launch ClipRay and open Settings
chinesesimp.LaunchSettings=启动 ClipRay 并打开设置
en.UpgradeDirectory=An existing ClipRay installation was found. Upgrade in its current folder to keep settings and output locations.
chinesesimp.UpgradeDirectory=检测到已安装的 ClipRay。请在原目录升级，保留设置和成果位置。
en.DeveloperCaption=ClipRay · Developer: 落花 φ(>ω<*)
chinesesimp.DeveloperCaption=ClipRay 截光 · 开发者：落花 φ(>ω<*)
en.ChooseInstallDirectory=Choose installation folder
chinesesimp.ChooseInstallDirectory=选择安装目录
en.CleanupSetupFailed=ClipRay was installed, but Windows scheduled cleanup could not be enabled. The option remains off. Open ClipRay Settings to try again.
chinesesimp.CleanupSetupFailed=ClipRay 已安装，但 Windows 自动清理未能启用。设置保持为未启用，请打开 ClipRay 设置后重试。
en.UninstallTitle=Uninstall ClipRay
chinesesimp.UninstallTitle=卸载 ClipRay
en.UninstallSummary=Before uninstalling ClipRay, confirm the following:%n%nChoose Yes to uninstall or No to cancel.%nThe running ClipRay process will close before removal.%nThe app, settings, cost statistics, temporary data, scheduled tasks, startup entry, and Windows credentials will be removed.%nScreenshots, long captures, and recordings are kept by default.
chinesesimp.UninstallSummary=卸载 ClipRay 前，请确认以下内容。%n%n点击“是”卸载 ClipRay；点击“否”取消。%n卸载开始时会先关闭正在运行的 ClipRay。%n程序、应用设置、费用统计、临时数据、计划任务、开机启动项和 Windows 凭据将被删除。%n截图、长截图和录屏默认保留。
en.RecycleOutputs=Move the entire default ClipRay Outputs folder to the Recycle Bin (custom folders are unaffected)
chinesesimp.RecycleOutputs=将 ClipRay 默认 Outputs 文件夹整体移到回收站（自定义目录不受影响）
en.YesButton=Yes (Y)
chinesesimp.YesButton=是(Y)
en.NoButton=No (N)
chinesesimp.NoButton=否(N)
en.CleanupStartFailed=ClipRay uninstall cleanup could not start. Uninstallation has stopped. Try again.
chinesesimp.CleanupStartFailed=无法启动 ClipRay 卸载清理，卸载已停止。请重试。
en.OutputsRecycleFailed=The default ClipRay Outputs folder could not be moved to the Recycle Bin. Uninstallation has stopped. Close programs using its files and try again.
chinesesimp.OutputsRecycleFailed=ClipRay 默认 Outputs 文件夹未能整体移入回收站，卸载已停止。请关闭正在使用其中文件的程序后重试。
en.UninstallCleanupFailed=ClipRay uninstall cleanup did not finish. Uninstallation has stopped. Restart Windows and try again; if it still fails, keep this message and contact the developer.
chinesesimp.UninstallCleanupFailed=ClipRay 卸载清理未能完成，卸载已停止。请重新启动电脑后重试；若仍失败，请保留此提示并联系开发者。
en.InstallExitFailed=ClipRay could not confirm that this installation has exited. Finish active work and exit ClipRay from its tray menu, then retry. You do not need to uninstall. No process will be force terminated.
chinesesimp.InstallExitFailed=无法确认此安装的 ClipRay 已退出。请完成当前操作，从托盘退出后重试，无需卸载。不会强制终止进程。

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:ShortcutsGroup}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartupTask}"; GroupDescription: "{cm:StartupGroup}"; Flags: unchecked; Check: IsFreshInstall
Name: "cleanup"; Description: "{cm:CleanupTask}"; GroupDescription: "{cm:CleanupGroup}"; Flags: unchecked; Check: IsFreshInstall

[Files]
Source: "{#StagingDir}\*"; DestDir: "{app}\app"; Excludes: "licenses\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StagingDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{app}\Outputs\Screenshots"
Name: "{app}\Outputs\LongCaptures"
Name: "{app}\Outputs\Recordings"

[Icons]
Name: "{group}\ClipRay 截光"; Filename: "{app}\app\{#AppExeName}"; WorkingDir: "{app}\app"; IconFilename: "{app}\app\Assets\ClipRay-v2.ico"
Name: "{autodesktop}\ClipRay 截光"; Filename: "{app}\app\{#AppExeName}"; WorkingDir: "{app}\app"; IconFilename: "{app}\app\Assets\ClipRay-v2.ico"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ClipRay"; ValueData: """{app}\app\{#AppExeName}"" --startup"; Flags: uninsdeletevalue; Tasks: startup; Check: IsFreshInstall

[Run]
Filename: "{app}\app\{#AppExeName}"; Parameters: "--open-settings"; Description: "{cm:LaunchSettings}"; Flags: nowait postinstall skipifsilent

[Code]
const
  ErrorAlreadyExists = 183;

function CreateMutex(lpMutexAttributes: Longint; bInitialOwner: Boolean; lpName: String): Longint;
  external 'CreateMutexW@kernel32.dll stdcall';
function GetLastError: Cardinal;
  external 'GetLastError@kernel32.dll stdcall';
function CreateFileForUpdate(lpFileName: String; dwDesiredAccess, dwShareMode: Cardinal;
  lpSecurityAttributes: Longint; dwCreationDisposition, dwFlagsAndAttributes: Cardinal;
  hTemplateFile: Longint): Longint;
  external 'CreateFileW@kernel32.dll stdcall';
function CloseUpdateHandle(hObject: Longint): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

var
  RecycleOutputs: Boolean;
  SetupMutexHandle: Longint;
  UpgradeMutexHandle: Longint;
  DeveloperLabel: TNewStaticText;
  LicenseDescriptionLabel: TNewStaticText;
  UpgradeInstallLocation: String;
  IsUpgrade: Boolean;

procedure BrowseDirectoryButtonClick(Sender: TObject); forward;
procedure BrowseInstallationDirectory; forward;

function IsAnotherClipRaySetupRunning: Boolean;
begin
  SetupMutexHandle := CreateMutex(0, False, 'Local\ClipRaySetup-C5C7914B-7566-4912-A9E0-4D09E724BDA1');
  Result := GetLastError = ErrorAlreadyExists;
end;

function ExistingInstallLocation: String;
var
  InstallLocation: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, '{#AppUninstallKey}', 'InstallLocation', InstallLocation) and
     (Trim(InstallLocation) <> '') then
    Result := InstallLocation;
end;

function InitializeSetup(): Boolean;
begin
  if IsAnotherClipRaySetupRunning then
  begin
    Result := False;
    exit;
  end;

  UpgradeInstallLocation := ExistingInstallLocation;
  IsUpgrade := UpgradeInstallLocation <> '';
  Result := True;
end;

function IsFreshInstall(): Boolean;
begin
  Result := not IsUpgrade;
end;

procedure ReleaseUpgradeLock;
begin
  if UpgradeMutexHandle <> 0 then
  begin
    CloseUpdateHandle(UpgradeMutexHandle);
    UpgradeMutexHandle := 0;
  end;
end;

procedure DeinitializeSetup;
begin
  ReleaseUpgradeLock;
  if SetupMutexHandle <> 0 then CloseUpdateHandle(SetupMutexHandle);
end;

function CanReplaceInstalledExecutable(const FileName: String): Boolean;
var
  FileHandle: Longint;
begin
  { Open for write without writing: running image mappings must not be overwritten. }
  FileHandle := CreateFileForUpdate(FileName, $40000000, 0, 0, 3, $80, 0);
  Result := FileHandle <> -1;
  if Result then CloseUpdateHandle(FileHandle);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstalledExecutable: String;
  ProtocolMarker: AnsiString;
  ExitRequestCode: Integer;
begin
  Result := '';
  if IsUpgrade and (CompareText(AddBackslash(ExpandConstant('{app}')),
      AddBackslash(UpgradeInstallLocation)) <> 0) then
  begin
    Result := CustomMessage('UpgradeDirectory');
    exit;
  end;
  InstalledExecutable := ExpandConstant('{app}\app\{#AppExeName}');
  if UpgradeMutexHandle = 0 then
    UpgradeMutexHandle := CreateMutex(0, False, 'Local\ClipRayUpgrade-C5C7914B-7566-4912-A9E0-4D09E724BDA1');
  if UpgradeMutexHandle = 0 then
  begin
    Result := CustomMessage('InstallExitFailed');
    exit;
  end;
  if not FileExists(InstalledExecutable) then exit;
  { Also preserve data when upgrading an unregistered/portable installation. }
  IsUpgrade := True;
  if UpgradeInstallLocation = '' then UpgradeInstallLocation := ExpandConstant('{app}');
  if LoadStringFromFile(ExpandConstant('{app}\app\install-exit-protocol.txt'), ProtocolMarker)
      and (Trim(ProtocolMarker) = 'ClipRay.InstallExit.1') then
  begin
    if (not Exec(InstalledExecutable, '--request-install-exit', ExpandConstant('{app}\app'),
        SW_HIDE, ewWaitUntilTerminated, ExitRequestCode)) or (ExitRequestCode <> 0) then
    begin
      Result := CustomMessage('InstallExitFailed');
      ReleaseUpgradeLock;
      exit;
    end;
  end;
  { Legacy versions without the protocol can upgrade after a manual tray exit. }
  if not CanReplaceInstalledExecutable(InstalledExecutable) then
  begin
    Result := CustomMessage('InstallExitFailed');
    ReleaseUpgradeLock;
  end;
end;

procedure InitializeWizard;
begin
  if IsUpgrade then
  begin
    WizardForm.DirEdit.Text := UpgradeInstallLocation;
    WizardForm.DirEdit.Enabled := False;
    WizardForm.DirBrowseButton.Enabled := False;
  end;
  LicenseDescriptionLabel := TNewStaticText(WizardForm.FindComponent('LicenseLabel3'));
  if LicenseDescriptionLabel <> nil then LicenseDescriptionLabel.Visible := False;

  DeveloperLabel := TNewStaticText.Create(WizardForm);
  DeveloperLabel.Parent := WizardForm;
  DeveloperLabel.Caption := CustomMessage('DeveloperCaption');
  DeveloperLabel.Font.Size := 17;
  DeveloperLabel.Font.Style := [fsBold];
  DeveloperLabel.AutoSize := True;
  DeveloperLabel.Left := 68;
  DeveloperLabel.Top := 70;
  DeveloperLabel.Visible := True;

  WizardForm.DirBrowseButton.OnClick := @BrowseDirectoryButtonClick;

end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if DeveloperLabel <> nil then DeveloperLabel.Visible := CurPageID = wpLicense;
end;

procedure BrowseDirectoryButtonClick(Sender: TObject);
begin
  BrowseInstallationDirectory;
end;

procedure BrowseInstallationDirectory;
var
  SelectedPath: String;
begin
  SelectedPath := '';
  if BrowseForFolder(CustomMessage('ChooseInstallDirectory'), SelectedPath, True) then
    WizardForm.DirEdit.Text := SelectedPath;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ConfigurationExitCode: Integer;
begin
  if (CurStep = ssInstall) and FileExists(ExpandConstant('{app}\app\{#AppExeName}')) and
      (not CanReplaceInstalledExecutable(ExpandConstant('{app}\app\{#AppExeName}'))) then
  begin
    MsgBox(CustomMessage('InstallExitFailed'), mbError, MB_OK);
    Abort;
  end;
  { Release before post-install helpers or the optional launch checkbox execute. }
  if CurStep = ssPostInstall then ReleaseUpgradeLock;
  if (CurStep = ssPostInstall) and IsFreshInstall() and WizardIsTaskSelected('cleanup') then
  begin
    if (not Exec(
      ExpandConstant('{app}\app\{#AppExeName}'),
      '--configure-install-cleanup',
      ExpandConstant('{app}\app'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ConfigurationExitCode)) or (ConfigurationExitCode <> 0) then
    begin
      MsgBox(CustomMessage('CleanupSetupFailed'), mbError, MB_OK);
    end;
  end;
end;

function InitializeUninstall(): Boolean;
var
  Dialog: TSetupForm;
  Description: TNewStaticText;
  RecycleOutputsCheck: TNewCheckBox;
  YesButton, NoButton: TNewButton;
begin
  Dialog := CreateCustomForm(ScaleX(660), ScaleY(360), False, False);
  try
    Dialog.Caption := CustomMessage('UninstallTitle');
    Description := TNewStaticText.Create(Dialog);
    Description.Parent := Dialog;
    Description.Caption := CustomMessage('UninstallSummary');
    Description.Left := ScaleX(22);
    Description.Top := ScaleY(22);
    Description.Width := Dialog.ClientWidth - ScaleX(44);
    Description.Height := ScaleY(200);
    Description.WordWrap := True;

    RecycleOutputsCheck := TNewCheckBox.Create(Dialog);
    RecycleOutputsCheck.Parent := Dialog;
    RecycleOutputsCheck.Caption := CustomMessage('RecycleOutputs');
    RecycleOutputsCheck.Left := Description.Left;
    RecycleOutputsCheck.Top := Description.Top + Description.Height + ScaleY(10);
    RecycleOutputsCheck.Width := Description.Width;
    RecycleOutputsCheck.Height := ScaleY(34);
    RecycleOutputsCheck.Checked := False;

    YesButton := TNewButton.Create(Dialog);
    YesButton.Parent := Dialog;
    YesButton.Caption := CustomMessage('YesButton');
    YesButton.ModalResult := mrYes;
    YesButton.Left := Dialog.ClientWidth - ScaleX(196);
    YesButton.Top := Dialog.ClientHeight - ScaleY(48);
    YesButton.Width := ScaleX(82);

    NoButton := TNewButton.Create(Dialog);
    NoButton.Parent := Dialog;
    NoButton.Caption := CustomMessage('NoButton');
    NoButton.ModalResult := mrNo;
    NoButton.Left := Dialog.ClientWidth - ScaleX(102);
    NoButton.Top := YesButton.Top;
    NoButton.Width := YesButton.Width;
    NoButton.Default := True;

    Result := Dialog.ShowModal = mrYes;
    RecycleOutputs := Result and RecycleOutputsCheck.Checked;
  finally
    Dialog.Free;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExitRequestCode: Integer;
  CleanupExitCode: Integer;
  CleanupArguments: String;
  ProtocolMarker: AnsiString;
begin
  if CurUninstallStep = usUninstall then
  begin
    if (not LoadStringFromFile(ExpandConstant('{app}\app\install-exit-protocol.txt'), ProtocolMarker)) or
       (Trim(ProtocolMarker) <> 'ClipRay.InstallExit.1') then
    begin
      MsgBox(CustomMessage('InstallExitFailed'), mbError, MB_OK);
      Abort;
    end;
    { 卸载时才读取复选框；[UninstallRun] 中的 scripted constant 会在安装时固化。 }
    if (not Exec(
      ExpandConstant('{app}\app\{#AppExeName}'),
      '--request-install-exit',
      ExpandConstant('{app}\app'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ExitRequestCode)) or (ExitRequestCode <> 0) then
    begin
      MsgBox(CustomMessage('InstallExitFailed'), mbError, MB_OK);
      Abort;
    end;

    CleanupArguments := '--uninstall-cleanup';
    if RecycleOutputs then
      CleanupArguments := CleanupArguments + ' --recycle-outputs';

    if not Exec(
      ExpandConstant('{app}\app\{#AppExeName}'),
      CleanupArguments,
      ExpandConstant('{app}\app'),
      SW_HIDE,
      ewWaitUntilTerminated,
      CleanupExitCode) then
    begin
      MsgBox(CustomMessage('CleanupStartFailed'), mbError, MB_OK);
      Abort;
    end;

    if CleanupExitCode <> 0 then
    begin
      if CleanupExitCode = 3 then
        MsgBox(CustomMessage('InstallExitFailed'), mbError, MB_OK)
      else if CleanupExitCode = 2 then
        MsgBox(CustomMessage('OutputsRecycleFailed'), mbError, MB_OK)
      else
        MsgBox(CustomMessage('UninstallCleanupFailed'), mbError, MB_OK);
      Abort;
    end;
  end;
end;
