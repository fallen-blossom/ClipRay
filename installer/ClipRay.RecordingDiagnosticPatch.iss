#ifndef PatchedDll
  #error PatchedDll must be defined
#endif
#ifndef PatchIcon
  #error PatchIcon must be defined
#endif
#ifndef OutputDir
  #error OutputDir must be defined
#endif
#define RequiredAppVersion "1.0.2"
#define MainAppExeName "ClipRay.App.exe"
#define MainAppUninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{C5C7914B-7566-4912-A9E0-4D09E724BDA1}_is1"

[Setup]
AppId={{E6157567-73D5-4A08-8073-03E44504A82C}
AppName=ClipRay 录屏诊断补丁
AppVersion={#RequiredAppVersion}
AppVerName=ClipRay 录屏诊断补丁（适用于 {#RequiredAppVersion}）
AppPublisher=落花
DefaultDirName={code:GetInstalledAppDirectory}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=ClipRay-Recording-Diagnostic-Patch-{#RequiredAppVersion}-2026-08-22
SetupIconFile={#PatchIcon}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=no
CreateUninstallRegKey=no
Uninstallable=no
VersionInfoVersion={#RequiredAppVersion}.0
VersionInfoCompany=落花
VersionInfoProductName=ClipRay 录屏诊断补丁
VersionInfoDescription=为已安装的 ClipRay 1.0.2 添加录屏启动阶段诊断

[Languages]
Name: "chinesesimp"; MessagesFile: "{#SourcePath}\Languages\ChineseSimplified.isl"

[Files]
Source: "{#PatchedDll}"; DestDir: "{app}\app"; DestName: "ClipRay.App.dll"; Flags: ignoreversion

[Run]
Filename: "{app}\app\{#MainAppExeName}"; Description: "启动 ClipRay"; Flags: nowait postinstall skipifsilent

[Code]
function ExistingInstallLocation: String;
var
  InstallLocation: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, '{#MainAppUninstallKey}', 'InstallLocation', InstallLocation) and
     FileExists(AddBackslash(InstallLocation) + 'app\{#MainAppExeName}') and
     FileExists(AddBackslash(InstallLocation) + 'app\ClipRay.App.dll') then
    Result := RemoveBackslashUnlessRoot(InstallLocation);
end;
function ExistingDisplayVersion: String;
begin
  Result := '';
  RegQueryStringValue(HKCU, '{#MainAppUninstallKey}', 'DisplayVersion', Result);
end;

function GetInstalledAppDirectory(Param: String): String;
begin
  Result := ExistingInstallLocation;
end;

function InitializeSetup(): Boolean;
var
  InstallLocation: String;
  DisplayVersion: String;
begin
  InstallLocation := ExistingInstallLocation;
  DisplayVersion := ExistingDisplayVersion;
  Result := (InstallLocation <> '') and (CompareText(DisplayVersion, '{#RequiredAppVersion}') = 0);
  if not Result then
    MsgBox(
      '录屏诊断补丁只适用于已安装的 ClipRay 1.0.2。' + #13#10 + #13#10 +
      '没有找到对应版本，未修改任何文件。请安装完整版本后再运行此补丁。',
      mbError,
      MB_OK);
end;
