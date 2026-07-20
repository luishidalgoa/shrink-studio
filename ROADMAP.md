# Roadmap — ShrinkStudio (relevo de HandBrake)

Objetivo: heredar las funcionalidades clave de HandBrake manteniendo lo que nos diferencia
(procesamiento por lotes cómodo, análisis de pistas, idiomas de audio con el principal por defecto).

Leyenda: ✅ hecho · 🔜 siguiente · ⬜ pendiente

## Base (v0.1.x)
- ✅ Compresión H.265 por hardware (QSV/NVENC/AMF) con fallback a CPU.
- ✅ Calidad ajustable, downscale de resolución, idiomas de audio, subtítulos por idioma.
- ✅ Lote: análisis de pistas, lista con miniaturas, marcar/comprimir, papelera.
- ✅ Instalador per-user + auto-update.

## Bloque 1 — Presets + más formatos
- ✅ **Formato de salida**: MKV y **MP4** (con audio/subtítulos recodificados a lo que MP4 admite).
- ⬜ **WebM** (requiere forzar AV1/VP9 + Opus; sin subtítulos de texto).
- 🔜 **Presets**: guardar/cargar configuraciones con nombre (JSON en `%AppData%`), combo + botón guardar/borrar.
- ⬜ Presets de fábrica ("Máxima compatibilidad", "Archivar", "Móvil", …).

## Bloque 2 — Vídeo avanzado
- ✅ **Códec elegible**: H.265 / H.264 / **AV1** (hardware si hay, si no software).
- ✅ Modo calidad (CRF/CQ) por presets de calidad.
- 🔜 Modo **bitrate objetivo** (VBR de N kbps / tamaño objetivo) además de calidad.
- 🔜 Preset de velocidad del codificador (ultrafast…slow) expuesto en la UI.
- ⬜ **Recorte (crop)** y **dimensiones exactas** (con anamórfico/relación de aspecto).
- ⬜ **Filtros**: desentrelazado, denoise, deblock, nitidez, rotación.

## Bloque 3 — Audio y subtítulos avanzados
- ✅ Copiar original o recodificar a AAC (por bitrate).
- 🔜 **Codecs de audio** elegibles: AAC / AC3 / E-AC3 / Opus / FLAC / passthrough.
- 🔜 **Mezcla** (downmix a estéreo, mantener 5.1) y bitrate/samplerate por pista.
- ⬜ Ganancia y compresión de rango dinámico (DRC).
- ⬜ Subtítulos: **quemar (burn-in)**, marcar *forced*, importar `.srt` externos.

## Bloque 4 — Flujo de trabajo
- 🔜 **Cola de trabajos** con ajustes distintos por trabajo (hoy todos comparten opciones).
- ⬜ **Recortar por tiempo** (procesar solo un tramo inicio–fin).
- ⬜ **Previsualización** (muestra corta del resultado antes de codificar todo).
- ⬜ "Al terminar": no hacer nada / apagar / suspender.

## Transversal
- ✅ **FFmpeg**: el instalador lo detecta y, si falta, lo descarga e instala junto a la app (el usuario no configura nada).
- ⬜ Firmar el instalador (evitar aviso de SmartScreen).
- ⬜ Rediseño visual (brief en [`docs/design-brief.md`](docs/design-brief.md)).
