#define AppName "Production Transcription"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
[Setup]
AppId={{9E753713-7B18-476E-9F1B-40FA6A10B9B4}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\Production Transcription
DefaultGroupName=Production Transcription
OutputDir=..\artifacts\installer
OutputBaseFilename=ProductionTranscription-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\ProductionTranscription.exe
CloseApplications=yes
DisableProgramGroupPage=yes
[Files]
Source: "..\artifacts\windows-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Production Transcription"; Filename: "{app}\ProductionTranscription.exe"
Name: "{autodesktop}\Production Transcription"; Filename: "{app}\ProductionTranscription.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
[Run]
Filename: "{app}\ProductionTranscription.exe"; Description: "Launch Production Transcription"; Flags: nowait postinstall skipifsilent
