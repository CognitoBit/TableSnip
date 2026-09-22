; Inno Setup script for TableSnip.
; Built by publish.ps1 -Installer, which passes:
;   /DAppVersion=1.2.3      version stamp
;   /DSourceDir=<folder>    self-contained publish output to package

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\TableSnip-selfcontained"
#endif
#define AppName "TableSnip"
#define AppExe "TableSnip.exe"
#define AppPublisher "CognitoBit"
#define AppURL "https://github.com/CognitoBit/TableSnip"
; When publish.ps1 could bundle the VC++ runtime DLLs, the download fallback below is not needed.
#define HasVcRuntime FileExists(SourceDir + "\vcruntime140.dll")

[Setup]
AppId={{B7E0E0F2-2B4F-4C1E-9C8E-5D6C0A1F3B21}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
; Per-user by default (no admin prompt); "Install for all users" is offered if the user is an admin.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile=..\src\TableSnip\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
ShowLanguageDialog=no
; The app creates this mutex; setup asks to close a running copy before updating it.
AppMutex=TableSnip.SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "&Open TableSnip when I sign in (keeps Ctrl+Alt+T ready in the tray)"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Snip a table from your screen and paste it into Excel or Sheets"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"" --tray"; Flags: uninsdeletevalue; Tasks: autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#AppName}"; Flags: deletevalue; Tasks: not autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

#if !HasVcRuntime
[Code]
// Tesseract's native library needs the Microsoft Visual C++ 2015-2022 (x64) runtime. Almost every
// PC has it; when it is missing, fetch Microsoft's installer and run it quietly.
const
  VcRedistUrl = 'https://aka.ms/vs/17/release/vc_redist.x64.exe';
  VcRedistHelp = 'TableSnip needs the Microsoft Visual C++ runtime to read text. ' +
                 'You can install it later from ' + VcRedistUrl + ' and then start TableSnip.';

var
  DownloadPage: TDownloadWizardPage;

function VcRedistInstalled: Boolean;
var
  Installed: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed)
            and (Installed = 1);
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID = wpReady) and not VcRedistInstalled then
  begin
    DownloadPage.Clear;
    DownloadPage.Add(VcRedistUrl, 'vc_redist.x64.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        if not Exec(ExpandConstant('{tmp}\vc_redist.x64.exe'), '/install /quiet /norestart', '',
                    SW_SHOW, ewWaitUntilTerminated, ResultCode) then
          MsgBox('Could not start the Visual C++ runtime installer. ' + VcRedistHelp, mbInformation, MB_OK)
        else if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1638) then
          MsgBox('The Visual C++ runtime installer reported code ' + IntToStr(ResultCode) + '. ' + VcRedistHelp, mbInformation, MB_OK);
      except
        if not DownloadPage.AbortedByUser then
          MsgBox('Could not download the Visual C++ runtime. ' + VcRedistHelp, mbInformation, MB_OK);
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;
#endif
