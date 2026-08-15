; Inno Setup script for Umbrella Wallet (desktop).
; Produces a Windows installer that lets the user choose the install folder,
; create a desktop / Start-menu shortcut, and uninstall cleanly.

#define AppName "Umbrella Wallet"
#define AppVersion "4.2.1"
#define AppPublisher "the fear"
#define AppExe "Umbrella.exe"

; SourceDir = the published `app` folder; DistDir = where the installer is written.
; Both default to the local D:\umbrella-dist layout but can be overridden on CI, e.g.
;   ISCC /DSourceDir=dist\app /DDistDir=dist desktop\installer\umbrella.iss
#ifndef SourceDir
  #define SourceDir "D:\umbrella-dist\app"
#endif
#ifndef DistDir
  #define DistDir "D:\umbrella-dist"
#endif

#define AppUrl "https://t.me/UmbrellaWallet"
#define AppReleases "https://github.com/kiurakku/umbrella-wallet/releases"

[Setup]
AppId={{7C1B0E2A-0B7E-4E9A-9C2E-UMBRELLA0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppReleases}
AppContact={#AppUrl}
AppComments=Self-custody, non-custodial crypto wallet. Your keys are generated and encrypted on this device and never leave it.
VersionInfoDescription={#AppName} — self-custody crypto wallet
VersionInfoProductName={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
DefaultDirName={autopf}\Umbrella Wallet
DefaultGroupName=Umbrella Wallet
DisableProgramGroupPage=no
AllowNoIcons=yes
; This is what gives the "choose where to install" page:
DisableDirPage=no
; Show the licence so a first-time downloader sees the terms before installing.
LicenseFile=..\..\LICENSE
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#DistDir}
OutputBaseFilename=UmbrellaWallet-Setup-{#AppVersion}
SetupIconFile=..\src\Umbrella.Wallet.App\Assets\umbrella.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
; Excludes: never ship a runtime `data` folder — that would overwrite the user's wallets/settings on
; update. The user's data lives in {app}\data (portable) or %APPDATA%\UmbrellaWallet and is never touched.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "data\*,data"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Umbrella Wallet"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall Umbrella Wallet"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Umbrella Wallet"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch Umbrella Wallet"; Flags: nowait postinstall skipifsilent

[Code]
// A wallet must never silently destroy funds on uninstall. We deliberately leave the encrypted
// vault in place (so a reinstall restores everything) and tell the user exactly where it is and
// how to erase it themselves if they truly want to.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    MsgBox('Umbrella Wallet has been removed.' + #13#10 + #13#10 +
      'Your wallet data was NOT deleted — it stays encrypted on this PC in one of:' + #13#10 +
      '   • ' + ExpandConstant('{app}\data') + #13#10 +
      '   • ' + ExpandConstant('{userappdata}\UmbrellaWallet') + #13#10 + #13#10 +
      'Reinstalling restores your wallets, theme and settings automatically.' + #13#10 + #13#10 +
      'To erase everything, delete that folder yourself — but first make sure your 24-word ' +
      'recovery phrase is written down, or the funds in that wallet are lost forever.',
      mbInformation, MB_OK);
  end;
end;
