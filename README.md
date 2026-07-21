<p align="center">
  <img src="docs/icon.png" alt="ShrinkStudio" width="128">
</p>

<h1 align="center">ShrinkStudio</h1>

<p align="center">
  Compresor de vídeo por lotes, pensado como <b>relevo ligero de HandBrake</b>:
  reduce series, películas y capítulos a <b>H.265 / H.264 / AV1</b> en <b>MKV, MP4 o WebM</b>,
  con aceleración por hardware y conservando solo los idiomas de audio que quieras.
</p>

<p align="center">
  <b>App de escritorio para Windows</b> · <b>herramienta de terminal para Linux, macOS y Windows</b>
</p>

---

Su foco es el **ahorro de almacenamiento**: reduce típicamente **un 80–90 %** el tamaño manteniendo muy
buena calidad visual. Antes de comprimir te muestra un **pronóstico** del tamaño final y del ahorro, y si
quieres afinarlo puedes **medirlo de verdad**: codifica varias muestras cortas con tus ajustes y calcula
el peso real.

Frente a HandBrake destaca en **procesamiento por lotes**: analiza una carpeta entera, muestra las pistas
de cada vídeo y comprime en tanda desatendida. **Nunca toca los originales** salvo que se lo pidas
explícitamente, y en ese caso van a la papelera, nunca a borrado definitivo.

> Cambios de cada versión: [`CHANGELOG.md`](CHANGELOG.md) ·
> Roadmap de funcionalidades heredadas de HandBrake: [`ROADMAP.md`](ROADMAP.md).

## Qué sabe hacer

- **Lotes de verdad.** Selección estilo explorador en la tabla: arrastra en banda, `Ctrl`/`Mayús`+clic,
  `Ctrl+A`. Se procesa lo que esté seleccionado. `Supr` quita de la lista (sin tocar el archivo) y el
  botón derecho abre un menú con más opciones.
- **Pronóstico y medición.** Estimación en vivo de tamaño y ahorro con valoración calidad↔ahorro.
  El botón *Medir con una muestra* codifica tres fragmentos y da la cifra real, calibrando de paso el
  resto de la lista.
- **No se atasca.** Si el disco se llena, **pausa** en vez de cancelarse o colgarse, y continúa sola en
  cuanto liberas espacio, conservando la cola de pendientes.
- **Idiomas y subtítulos.** Detecta las pistas, pone tu idioma preferido como predeterminado y descarta
  los que no quieras conservar.
- **Renombrado de la salida al estilo PowerRename**: buscar/reemplazar con expresiones regulares,
  contadores, variables de fecha y formato del texto, con vista previa en vivo.
- **Previsualización de 10 s** con los ajustes actuales, para comprobar antes de lanzar la tanda.
- **Presets y preferencias** por pestañas, y actualizaciones automáticas desde GitHub.

## Instalación

### Windows — app de escritorio

