; Instalador de ShrinkStudio (Inno Setup).
; La versión se pasa al compilar:  ISCC.exe /DMyAppVersion=0.2.1 shrink-video.iss
; Instala per-user (sin UAC), para que el auto-update no pida admin cada vez.
; Detecta FFmpeg y, si falta, lo descarga e instala junto a la app.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppName "ShrinkStudio"
#define MyAppExeName "ShrinkVideo.exe"
#define MyAppPublisher "luishidalgoa"
#define MyAppURL "https://github.com/luishidalgoa/shrink-studio"
#define FfmpegUrl "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"

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
OutputBaseFilename=ShrinkStudio-Setup-{#MyAppVersion}
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
; Solo aparece (y marcada) si NO se detecta FFmpeg en el sistema.
Name: "installffmpeg"; Description: "Descargar e instalar FFmpeg (no detectado — necesario para funcionar)"; GroupDescription: "Dependencias:"; Check: NeedsFfmpeg

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir {#MyAppName} ahora"; Flags: nowait postinstall skipifsilent

[Code]
var
  FfmpegChecked: Boolean;
  FfmpegPresent: Boolean;

{ True si 'ffmpeg' está en el PATH (where ffmpeg -> codigo 0). Se cachea. }
function FfmpegInPath(): Boolean;
var
  ResultCode: Integer;
begin
  if not FfmpegChecked then begin
    FfmpegPresent := Exec('cmd.exe', '/C where ffmpeg', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
    FfmpegChecked := True;
  end;
  Result := FfmpegPresent;
end;

{ Check de la tarea installffmpeg: la muestra (marcada) solo si FALTA ffmpeg. }
function NeedsFfmpeg(): Boolean;
begin
  Result := not FfmpegInPath();
end;

{ Tras copiar la app: descarga el zip de FFmpeg y copia ffmpeg/ffprobe a la carpeta ffmpeg de la app.
  Funciona en instalacion normal y desatendida. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Ps: String;
begin
  if (CurStep <> ssPostInstall) or (not WizardIsTaskSelected('installffmpeg')) then
    Exit;

  WizardForm.StatusLabel.Caption := 'Descargando FFmpeg (una sola vez)...';
  try
    DownloadTemporaryFile('{#FfmpegUrl}', 'ffmpeg.zip', '', nil);
  except
    SuppressibleMsgBox('No se pudo descargar FFmpeg. Puedes instalarlo luego con: winget install Gyan.FFmpeg',
      mbError, MB_OK, IDOK);
    Exit;
  end;

  WizardForm.StatusLabel.Caption := 'Instalando FFmpeg...';
  Ps := '-NoProfile -ExecutionPolicy Bypass -Command "' +
    '$ErrorActionPreference=''Stop''; ' +
    '$z=''' + ExpandConstant('{tmp}\ffmpeg.zip') + '''; ' +
    '$d=''' + ExpandConstant('{tmp}\ffx') + '''; ' +
    'Expand-Archive -LiteralPath $z -DestinationPath $d -Force; ' +
    '$b=(Get-ChildItem $d -Recurse -Filter ffmpeg.exe | Select-Object -First 1).DirectoryName; ' +
    '$o=''' + ExpandConstant('{app}\ffmpeg') + '''; New-Item -ItemType Directory -Force $o | Out-Null; ' +
    'Copy-Item (Join-Path $b ''ffmpeg.exe'') $o -Force; ' +
    'Copy-Item (Join-Path $b ''ffprobe.exe'') $o -Force"';
  if (not Exec('powershell.exe', Ps, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)) or (ResultCode <> 0) then
    SuppressibleMsgBox('No se pudo preparar FFmpeg (codigo ' + IntToStr(ResultCode) + '). ' +
      'La app funcionara si instalas FFmpeg manualmente (winget install Gyan.FFmpeg).',
      mbError, MB_OK, IDOK);
end;
