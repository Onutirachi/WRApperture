; WRApperture host setup (administrator, whole PC). Built by scripts/build-release.ps1:
;   ISCC /DAppVersion=X.Y.Z /DRepoRoot=<repo> /DSourceDir=<staged host files> /DOutputDir=<dist> /DLanguagesFile=<generated> host.iss
; Files and shortcuts are handled here; the system configuration (agent service, firewall, PATH, Remote Desktop
; preparation) stays in Install-WRAppertureHost.ps1, run hidden with -FromSetup. The languages and the
; WRApperture-specific texts come from localization/*/installer.json (keys "setup.*").

#ifndef AppVersion
  #error Pass /DAppVersion=X.Y.Z
#endif

[Setup]
AppId={{AA0152ED-8EF5-411D-B94D-4E3F77323C72}
AppName=WRApperture (Host)
AppVersion={#AppVersion}
AppVerName=WRApperture (Host) {#AppVersion}
AppPublisher=Carlos Landini
AppPublisherURL=https://github.com/Onutirachi/WRApperture
AppSupportURL=https://github.com/Onutirachi/WRApperture/issues
AppUpdatesURL=https://github.com/Onutirachi/WRApperture/releases
VersionInfoVersion={#AppVersion}
; Same folder as the script-based installs of earlier versions, so updating keeps working.
DefaultDirName={autopf}\WRApperture
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=WRApperture-Host-Setup-{#AppVersion}
SetupIconFile={#RepoRoot}\assets\icons\WRApperture.Host.ico
UninstallDisplayIcon={app}\WRApperture.Host.exe
UninstallDisplayName=WRApperture (Host)
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=force
RestartApplications=no
ShowLanguageDialog=auto

#include LanguagesFile

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; Leftovers of the script-based installs (0.x).
Type: files; Name: "{app}\Instalar.cmd"
Type: files; Name: "{app}\Desinstalar.cmd"
Type: files; Name: "{app}\LEIA-ME.txt"
Type: files; Name: "{app}\README.txt"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{commonprograms}\WRApperture (Host)"; Filename: "{app}\WRApperture.Host.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\WRApperture (Host)"; Filename: "{app}\WRApperture.Host.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\WRApperture.Host.exe"; Description: "{cm:setup_openHost}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Before the files go: service, firewall, PATH and shortcuts; with "undo everything", also the PC preparation
; and the pairing token.
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Uninstall-WRAppertureHost.ps1"" -InstallDir ""{app}"" -KeepFiles {code:PurgeArgument}"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"

[Code]
var
  Purge: Boolean;

{ RemoteApp needs Windows Pro, Enterprise or Education: stop before installing anything on Home ("Core*"). }
function InitializeSetup(): Boolean;
var
  Edition: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion', 'EditionID', Edition) and
     (Pos('core', Lowercase(Edition)) = 1) then
  begin
    SuppressibleMsgBox(CustomMessage('setup_unsupportedEdition'), mbCriticalError, MB_OK, IDOK);
    Result := False;
  end;
end;

{ The agent service and the host app lock their files: stop them before copying (also when the agent itself
  started this setup to update: it is stopped here and the setup keeps running). }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop WRAppertureAgent', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM WRApperture.Host.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM WRApperture.Agent.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(1500);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  Logs: String;
begin
  if CurStep <> ssPostInstall then Exit;
  WizardForm.StatusLabel.Caption := CustomMessage('setup_configuring');
  Logs := ExpandConstant('{commonappdata}\WRApperture\logs');
  if not Exec('powershell.exe',
      '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}') + '\Install-WRAppertureHost.ps1" -InstallDir "' +
      ExpandConstant('{app}') + '" -FromSetup', '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  if Code <> 0 then
  begin
    Log('Install-WRAppertureHost.ps1 exited with ' + IntToStr(Code));
    if not WizardSilent() then
      SuppressibleMsgBox(FmtMessage(CustomMessage('setup_configureFailed'), [IntToStr(Code), Logs]), mbError, MB_OK, IDOK);
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Purge := (not UninstallSilent()) and
    (MsgBox(CustomMessage('setup_purgeQuestion'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES);
  Result := True;
end;

function PurgeArgument(Param: String): String;
begin
  if Purge then Result := '-Purge' else Result := '';
end;
