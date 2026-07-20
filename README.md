# Comprimir vídeos (Shrink-Video)

Herramienta para **reducir el peso de vídeos** (series, películas, capítulos sueltos) recodificándolos
a **HEVC/H.265** con aceleración por hardware, convirtiendo el audio y **conservando solo los idiomas
que te interesan** (con tu idioma preferido marcado como predeterminado).

Pensada para tandas de archivos grandes: reduce típicamente **un 80–90 %** el tamaño manteniendo una
calidad visual muy buena. **Nunca toca los originales** — el resultado va siempre a otra carpeta.

## Requisitos

- **Windows** con **PowerShell 7** (`pwsh`).
- **FFmpeg** en el `PATH`:
  ```powershell
  winget install Gyan.FFmpeg
  ```
- Codificación por hardware automática si tienes GPU Intel (QSV), NVIDIA (NVENC) o AMD (AMF).
  Si no hay ninguna, usa la CPU (`libx265`, más lento) y te avisa.

## Uso

### Interfaz gráfica
Doble clic en **`Comprimir vídeos.cmd`** (o en el acceso directo si lo creaste).
Elige el origen (carpeta o archivo), opcionalmente el destino, y pulsa **Comprimir**.
Todo lo demás trae valores por defecto.

### Línea de comandos
```powershell
# Todos los vídeos de una carpeta
.\Shrink-Video.ps1 -Path "D:\Series\Bob Esponja"

# Una serie entera, con subcarpetas de temporadas
.\Shrink-Video.ps1 -Path "D:\Series\Bob Esponja" -Recurse

# Una película en 4K, más calidad, bajándola a 1080p
.\Shrink-Video.ps1 -Path "D:\Pelis\Duna.mkv" -Quality 24 -MaxHeight 1080

# Solo audio inglés, tirando el resto
.\Shrink-Video.ps1 -Lang eng -KeepLangs eng

# Ver qué haría sin codificar nada
.\Shrink-Video.ps1 -Path "D:\Pelis" -DryRun
```

## Parámetros

| Parámetro | Por defecto | Qué hace |
|---|---|---|
| `-Path` | carpeta actual | Archivo o carpeta a procesar. |
| `-Output` | `<origen>\comprimido` | Carpeta de destino. |
| `-Lang` | `spa` | Idioma preferido (ISO 3 letras); se marca como audio predeterminado. |
| `-KeepLangs` | `spa,eng` | Idiomas de audio a conservar (coma). `all` = todos. |
| `-Quality` | `0` (auto) | Menor número = más calidad y tamaño. Rango útil 22–30. |
| `-MaxHeight` | `0` (sin cambio) | Reescala si el vídeo es más alto (p. ej. `1080`). |
| `-AudioBitrate` | `0` (copiar original) | kbps del audio AAC; `0` copia el audio sin recomprimir. |
| `-SubLangs` | todos | Idiomas de subtítulos a conservar. |
| `-Recurse` | — | Incluye subcarpetas. |
| `-NoSubs` | — | No conservar subtítulos. |
| `-Force` | — | Reprocesa también los que ya parecen comprimidos. |
| `-DryRun` | — | Muestra qué haría sin codificar. |

Ayuda completa:
```powershell
Get-Help .\Shrink-Video.ps1 -Full
```

## Cómo funciona

- Detecta las pistas con `ffprobe`: vídeo, audios por idioma y subtítulos.
- Reordena el audio para poner tu idioma preferido primero y como predeterminado.
- Salta lo que ya está comprimido (HEVC/AV1 con bitrate bajo) y lo que aún se está descargando.
- Es **re-ejecutable sin miedo**: puedes lanzarlo otra vez y solo procesa lo que falta.

## Estructura

| Archivo | Qué es |
|---|---|
| `Shrink-Video.ps1` | El motor (toda la lógica; usable por línea de comandos). |
| `Shrink-Video-GUI.ps1` | Interfaz gráfica (WPF); solo llama al motor. |
| `Comprimir vídeos.cmd` | Lanzador de la interfaz (doble clic). |

> La lógica vive **solo** en `Shrink-Video.ps1`. La interfaz es un frontend: mejorar el motor
> beneficia a ambos.
