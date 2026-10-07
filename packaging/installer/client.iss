; WRApperture client setup (per user, no administrator needed). Built by scripts/build-release.ps1:
;   ISCC /DAppVersion=X.Y.Z /DRepoRoot=<repo> /DSourceDir=<staged client files> /DOutputDir=<dist> /DLanguagesFile=<generated> client.iss
; The languages and the WRApperture-specific texts come from localization/*/installer.json (keys "setup.*").

#ifndef AppVersion
  #error Pass /DAppVersion=X.Y.Z
#endif

[Setup]
AppId={{5F53E7A6-8100-4955-BA54-41FE8D2046A8}
AppName=WRApperture
AppVersion={#AppVersion}
AppVerName=WRApperture {#AppVersion}
AppPublisher=Carlos Landini
AppPublisherURL=https://github.com/Onutirachi/WRApperture
AppSupportURL=https://github.com/Onutirachi/WRApperture/issues
AppUpdatesURL=https://github.com/Onutirachi/WRApperture/releases
VersionInfoVersion={#AppVersion}
; Same folder as the script-based installs of earlier versions, so updating keeps working.
DefaultDirName={localappdata}\Programs\WRApperture
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=WRApperture-Client-Setup-{#AppVersion}
SetupIconFile={#RepoRoot}\assets\icons\WRApperture.ico
UninstallDisplayIcon={app}\WRApperture.exe
UninstallDisplayName=WRApperture
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; The client hides to the tray on close; force-close it so its files can be replaced.
CloseApplications=force
RestartApplications=no
ShowLanguageDialog=auto

#include LanguagesFile

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; Leftovers of the script-based installs (0.x): their uninstall shortcut and console uninstaller.
Type: files; Name: "{userprograms}\Desinstalar WRApperture.lnk"
Type: files; Name: "{userprograms}\Uninstall WRApperture.lnk"
Type: files; Name: "{app}\Desinstalar.cmd"
Type: files; Name: "{app}\Install-WRAppertureClient.ps1"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\WRApperture"; Filename: "{app}\WRApperture.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\WRApperture"; Filename: "{app}\WRApperture.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\WRApperture.exe"; Description: "{cm:LaunchProgram,WRApperture}"; Flags: nowait postinstall skipifsilent
; Silent update started by the client itself: bring it back, in the tray.
Filename: "{app}\WRApperture.exe"; Parameters: "--tray"; Flags: nowait; Check: WizardSilent

[UninstallRun]
; Before the files go: close the client, then let the script undo what the app created (signing setup,
; saved credentials, "start with Windows", app shortcuts) — with the user data only when asked.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM WRApperture.exe"; Flags: runhidden; RunOnceId: "CloseClient"
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Uninstall-WRAppertureClient.ps1"" -InstallDir ""{app}"" -KeepFiles {code:UserDataArgument}"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"

[Code]
var
  RemoveUserData: Boolean;

function InitializeUninstall(): Boolean;
begin
  RemoveUserData := (not UninstallSilent()) and
    (MsgBox(CustomMessage('setup_removeUserDataQuestion'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES);
  Result := True;
end;

function UserDataArgument(Param: String): String;
begin
  if RemoveUserData then Result := '-RemoveUserData' else Result := '';
end;
