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
   Lo que aún no se ha publicado vive en la sección de cambios pendientes.
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

### Añadido

- **El formato del catálogo está documentado y se comprueba al importar.** Un botón
  «¿Qué formato?» abre la especificación con todos los campos, las reglas y un ejemplo
  completo, y «Crear ejemplo…» te guarda un catálogo válido para que lo edites en vez de
  escribirlo a ciegas. Si el archivo tiene fallos, se te dicen **todos juntos** y con el
  episodio concreto: números repetidos (que antes hacían perder un episodio en silencio),
  fechas imposibles o números negativos.

### Cambiado

- **La ventana se puede encoger mucho más.** El mínimo baja de 1000×660 a 820×560, y el
  contenido se adapta en vez de recortarse: los campos se estiran, los botones bajan de
  línea si no caben, el panel de detalle se pliega cuando le quita sitio a la tabla, y en
  la barra de título el conmutador se queda en iconos para que no se coma el menú.

### Corregido

- **Con el aviso de actualización en pantalla no se podía mover la ventana.** El aviso se
  colocaba encima de la barra de título y se quedaba con la franja que Windows reserva para
  arrastrar, así que no valía ni arrastrar por el aviso ni por la barra. Ahora el aviso va
  debajo y la ventana se mueve como siempre.

## [0.6.0] - 2026-07-21

### Añadido

- Nueva página **Organizar**, que identifica qué episodio es cada fichero comparándolo con
  un catálogo de la serie y propone su nombre definitivo. Se cambia entre «Comprimir» y
  «Organizar» desde la barra de título, y la compresión sigue su curso mientras tanto
  (una píldora avisa del avance). Nada se renombra sin aprobación: primero se simula, se
  revisa el resultado en una tabla con semáforo —limpio, corregido, especial, conflicto,
  error— y solo entonces se aplica. Cada lote aplicado se puede deshacer entero, y las
  decisiones que tomas se recuerdan para no volver a preguntarte lo mismo.

### Cambiado

- **La columna «Estado» de la tabla ahora sirve para algo.** Antes ponía «listo» tras analizar
  y no volvía a cambiar nunca. Ahora cuenta lo que pasa con cada vídeo: si ya está bien
  comprimido y por qué se salta, cuándo está en cola, el avance mientras se comprime, y al
  terminar cuánto se ha ahorrado y cuánto ocupa.
- **Al analizar se marcan solos los vídeos que conviene comprimir.** Los que ya están en un
  códec eficiente se quedan sin marcar, con su motivo a la vista, en vez de descubrirlo al
  lanzar la tanda.
- La lista vacía ahora explica qué hacer en vez de ser un hueco en negro.
- **La interfaz se ilumina.** El fondo tiene ahora una luz ambiental tenue que respira muy
  despacio, y brillan los puntos que importan: el botón de la acción principal al apuntarlo,
  el campo donde vas a escribir, la página en la que estás y el progreso mientras trabaja.
  Está medido para que ayude a mirar donde toca, no para llenar la pantalla de luces.
- **Las ventanas tienen las esquinas redondeadas de Windows 11.** Como la app dibuja su
  propia barra de título, Windows dejaba de redondearlas y quedaban como un rectángulo
  recto que desentonaba con el resto del escritorio. El redondeo lo pone ahora el propio
  sistema, así que la sombra y el radio son los suyos y desaparecen al maximizar, igual
  que en cualquier otra ventana. En Windows 10 se mantienen rectas, que es su aspecto.

### Corregido

- **Los MP4 salían sin subtítulos, y además no eran MP4 de verdad.** El archivo temporal
  se creaba siempre con extensión `.mkv`, y como ffmpeg decide el formato por la extensión,
  el resultado era un Matroska con el nombre cambiado. Ahora el MP4 es un MP4 y conserva
  los subtítulos de texto. Los de imagen (los de los DVD y Blu-ray) no caben en MP4: se
  descartan avisándote, en vez de tumbar la compresión entera.

## [0.5.0] - 2026-07-21

### Añadido

- **Atajos desde el Explorador de Windows** (se activan en Preferencias → General):
  - **«Abrir con → ShrinkStudio»** en el menú contextual de primer nivel, junto a Fotos o
    Clipchamp, para uno o pocos vídeos.
  - **«Enviar a → ShrinkStudio»** y **«Comprimir con ShrinkStudio»** (en «Mostrar más
    opciones»), y también puedes **arrastrar vídeos o carpetas enteras** a la ventana.
    Estas vías admiten selecciones grandes, que Windows recorta en el menú clásico.
  - Los vídeos llegan a la lista tanto si la app estaba cerrada como abierta, sin duplicar
    ventanas ni filas.

### Cambiado

- **Icono nuevo, en el morado de la app.** El anterior era turquesa y no se parecía al
  logotipo de la propia ventana. Ahora el icono del programa, del instalador, de los
  accesos directos y del repositorio es el mismo glifo morado de la barra de título.
- La herramienta de terminal ocupa ahora **13 MB en vez de 68**, y en Windows se descarga
  como un `.exe` suelto, sin comprimir. En Linux y macOS sigue en `.tar.gz`, que es lo que
  conserva el permiso de ejecución.
- Más información durante la actualización: se ve qué archivo se descarga, con barra de
  progreso, y se avisa de que la app se cerrará para instalar.

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
- Al recortar la herramienta de terminal se perdían los tipos con los que se lee la salida
  de ffmpeg y el análisis devolvía datos vacíos. Ahora esos tipos se generan en compilación.

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
