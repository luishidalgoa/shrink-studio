; Instalador de "Comprimir vídeos" (Inno Setup).
; La versión se pasa al compilar:  ISCC.exe /DMyAppVersion=0.1.0 shrink-video.iss
; Instala per-user (sin UAC), para que el auto-update no pida admin cada vez.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppName "Comprimir vídeos"
#define MyAppExeName "ShrinkVideo.exe"
#define MyAppPublisher "luishidalgoa"
#define MyAppURL "https://github.com/luishidalgoa/shrink-video"

[Setup]
; AppId FIJO: identifica la app entre versiones -> instalar una nueva actualiza la anterior in-place.
AppId={{45B9197E-ACC6-4BB7-8B9C-AE2180B2429B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppVersion}

; Instalación per-user: sin permisos de administrador (clave para el auto-update fluido)
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\ShrinkVideo
DisableProgramGroupPage=yes
DisableDirPage=yes

OutputDir=Output
OutputBaseFilename=ShrinkVideo-Setup-{#MyAppVersion}
SetupIconFile=..\src\ShrinkVideo\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Cerrar automáticamente la app en ejecución al actualizar (usa el mutex de la app)
CloseApplications=yes
RestartApplications=no
AppMutex=ShrinkVideoSingleInstanceMutex

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el &Escritorio"; GroupDescription: "Accesos directos adicionales:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir {#MyAppName} ahora"; Flags: nowait postinstall skipifsilent
