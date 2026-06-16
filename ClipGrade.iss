; Inno Setup script for ClipGrade
; Build with: iscc ClipGrade.iss   (produces Output\ClipGrade-Setup.exe)

#define AppName "ClipGrade"
#define AppVersion "1.1.2"
#define AppPublisher "lucab"
#define AppExe "ClipGrade.exe"

[Setup]
AppId={{B7E6F0A2-1C3D-4E5F-9A8B-0C1D2E3F4A5B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=ClipGrade-Setup
SetupIconFile=clipboard.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "bin\Release\net9.0-windows\win-x64\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

; Make sure the app isn't running during uninstall so the file can be removed.
[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "KillClipGrade"
