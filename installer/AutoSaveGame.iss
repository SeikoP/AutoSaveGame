#ifndef AppVersion
  #error AppVersion must be provided by Build-Installer.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be provided by Build-Installer.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be provided by Build-Installer.ps1
#endif

[Setup]
AppId={{F532D19B-8DB1-44A8-9E03-96C4FE725F10}
AppName=AutoSaveGame
AppVersion={#AppVersion}
AppVerName=AutoSaveGame {#AppVersion}
AppPublisher=SeikoP
AppPublisherURL=https://github.com/SeikoP/AutoSaveGame
AppSupportURL=https://github.com/SeikoP/AutoSaveGame/issues
DefaultDirName={localappdata}\Programs\AutoSaveGame
DefaultGroupName=AutoSaveGame
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=AutoSaveGame-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=AutoSaveGame
UninstallDisplayIcon={app}\AutoSaveGame.exe
VersionInfoVersion={#AppVersion}
WizardStyle=modern
SetupIconFile={#SourcePath}\..\src\AutoSaveGame.App\Assets\AutoSaveGame.ico
CloseApplications=force
RestartApplications=yes

[Languages]
; Translation source: LenovoLegionToolkit InnoDependencies/Languages/Vietnamese.isl
Name: "vietnamese"; MessagesFile: "{#SourcePath}\Languages\Vietnamese.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\AutoSaveGame"; Filename: "{app}\AutoSaveGame.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\AutoSaveGame.exe"; Description: "Mở AutoSaveGame"; Flags: nowait postinstall skipifsilent