1. Descarga el instalador de la página de **[Releases](https://github.com/luishidalgoa/shrink-studio/releases/latest)** → `ShrinkStudio-Setup-X.Y.Z.exe`.
2. Ejecútalo. Se instala **solo para tu usuario** (no pide permisos de administrador) y crea acceso
   directo en el menú Inicio (y opcionalmente en el Escritorio).
3. Como el instalador no está firmado, Windows SmartScreen puede avisar: pulsa
   **Más información → Ejecutar de todas formas**.

> **FFmpeg** (única dependencia): el instalador lo **detecta automáticamente** y, si no lo tienes,
> ofrece descargarlo e instalarlo junto a la app. No necesitas configurar nada.

### Linux y macOS — terminal

La interfaz gráfica usa WPF, que solo existe en Windows. Para el resto de sistemas se publica
`shrinkstudio`, que comparte **exactamente el mismo motor**. Descarga el paquete de tu plataforma en
[Releases](https://github.com/luishidalgoa/shrink-studio/releases/latest) y descomprímelo:

```bash
tar xzf shrinkstudio-linux-x64.tar.gz     # o linux-arm64, macos-arm64, macos-x64
./shrinkstudio --help
```

Es un único binario autocontenido: no hace falta instalar .NET. Se entrega en `.tar.gz` porque así
conserva el permiso de ejecución, que un fichero suelto pierde al descargarse.

En **Windows**, la herramienta de terminal se descarga directamente como
`shrinkstudio-windows-x64.exe`, sin comprimir. Ojo: eso es el CLI, distinto del instalador
`ShrinkStudio-Setup-X.Y.Z.exe`, que es la app de escritorio.

Necesita `ffmpeg` y `ffprobe` en el `PATH` (`apt install ffmpeg`, `brew install ffmpeg`).

## Uso

### App de escritorio

1. **Origen**: elige una carpeta (o archivos sueltos). Con *Subcarpetas* marcado, entra en las temporadas.
2. **Analizar**: lista los vídeos con tamaño, duración, códec e idiomas de audio y subtítulos detectados.
3. Ajusta las opciones (todas tienen valor por defecto) o elige un **preset**.
4. Selecciona los vídeos y pulsa **Comprimir selección**. Verás el progreso en vivo, con **Pausar** y
   **Detener** disponibles en cualquier momento.

El **idioma principal** (español por defecto) se marca como pista de audio predeterminada; los idiomas
que no elijas se descartan para ahorrar espacio.

### Terminal

```bash
# Comprimir una temporada entera a MP4 720p, con el audio a 128 kbps
shrinkstudio comprimir serie/ -r --formato mp4 --alto 720 --audio 128 -o comprimidos/

# Ver qué pistas tiene cada vídeo
shrinkstudio analizar serie/ -r

# Medir cuánto va a ocupar de verdad, sin comprimirlo entero
shrinkstudio medir capitulo.mkv --alto 720

# Comprimir renombrando la salida con un contador
shrinkstudio comprimir *.mkv --regex --buscar "^" --reemplazar 'T01E${padding=2;start=1} - ' --enumerar
```

`shrinkstudio --help` lista todas las opciones.

## Actualizaciones automáticas

La app comprueba al arrancar si hay una versión nueva en GitHub. Si la hay, al pulsar **Actualizar ahora**
descarga el instalador, lo ejecuta y se cierra para completar la actualización, que reemplaza la versión
anterior in-place. También puedes comprobarlo a mano con **Buscar actualizaciones**.

## Desarrollo

Requisitos: **.NET 9 SDK** e **Inno Setup 6** (`winget install JRSoftware.InnoSetup`).

```powershell
# Ejecutar la app en desarrollo
dotnet run --project src/ShrinkVideo

# Ejecutar la herramienta de terminal
dotnet run --project src/ShrinkStudio.Cli -- --help

# Compilar el instalador completo (icono + .exe self-contained + instalador Inno)
pwsh -File build.ps1
# -> installer/Output/ShrinkStudio-Setup-<version>.exe
```

### Publicar una versión

Todo se compila en la nube, sin dependencias locales:

1. Añade la sección de la versión en [`CHANGELOG.md`](CHANGELOG.md) (`## [X.Y.Z] - AAAA-MM-DD`).
2. Sube `<Version>` **en los dos** `.csproj` (`src/ShrinkVideo` y `src/ShrinkStudio.Cli`).
3. `git tag vX.Y.Z && git push --follow-tags`.

[GitHub Actions](.github/workflows/build.yml) **verifica primero el contrato del CHANGELOG** —que la
sección exista, que las versiones cuadren y que las categorías sean válidas— y solo entonces compila el
instalador de Windows y los binarios de terminal para Linux, macOS y Windows, adjuntándolo todo al
Release. Si el contrato no se cumple, no se publica nada.

### Estructura

| Carpeta | Qué es |
|---|---|
| `src/ShrinkVideo/` | App C#/WPF. `Engine.cs` es el motor (FFmpeg); el resto es interfaz y auto-update. |
| `src/ShrinkStudio.Cli/` | Herramienta de terminal multiplataforma. Enlaza los fuentes del motor, no los copia. |
| `installer/` | Script de Inno Setup. |
| `make-icon.ps1` | Genera el icono con GDI+. |
| `build.ps1` | Compila todo de punta a punta. |
| `legacy/` | La versión original en PowerShell, con la que nació el proyecto. |

> **La interfaz gráfica es solo para Windows** porque usa **WPF**, que no tiene runtime en Linux ni macOS.
> El motor (`Engine`, `Estimator`, `RenameRule`) sí es portable, y es justo lo que reutiliza la
> herramienta de terminal. Llevar la interfaz completa a Linux/macOS exigiría migrarla a **Avalonia**.

## Cómo funciona

- Detecta las pistas con `ffprobe` y reordena el audio para poner tu idioma preferido primero y como
  predeterminado.
- Elige el codificador por hardware disponible (Intel QSV, NVIDIA NVENC, AMD AMF) o cae a CPU (`libx265`).
- Salta lo ya comprimido (HEVC/AV1 con bitrate bajo) y los archivos que aún se están descargando.
- Escribe siempre a un temporal y solo lo mueve al destino cuando termina bien, así una interrupción
  nunca deja un vídeo a medias haciéndose pasar por bueno.
