; Brighter installer (Inno Setup 6). Built by GitHub Actions for every release;
; locally: build-installer.bat. A classic wizard: welcome, licence, "all users or just me",
; folder, Start menu folder, options, ready, install, finish.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "Brighter"
#define AppExe "Brighter.exe"
#define AppUrl "https://github.com/PyePye-Digital/Brighter"

[Setup]
AppId={{1E3E5A48-700A-4984-B75B-9B9F5CC42971}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=PyePye Digital
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
; All users into Program Files (asks for administrator rights) by default; the first page lets
; the user pick "only for me" instead, which installs into %LOCALAPPDATA%\Programs without UAC.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
UsedUserAreasWarning=no
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=no
DisableReadyPage=no
DisableFinishedPage=no
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=Brighter-Setup-{#AppVersion}
SetupIconFile=..\assets\brighter.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
MinVersion=10.0
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[CustomMessages]
english.AutoStartTask=Start Brighter with Windows (hidden in the tray)
turkish.AutoStartTask=Brighter'ı Windows ile başlat (tepside gizli)

[Tasks]
Name: "autostart"; Description: "{cm:AutoStartTask}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The same entry the app's own "Start with Windows" switch writes, so the two stay in sync.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"" --tray"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
// Ask a running copy to quit properly (resets colour temperature, turns monitors back on).
procedure CloseRunningApp(const ExePath: String);
var
  ResultCode: Integer;
begin
  if FileExists(ExePath) then
    Exec(ExePath, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // Upgrade: close the installed copy before its exe is replaced.
  CloseRunningApp(ExpandConstant('{app}\{#AppExe}'));
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    CloseRunningApp(ExpandConstant('{app}\{#AppExe}'));

  if CurUninstallStep = usPostUninstall then
  begin
    // Clean removal: the start-up entry (also when it was switched on from inside the app),
    // and the settings and log folder.
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}');
    DelTree(ExpandConstant('{userappdata}\{#AppName}'), True, True, True);
  end;
end;
