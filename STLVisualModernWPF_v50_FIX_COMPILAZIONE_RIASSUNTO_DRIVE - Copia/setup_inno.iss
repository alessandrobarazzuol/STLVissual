; SETUP INNO - STL Visual Modern WPF
; v49 - setup universale corretto
; Crea un unico installer che contiene x64, x86 e ARM64.
; Durante l'installazione copia automaticamente la versione adatta al PC.

#define MyAppName        "STL Visual Modern WPF"
#define MyAppVersion     "4.9"
#define MyAppPublisher   "Alessandro Barazzuol"
#define MyAppURL         "https://www.alessandrobarazzuol.com"
#define MyAppExeName     "STLVisualModernWPF.exe"
#define MyAppIcoName     "app.ico"
#define MyAppUninstallIco "appD.ico"
#define MyAppUninstall   "Disinstalla STL Visual Modern WPF"

[Setup]
AppId={{0F060D28-6435-4D98-8A2A-6DD6589296C1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppCopyright=Copyright (C) Alessandro Barazzuol
VersionInfoCopyright=Copyright (C) Alessandro Barazzuol
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
LicenseFile=licenza.txt
InfoBeforeFile=Info.txt
OutputDir=Output
OutputBaseFilename=STLVisualModernWPF_Setup_v49_UNIVERSALE_CORRETTO
SetupIconFile=STLVisualModernWPF\{#MyAppIcoName}
UninstallDisplayIcon={app}\{#MyAppUninstallIco}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=120
CloseApplications=yes
RestartApplications=no
CloseApplicationsFilter=STLVisualModernWPF.exe
ArchitecturesAllowed=x86compatible x64compatible arm64

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}";
Name: "desktopiconuninstall"; Description: "Crea icona sul desktop per disinstallare il programma"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "STLVisualModernWPF\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "STLVisualModernWPF\appD.ico"; DestDir: "{app}"; Flags: ignoreversion
; Versione x64 per Windows Intel/AMD 64 bit
Source: "STLVisualModernWPF\publish-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsX64Build

; Versione ARM64 per Windows ARM64
Source: "STLVisualModernWPF\publish-win-arm64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsArm64Build

; Versione x86 per Windows 32 bit o fallback
Source: "STLVisualModernWPF\publish-win-x86\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsX86Build

Source: "README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "GUIDE_COMPLETE_v3.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "OVERLOAD_ESEGUIBILI_v4.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "licenza.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "Info.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; \
    Filename: "{app}\{#MyAppExeName}"; \
    IconFilename: "{app}\app.ico"

Name: "{autodesktop}\{#MyAppName}"; \
    Filename: "{app}\{#MyAppExeName}"; \
    IconFilename: "{app}\app.ico"; \
    Tasks: desktopicon

Name: "{userdesktop}\{#MyAppUninstall}"; \
    Filename: "{uninstallexe}"; \
    IconFilename: "{app}\appD.ico"; \
    Tasks: desktopiconuninstallss

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Messages]
ButtonNext=&Avanti

[Code]
function GetProcessorArchitectureText(): String;
var
  A1: String;
  A2: String;
begin
  A1 := LowerCase(GetEnv('PROCESSOR_ARCHITEW6432'));
  A2 := LowerCase(GetEnv('PROCESSOR_ARCHITECTURE'));

  if A1 <> '' then
    Result := A1
  else
    Result := A2;
end;

function IsArm64Build(): Boolean;
var
  A: String;
begin
  A := GetProcessorArchitectureText();
  Result := (Pos('arm64', A) > 0) or (Pos('aarch64', A) > 0);
end;

function IsX64Build(): Boolean;
var
  A: String;
begin
  A := GetProcessorArchitectureText();
  Result := ((Pos('amd64', A) > 0) or (Pos('x64', A) > 0)) and (not IsArm64Build());
end;

function IsX86Build(): Boolean;
begin
  Result := (not IsArm64Build()) and (not IsX64Build());
end;
