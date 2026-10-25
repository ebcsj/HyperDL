#define AppName "HyperDL"
#define AppVersion "1.0.0.0"
#define AppPublisher "Kerby Chow"

[Setup]
AppId={{B9B178A3-0C48-4E9F-96DF-6CB3BA034AF3}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\HyperDL
DefaultGroupName=HyperDL
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
Uninstallable=yes
UninstallDisplayName=HyperDL
UninstallDisplayIcon={app}\HyperDL.exe
OutputDir=Output
OutputBaseFilename=HyperDL_Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\HyperDL\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\HyperDL"; Filename: "{app}\HyperDL.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall HyperDL"; Filename: "{uninstallexe}"
Name: "{autodesktop}\HyperDL"; Filename: "{app}\HyperDL.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\HyperDL.exe"; Description: "Launch HyperDL"; Flags: nowait postinstall skipifsilent
