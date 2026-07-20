<p align="center">
  <img src="docs/icon.png" alt="Comprimir vídeos" width="128">
</p>

<h1 align="center">ShrinkStudio</h1>

<p align="center">
  Transcodificador de vídeo para Windows, pensado como <b>relevo ligero de HandBrake</b>:
  comprime series, películas y capítulos a <b>H.265 / H.264 / AV1</b> en <b>MP4 o MKV</b>,
  con aceleración por hardware y conservando solo los idiomas de audio que quieras.
</p>

---

Su foco es el **ahorro de almacenamiento**: reduce típicamente **un 80–90 %** el tamaño manteniendo una
calidad visual muy buena, y antes de comprimir te muestra un **pronóstico** del tamaño final y del ahorro,
con una valoración **calidad↔ahorro** que reacciona a cada ajuste. A diferencia de HandBrake destaca en
**procesamiento por lotes**: analiza una carpeta entera, muestra las pistas de cada vídeo y comprime en
tanda. **Nunca toca los originales**: el resultado va siempre a otra carpeta.

> Roadmap de funcionalidades heredadas de HandBrake: [`ROADMAP.md`](ROADMAP.md).

## Instalación

1. Descarga el instalador más reciente de la página de **[Releases](https://github.com/luishidalgoa/shrink-studio/releases/latest)** → `ShrinkVideo-Setup-X.Y.Z.exe`.
2. Ejecútalo. Se instala **solo para tu usuario** (no pide permisos de administrador) y crea acceso directo en el menú Inicio (y opcionalmente en el Escritorio).
3. Como el instalador no está firmado, Windows SmartScreen puede avisar: pulsa **Más información → Ejecutar de todas formas**.

> **FFmpeg** (única dependencia): el instalador lo **detecta automáticamente** y, si no lo tienes,
> ofrece descargarlo e instalarlo junto a la app. No necesitas configurar nada.

## Uso

1. **Origen**: elige una carpeta (o un archivo suelto). Con *Subcarpetas* marcado, entra en las temporadas.
2. **Analizar**: lista los vídeos con su tamaño, duración, códec y los idiomas de audio/subtítulos detectados.
3. Ajusta las opciones (todas con valor por defecto): idioma principal, calidad, resolución máxima, audio, y qué idiomas conservar.
4. Marca los que quieras y pulsa **Comprimir marcados**. Ves el progreso en vivo; puedes **Cancelar** en cualquier momento.

El **idioma principal** (por defecto español) se marca como pista de audio predeterminada. Los idiomas no elegidos se descartan para ahorrar espacio.

## Actualizaciones automáticas

La app comprueba al arrancar si hay una versión nueva en GitHub. Si la hay, muestra un aviso: al pulsar **Actualizar ahora**, descarga el instalador, lo ejecuta y se cierra para completar la actualización (que reemplaza la versión anterior in-place). También puedes comprobarlo manualmente con **Buscar actualizaciones**.

## Desarrollo

Requisitos: **.NET 9 SDK** e **Inno Setup 6** (`winget install JRSoftware.InnoSetup`).

```powershell
# Ejecutar en desarrollo
dotnet run --project src/ShrinkVideo

# Compilar el instalador completo (icono + .exe self-contained + instalador Inno)
pwsh -File build.ps1
# -> installer/Output/ShrinkVideo-Setup-<version>.exe
```

Publicar una versión nueva (dispara el auto-update en los usuarios):

1. Sube el número en `<Version>` de [`src/ShrinkVideo/ShrinkVideo.csproj`](src/ShrinkVideo/ShrinkVideo.csproj).
2. `pwsh -File build.ps1`
3. `gh release create vX.Y.Z installer/Output/ShrinkVideo-Setup-X.Y.Z.exe --title "vX.Y.Z" --notes "..."`

### Estructura

| Carpeta | Qué es |
|---|---|
| `src/ShrinkVideo/` | App C#/WPF. `Engine.cs` = motor (FFmpeg); el resto es interfaz y auto-update. |
| `installer/` | Script de Inno Setup. |
| `make-icon.ps1` | Genera el icono con GDI+. |
| `build.ps1` | Compila todo de punta a punta. |
| `legacy/` | La versión original en PowerShell (el motor con el que nació esto). |

## Cómo funciona

- Detecta las pistas con `ffprobe`, reordena el audio para poner tu idioma preferido primero y como predeterminado.
- Elige el codificador por hardware disponible (Intel QSV, NVIDIA NVENC, AMD AMF) o cae a CPU (`libx265`).
- Salta lo ya comprimido (HEVC/AV1 con bitrate bajo) y lo que aún se está descargando.
