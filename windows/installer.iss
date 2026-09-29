#define MyAppName "Baixador de Vídeos e Lives"
#define MyAppVersion "1.0.0"
#define MyAppExeName "BaixadorLivesWindows.exe"

[Setup]
AppId={{A6F09CB4-63E8-4D6A-8BD5-DA02F1EA74F8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\Baixador de Videos e Lives
DefaultGroupName={#MyAppName}
OutputDir=dist
OutputBaseFilename=Instalador_Baixador_Videos_Lives_Windows_v1.0.0
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "tools\*"; DestDir: "{app}\tools"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos adicionais:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir {#MyAppName}"; Flags: nowait postinstall skipifsilent
