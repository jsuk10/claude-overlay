; Windows 설치 파일. release.yml이 publish한 exe로 만든다:
;   iscc /DAppVersion=0.2.4 /DSourceExe=publish\ClaudeSessionOverlay.exe /O..\dist installer\ClaudeSessionOverlay.iss
; 관리자 권한 없이 %LOCALAPPDATA%\Programs에 설치하고, 시작 프로그램은 앱과 같은 HKCU Run 값을 쓴다.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\ClaudeSessionOverlay.exe"
#endif

#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#define RunName "ClaudeSessionOverlay"

[Setup]
AppId={{6B0C7E52-3F1A-4C8E-9D2B-5A7F4E1C0D93}
AppName=Claude Session Overlay
AppVersion={#AppVersion}
AppPublisher=jsuk10
AppPublisherURL=https://github.com/jsuk10/claude-overlay
DefaultDirName={localappdata}\Programs\ClaudeSessionOverlay
DefaultGroupName=Claude Session Overlay
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=ClaudeSessionOverlay-Setup
SetupIconFile=..\assets\icon\app.ico
UninstallDisplayIcon={app}\ClaudeSessionOverlay.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UsePreviousTasks=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ko"; MessagesFile: "compiler:Languages\Korean.isl"

[CustomMessages]
en.Autostart=Start with Windows
ko.Autostart=Windows 시작 시 실행
en.Options=Options:
ko.Options=옵션:

[Tasks]
Name: "autostart"; Description: "{cm:Autostart}"; GroupDescription: "{cm:Options}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.ko.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\mod\*"; DestDir: "{app}\mod"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Claude Session Overlay"; Filename: "{app}\ClaudeSessionOverlay.exe"
Name: "{autodesktop}\Claude Session Overlay"; Filename: "{app}\ClaudeSessionOverlay.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ClaudeSessionOverlay.exe"; Description: "{cm:LaunchProgram,Claude Session Overlay}"; Flags: nowait postinstall skipifsilent

[Code]
var
  IsTaskDefaultSet: Boolean;

procedure StopOverlay;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ClaudeSessionOverlay.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// 다시 설치할 때 시작 프로그램을 꺼 둔 사람이면 체크를 풀어 둔다
procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectTasks) and not IsTaskDefaultSet and not WizardSilent then
  begin
    IsTaskDefaultSet := True;
    if (WizardForm.PrevAppDir <> '') and not RegValueExists(HKCU, '{#RunKey}', '{#RunName}') then
      WizardSelectTasks('!autostart');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopOverlay;
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then exit;
  if WizardIsTaskSelected('autostart') then
    RegWriteStringValue(HKCU, '{#RunKey}', '{#RunName}', '"' + ExpandConstant('{app}\ClaudeSessionOverlay.exe') + '"')
  else
    RegDeleteValue(HKCU, '{#RunKey}', '{#RunName}');
end;

function InitializeUninstall(): Boolean;
begin
  StopOverlay;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, '{#RunKey}', '{#RunName}');
end;
