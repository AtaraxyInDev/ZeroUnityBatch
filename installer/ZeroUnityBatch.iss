; Instalador de ZeroUnity Batch (Avalonia, self-contained)
; Compilar:  "C:\Users\mrzero\AppData\Local\Programs\Inno Setup 6\ISCC.exe" ZeroUnityBatch.iss

#define AppName "ZeroUnity Batch"
#define AppVersion "1.0.5"
#define AppPublisher "ZeroH Studio"
#define AppExe "ZeroUnityBatch.exe"

[Setup]
AppId={{8C4B1E52-5F3A-4A6E-9C21-7D5B0E6A9F14}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Instalacion por usuario: sin UAC, sin permisos de administrador
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=out
OutputBaseFilename=ZeroUnityBatch-Setup-{#AppVersion}
SetupIconFile=..\src\Zub.App\Assets\zub.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
; ~192 MB de archivos; avisar del espacio
ExtraDiskSpaceRequired=0
MinVersion=10.0

[Languages]
Name: "espanol"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: checkedonce

[Files]
; exe self-contained + classdata.tpk + tools\ (jre, apktool, zipalign, apksigner, Il2CppDumper, keystore)
Source: "stage\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; cache de compilacion de Avalonia y restos de ejecucion, si los hubiera dentro de {app}
Type: filesandordirs; Name: "{app}\tools\Il2CppDumper\DummyDll"
