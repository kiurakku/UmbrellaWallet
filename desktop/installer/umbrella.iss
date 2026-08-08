; Inno Setup script for Umbrella Wallet (desktop).
; Produces a Windows installer that lets the user choose the install folder,
; create a desktop / Start-menu shortcut, and uninstall cleanly.

#define AppName "Umbrella Wallet"
#define AppVersion "2.8.6"
#define AppPublisher "the fear"
#define AppExe "Umbrella.exe"

[Setup]
AppId={{7C1B0E2A-0B7E-4E9A-9C2E-UMBRELLA0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Umbrella Wallet
DefaultGroupName=Umbrella Wallet
DisableProgramGroupPage=no
AllowNoIcons=yes
; This is what gives the "choose where to install" page:
DisableDirPage=no
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=D:\umbrella-dist
OutputBaseFilename=UmbrellaWallet-Setup-{#AppVersion}
SetupIconFile=D:\WorkSpace - IT\Umbra Wallet\desktop\src\Umbrella.Wallet.App\Assets\umbrella.ico
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
Source: "D:\umbrella-dist\app\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Umbrella Wallet"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall Umbrella Wallet"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Umbrella Wallet"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch Umbrella Wallet"; Flags: nowait postinstall skipifsilent
