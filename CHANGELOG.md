# Registro de cambios

Todos los cambios relevantes de ShrinkStudio se anotan aquí.

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y el
versionado sigue [SemVer](https://semver.org/lang/es/). Antes de la 1.0 la versión
es `0.MINOR.PATCH`: `MINOR` sube con funcionalidad nueva y `PATCH` con arreglos.

## Contrato

Reglas que cumple **toda** versión publicada. El flujo de trabajo `verificar-version`
las comprueba en cada tag y **falla la publicación** si no se cumplen, así que esto no
es un acuerdo de buena voluntad: está verificado.

1. **Una sección por versión**, con el encabezado exacto `## [X.Y.Z] - AAAA-MM-DD`.
   Lo que aún no se ha publicado vive en `## [Unreleased]`.
2. **El tag manda**: al empujar `vX.Y.Z` debe existir la sección `## [X.Y.Z]` y la
   propiedad `<Version>` de los `.csproj` debe valer exactamente `X.Y.Z`.
3. **Escrito para quien usa la app**, en español y en pasado: qué cambia para ti, no
   qué fichero se tocó. Nada de nombres de clases ni de ramas.
4. **Una entrada por funcionalidad**, no por commit. Lo trivial (formato, refactores
   internos, cambios de comentarios) no aparece.
5. **Categorías permitidas**, en este orden: `Añadido`, `Cambiado`, `Obsoleto`,
   `Eliminado`, `Corregido`, `Seguridad`. Solo se escriben las que tengan contenido.
6. **Los cambios que rompen algo** se marcan con **RUPTURA** al principio de la línea
   y explican qué hacer.
7. **Sin secciones vacías** ni versiones repetidas, y las versiones van de más nueva a
   más antigua.

## [Unreleased]

### Corregido

- **Los botones del aviso de actualización no respondían.** «Actualizar ahora» y «Después»
  caían dentro de la franja que Windows reserva para arrastrar la ventana, que se tragaba
  los clics.
- **«Buscar actualizaciones» se quedaba colgado en «Buscando…»** cuando sí había versión
  nueva: el mensaje no se actualizaba nunca.
- **Se decía «ya tienes la última versión» aunque no hubiera habido conexión.** Ahora se
  distingue entre estar al día y no haber podido comprobarlo, y se explica el motivo.
- **El actualizador podía descargar el archivo equivocado.** Cogía el primer `.exe` del
  release y, desde que también se publica la herramienta de terminal para Windows, ese
  podía no ser el instalador.

### Cambiado

- Más información durante la actualización: se ve qué archivo se descarga, con barra de
  progreso, y se avisa de que la app se cerrará para instalar.

### Cambiado

- La herramienta de terminal ocupa ahora **13 MB en vez de 68**: se recorta el runtime que
  se empaqueta con ella.
- En Windows, la herramienta de terminal se descarga como un `.exe` suelto, sin comprimir.
  En Linux y macOS sigue en `.tar.gz`, que es lo que conserva el permiso de ejecución.

### Corregido

- Al recortar el binario se perdían los tipos con los que se lee la salida de ffmpeg y el
  análisis devolvía datos vacíos (duración 0, resolución 0x0). Ahora esos tipos se generan
  en compilación.

## [0.4.0] - 2026-07-21

### Añadido

- **Pausa automática cuando el disco se llena.** Si te quedas sin espacio, la compresión
  se pausa en lugar de cancelarse o colgarse, y continúa sola en cuanto liberas sitio,
  conservando la cola de archivos pendientes. Puedes seguir usando «Detener» mientras
  está en pausa.
- **Selección estilo explorador en la tabla.** Se procesa lo que esté seleccionado:
  arrastra para seleccionar en banda, o usa Ctrl+clic, Mayús+clic y Ctrl+A.
- **Quitar vídeos de la lista** con la tecla Supr o desde el nuevo menú contextual del
  botón derecho, que además permite enviar el archivo a la papelera, abrir su carpeta y
  copiar la ruta. Quitar de la lista nunca borra el archivo.
- **Barra de menú** con Archivo, Selección, Herramientas y Ayuda.
- **Preferencias por pestañas**: preset e idioma por defecto, qué hacer con los originales
  al terminar, margen mínimo de disco y uso de la aceleración por hardware.
- **Aviso antes de comprimir** para elegir si los originales se envían a la papelera
  según van terminando, con opción de no volver a preguntar.
- **Renombrado de los archivos de salida al estilo PowerRename**: buscar y reemplazar con
  expresiones regulares, contadores, variables de fecha y formato del texto, con vista
  previa en vivo y autocompletado en los campos.
- **Medición real del tamaño final.** El botón «Medir con una muestra» codifica tres
  fragmentos cortos con tus ajustes y calcula el peso de verdad, además de calibrar la
  estimación del resto de la lista.
- **Descripciones emergentes** en los controles, explicando el efecto de cada opción.
- **Versión de línea de órdenes para Linux, macOS y Windows** (`shrinkstudio`), con el
  mismo motor que la app: comprimir, analizar y medir desde la terminal.

### Cambiado

- **Una sola instancia**: si la app ya está abierta y vuelves a lanzarla, se trae al
  frente la ventana existente en vez de abrir otra.
- La estimación de tamaño era demasiado optimista con dibujos animados y material plano;
  ahora parte de una referencia más ajustada y se puede calibrar midiendo.

### Corregido

- La preferencia «analizar subcarpetas» se reactivaba sola al arrancar o al cambiar de
  preset, porque los presets la sobrescribían.
- Los menús se veían con el estilo claro de Windows y con contrastes insuficientes: el
  resalte del elemento activo era casi invisible y el texto de los atajos no llegaba al
  mínimo de accesibilidad AA.
- El texto de la barra de menú aparecía descolocado dentro de su recuadro.

### Notas

- La interfaz gráfica es **solo para Windows** porque usa WPF, que no existe en Linux ni
  macOS. Para esos sistemas se publica la versión de línea de órdenes, que comparte
  exactamente el mismo motor.

## [0.2.1] - 2026-07-20

### Añadido

- Primera versión distribuida con instalador propio y actualización automática desde
  GitHub: comprime a HEVC, H.264 o AV1 en MKV o MP4, conserva los idiomas de audio que
  elijas y nunca toca los archivos originales.
