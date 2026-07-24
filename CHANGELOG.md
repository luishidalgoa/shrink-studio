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

### Corregido

- **Un fichero bien nombrado ya no se confunde con un «remake» del mismo título al estar en una
  subcarpeta.** La temporada se leía solo del nombre de la carpeta; un fichero como
  «…S2020E574 - El aro de la gratitud.mkv» metido en una subcarpeta de trabajo (p. ej.
  «Renombrar», sin año) perdía su temporada, y como en Doraemon hay historias que se repiten
  años después con el mismo título, el motor lo tomaba por el episodio equivocado (el 88 de 2007
  en vez del 574 de 2020) y lo dejaba en conflicto una y otra vez. Ahora la temporada también se
  lee del propio nombre («S2020E…»), así que identifica el episodio correcto aunque el fichero no
  esté en su carpeta de temporada.

- **Un fichero ya correctamente nombrado deja de salir en «Conflicto» una y otra vez.** Cuando
  otro fichero reclamaba el mismo número de episodio, la app podía marcar como conflicto al que
  YA estaba bien nombrado (perdía un desempate alfabético) en vez de al aspirante. Lo «corregías»
  y volvía a aparecer en cada análisis. Ahora el **titular** —el fichero que ya lleva el nombre
  correcto— manda sobre su número y se queda en verde; el conflicto recae en el otro fichero,
  que es el que de verdad hay que decidir. Y si son dos copias del MISMO fichero (el típico caso
  de tener el vídeo en su carpeta de temporada y una copia en una subcarpeta de trabajo tipo
  «Renombrar»), la que queda verde es siempre la de la **biblioteca** —la más superficial—, no la
  de staging, sin depender del orden de escaneo. (La copia sobrante seguirá marcada como
  duplicada: para que desaparezca del todo hay que borrar ese segundo fichero.)

### Añadido

- **Botón «Vaciar» en Recortes para soltar el vídeo y liberar la memoria.** Deja la página como
  recién abierta —sin vídeo, sin cortes, sin historial— y devuelve al sistema la memoria que
  ocupaban el vídeo y las miniaturas, sin cerrar la app. Aparece en la cabecera cuando hay algo
  cargado; si tienes cortes preparados, pregunta antes de descartarlos.

- **Al elegir historia en un episodio multi-historia, puedes marcar VARIAS (no solo una).** El
  diálogo «¿Cuáles trae este fichero?» ahora usa casillas: si un fichero trae dos de las tres
  historias de un episodio (la «a» y la «c», pongamos), márcalas las dos y el nombre queda como
  «E413ac» con los dos títulos juntos. Marcar todas equivale a «el episodio completo». El nombre
  se relee igual, así que renombrar y volver a analizar sigue dando lo mismo.

- **«Elegir otro episodio…» también cuando detecta dos episodios en un fichero.** Antes, si la
  app veía que un fichero traía dos capítulos, solo dejaba «Partirlo en dos» o «Dejarlo como
  está» y escondía el selector de episodio. Ahora la opción de asignar un episodio a mano está
  siempre disponible: si la detección de «dos episodios» fue un falso positivo, puedes corregirlo
  eligiendo el episodio correcto — y la fila deja de recomendar partir y pasa a un renombrado
  normal. «Partirlo en dos» sigue siendo la acción destacada cuando de verdad son dos.

### Corregido

- **Recortes ya no se vuelve más lento cuanto más exportas.** Había una fuga de recursos: cada
  vez que exportabas un tramo y cargabas otro vídeo, el proceso se quedaba con un puñado de
  «handles» del sistema que nunca soltaba, y al repetir el ciclo muchas veces la app se iba
  arrastrando. La causa era doble: el reproductor se cerraba con una llamada que en WPF filtra
  handles a cada uso, y la exportación abría una tubería hacia ffmpeg que no hacía falta. Se
  arreglaron las dos. Medido en la máquina, la fuga por ciclo baja de ~23 handles a ~5 (el
  grueso, eliminado), y la exportación sigue produciendo exactamente los mismos ficheros.

## [0.14.7] - 2026-07-23

### Cambiado

- **Se retira el fondo de plasma animado de Recortes.** Era bonito, pero se calculaba píxel a
  píxel en la CPU y —en una app cuyo trabajo es justo saturar la CPU comprimiendo— se comía un
  núcleo entero **incluso en reposo**, y era la causa real de que la interfaz fuera lenta al
  importar y después de exportar un vídeo pesado. En su lugar queda un fondo degradado sobrio
  que no cuesta nada. Medido: el consumo en reposo cae de **~110 % de un núcleo a ~7 %**.

### Corregido

- **«Partir en dos» de otro vídeo justo después de exportar ya no carga el vídeo equivocado.**
  Al terminar una exportación, la previsualización del vídeo recién exportado se reabría con un
  pequeño retardo. Si en ese hueco cargabas otro fichero (por ejemplo, «Partir en dos» de otro
  episodio), esa reapertura tardía pisaba el nuevo vídeo con el anterior y la partición salía
  sobre el material equivocado. Ahora la reapertura comprueba antes que el vídeo en pantalla
  sigue siendo el que se exportó; si cargaste otro, no lo toca.
- **La interfaz ya no se arrastra mientras exportas con un códec por software.** Al comprimir,
  ffmpeg usaba los ocho núcleos y ahogaba a la propia app; bajarle la prioridad no bastaba.
  Ahora se le **reservan** un par de núcleos a la interfaz (y también al sondeo y las miniaturas
  del import), así responde al momento aunque la codificación esté a tope. La codificación tarda
  un pelín más, imperceptible en una tarea de fondo.

## [0.14.6] - 2026-07-23

### Cambiado

- **El gradiente de plasma también sale de fondo cuando Recortes está vacío.** Antes solo
  aparecía al exportar; ahora, cuando no hay ningún vídeo cargado, el «Elige un vídeo para
  empezar a cortarlo» se muestra sobre el mismo plasma en movimiento (atenuado para que el
  texto se lea). Mismo motor barato de siempre, y se **congela** si minimizas la ventana o
  cambias de pestaña, para no gastar batería moviendo algo que nadie está mirando.
- **El plasma se mueve más suave y ya no se congela al arrastrar la ventana.** Iba a 14
  fotogramas por segundo, que se veía a saltos y parecía lento; ahora va a 30 y fluye. Y
  arrastrar la ventana ya no lo congela: se comprobó que dejarlo correr no mete tirones (sigue
  yendo como en reposo), así que congelarlo solo se veía peor.

## [0.14.5] - 2026-07-23

### Cambiado

- **Los catálogos ya no se copian: se leen de donde están.** Al importar, la app referencia
  tu JSON en su sitio — si lo editas, cuenta al momento; ya no existe la copia interna que
  se quedaba vieja en silencio. La tarjeta enseña **la ruta del fichero** (pulsable: abre la
  carpeta con él seleccionado) y el clic derecho ofrece abrir la ubicación o copiar la ruta.
  Si mueves o borras el JSON, el catálogo **desaparece del programa** (no queda una tarjeta
  rota apuntando a un fichero que ya no está). «Quitar» de una copia interna de una versión
  anterior sí borra esa copia, para que no reaparezca al refrescar. Tu fichero original nunca
  se toca.
- **«Simular» pasa a llamarse «Analizar».** Analizar la carpeta es lo que hace; «simular»
  sugería un ensayo de mentira.

### Corregido

- **La ventana ya no va a tirones mientras se exporta o comprime.** El motor es asíncrono,
  pero sus tramos síncronos (candados, sondeos, espacio en disco) corrían en el hilo de la
  interfaz — y sobre OneDrive cada uno es un viaje de red. Ahora todo el trabajo del motor
  corre en un hilo aparte, también el escaneo de la carpeta al pulsar «Analizar».
- **El conteo de tramos exportados ya no miente.** Decía «1 de 2 sin salir» con los dos
  ficheros en el disco: si la carpeta ya tenía un fichero del mismo nombre (de un intento
  anterior), el motor saca la salida con sufijo y la comprobación buscaba el nombre sin él.
  Ahora se cuenta lo que el motor dice que escribió, comprobado en disco.
- **El tirón del final de la exportación, suavizado.** Al terminar, reabrir el vídeo en el
  reproductor costaba 100-200 ms del hilo de interfaz justo encima del desmontaje de la capa
  de aviso — era el único bloqueo medible de toda la exportación (durante la codificación el
  hilo va limpio). Ahora se reabre un instante después, cuando la interfaz está ociosa.
- **Nuevo aviso de exportación: un gradiente de plasma en movimiento.** En vez de las dos
  franjas de luz, la capa que aparece al exportar es ahora un gradiente animado de colores que
  fluyen (el efecto de plasma con viñeta y glow). Y lo importante: se hizo sin traer de vuelta
  el problema de fluidez. Se calcula diminuto (160×90) en un hilo de fondo por CPU —que
  durante un encode por hardware está ocioso— y se escala a la capa; así no toca ni el hilo de
  la interfaz (el del arrastre) ni la GPU (la del codificador), y además se congela mientras
  mueves la ventana. Medido con un export pesado de 1080p entero: arrastrar exportando va igual
  que en reposo. La intensidad del brillo respira sola, cambiando al azar cada pocos segundos.
- **Medidor de fluidez durante la exportación.** Por si algún tirón se escapa: la app mide en
  tu máquina los dos hilos que pueden causarlo —la entrada y el render— y, si de verdad hubo
  tela, lo anota en el Registro con el número. Si no sale nada y aun así lo notaste, el freno
  viene de fuera del proceso (el grabador de pantalla, que también codifica por GPU; la
  memoria llena; el compositor de Windows).
- **Los tabs que no miras dejan de trabajar.** Al cambiar de pestaña, Recortes deja de mover
  su reloj, de pedir fotogramas de previsualización y —si tenías el vídeo reproduciéndose— de
  decodificarlo; lo retoma al volver. Un tab oculto ya no se DIBUJA (de eso se encarga
  Windows), pero seguía trabajando en segundo plano sin que se viera.
- **Las tarjetas de catálogo se refrescan al volver a la app:** si borras o mueves el JSON
  desde el Explorador, la tarjeta desaparece al volver, sin reiniciar.
- **La barra de «Descargando de la nube» ahora avanza de verdad.** OneDrive suele traer el
  fichero entero de una vez, así que contar lo leído dejaba la barra a cero hasta el final;
  ahora se mide por los bytes que ya hay en disco, que crecen según baja.

## [0.14.4] - 2026-07-23

### Corregido

- **Un fichero con dos episodios distintos ahora recomienda partirlo, no elegir uno.** Cuando
  las dos historias de un vídeo casan cada una con un episodio distinto del catálogo (el
  trozo A con el 588, el B con el 589), la app lo trataba como un empate de «elige 588 o 589»
  — cuando ponerle el número de uno pierde el otro para siempre. Ahora lo detecta también en
  ese caso (antes solo si venía con número seguro), lo dice claro («trae dos episodios: el
  588 y el 589») y ofrece **partirlo en dos**, ocultando el selector de episodio que llevaba
  al error.

### Cambiado

- **Nombres de estado más claros en Organizar.** «Limpios» pasa a **«Correctos»** (el nombre
  ya está bien) y «Corregidos» a **«Con cambios»** (había un cambio propuesto que aún no se
  ha aplicado — «corregido» daba a entender que ya estaba hecho). Los ficheros que hay que
  partir se marcan aparte, con «✂ Partir en 2».

## [0.14.3] - 2026-07-23

### Corregido

- **Recortes ya no se queda «SALTADO: Descargando» hasta reiniciar la app.** La comprobación
  de «¿este fichero aún se está descargando?» abría el vídeo en exclusiva, así que saltaba
  con cualquier LECTOR: OneDrive hidratando, el indexador o el propio reproductor de la
  página, que suelta el fichero con retraso. Y tras cada intento fallido la app reabría el
  vídeo, con lo que el siguiente intento volvía a encontrarlo cogido — de ese bucle solo se
  salía reiniciando. Ahora solo salta si alguien lo tiene abierto para ESCRIBIR, que es lo
  que de verdad delata una descarga a medias.
- **Los vídeos que están solo en la nube se descargan enteros al abrirlos en Recortes,**
  con barra de progreso y Esc para cancelar. Trabajar sobre el marcador a medias era la
  otra mitad de la lentitud: las miniaturas y la codificación iban a velocidad de red y la
  app parecía ahogada sin decir por qué. Ahora la descarga se paga una vez, al principio y
  a la vista; si el sistema vuelve a soltar el fichero («liberar espacio»), exportar lo
  baja de nuevo con el mismo progreso.
- **Durante la exportación se paran las miniaturas de la pista:** goteaban lecturas sobre
  el mismo fichero que se estaba codificando.

## [0.14.2] - 2026-07-23

### Cambiado

- **La cola es ahora una cola de verdad, no un filtro.** Estaba mal planteada: añadir pedía
  escribir un motivo y verla era encender un filtro que escondía filas. Ahora funciona como
  la cola de un reproductor de música: el botón derecho la añade **de un clic, sin preguntar
  nada**, y el botón «Cola» de abajo a la derecha abre la lista con todo lo guardado. Desde
  ahí pulsas uno y te lleva a él —quitando el filtro que lo estuviera tapando—, o lo sacas
  con la ✕. El botón está siempre a la vista, también antes de simular, que es cuando abres
  la app y quieres saber qué tenías pendiente.

### Corregido

- **La app ya no se arrastra mientras exportas.** Dos causas, las dos medidas. La capa de
  aviso que se pone sobre el reproductor gastaba un **16-18 % de CPU ella sola**, en bucle,
  todo el rato que durase la exportación: la caché de dibujo estaba puesta en el grupo y no
  en cada luz, así que cada latido obligaba a repintar la capa entera. Ahora cuesta un
  **3-5 %** — y va a 20 fotogramas por segundo en vez de a 5, así que además se ve suave.
  Y ffmpeg, que codifica con todos los núcleos, ahogaba a la propia app: pasa a prioridad
  por debajo de lo normal, con lo que la ventana recupera CPU (medido: del 3,1 % al 4,9 %
  sobre un 5,9 % sin carga) a cambio de codificar un ~4 % más despacio.
- **Los pulsos de luz nacen ahora en los cantos.** Eran dos círculos flotando sobre el
  vídeo, con su silueta a la vista compitiendo con el mensaje. Ahora son dos franjas que
  asoman desde los laterales y se apagan hacia el centro: se leen como luz, no como formas.

## [0.14.1] - 2026-07-22

### Corregido

- **La versión de terminal (`shrinkstudio`) vuelve a publicarse.** Llevaba sin compilar desde
  que Recortes enseñó al motor a cortar por tramos: la CLI no incluía esa parte del motor y
  ninguna de las cinco variantes (Windows, Linux, macOS) llegaba a la publicación. La app de
  escritorio nunca se vio afectada.
- **Deshacer un lote devuelve también la marca de revisión.** Si tenías un fichero apartado
  y deshacías el renombrado, la marca seguía apuntando al nombre que ya no existía.

## [0.14.0] - 2026-07-22

### Añadido

- **Cola de revisión en Organizar.** Con el botón derecho sobre un fichero puedes apartarlo
  para mirarlo con calma, dejando escrito qué le pasa. Queda marcado con un 🔖, hay un chip
  que deja ver solo los apartados, y **sobrevive al cierre de la app**: al volver los tienes
  ahí sin buscarlos otra vez entre cientos. Si mientras tanto aplicas el renombrado, la marca
  se va con el fichero a su nombre nuevo.
- **Al terminar de exportar en Recortes se ofrece borrar el original.** Solo si TODOS los
  tramos están de verdad en disco, y va a la papelera de reciclaje, así que se puede
  recuperar si al verlos algo no cuadra.
- **Pausar y detener la exportación en Recortes.** Pausar suspende ffmpeg donde va y reanudar
  sigue desde ahí, no desde el principio. Detener corta y **mata el proceso**: comprobado que
  no queda ninguno de fondo, también en el caso traicionero de pausar primero y detener
  después.
- **Aviso mientras se exporta.** El reproductor se apaga a propósito —el vídeo tiene que
  estar libre para poder cortarlo—, así que en vez de un rectángulo negro que parece una
  avería se vela la imagen, late una luz suave y un triángulo explica por qué.
- **Deshacer y rehacer en Recortes (Ctrl+Z, Ctrl+Y y Ctrl+Mayús+Z).**
- **Se avisa antes de perder el trabajo.** Cargar otro vídeo con una exportación en marcha
  ya no se hace a medias: se dice que la detengas. Y si tenías tramos preparados, se
  pregunta antes de descartarlos.

- **Ampliar la línea de tiempo en Recortes (Ctrl + rueda, Ctrl + y Ctrl −).** Hasta 40×, para
  clavar un corte al fotograma en vez de a ojo. Amplía **por donde apuntas**: el punto bajo el
  cursor no se mueve, así que no pierdes de vista lo que estabas mirando. Ctrl+0 vuelve a ver
  el vídeo entero, y arriba a la derecha se indica el aumento.

- **«Partirlo en dos» cuando un fichero trae dos episodios.** Ese caso no se arregla
  eligiendo número —le pongas el que le pongas, pierdes el otro—: hay que partir el vídeo.
  Ahora el resolutor lo ofrece con un botón destacado que lo lleva a Recortes con un corte
  ya puesto por la mitad, listo para arrastrarlo al sitio exacto.
- **Previsualización en la barra del reproductor.** Pasando el ratón por la barra sale el
  fotograma de ese punto con su tiempo, como en Recortes. Si el vídeo todavía está solo en
  la nube no se saca ningún fotograma —eso lo descargaría entero—; en cuanto termina de
  bajar, las previas salen solas.
- **El vídeo arranca solo de verdad.** Se abre con doble clic, y el «soltar» de ese segundo
  clic aterrizaba ya dentro del reproductor recién abierto: lo pausaba al nacer y se quedaba
  en la animación de carga hasta que dabas al play. Ahora solo cuenta como clic el que
  EMPIEZA dentro de la ventana.
- **Una animación mientras el vídeo carga.** El rectángulo negro no decía nada, y con
  «Archivos a petición» la espera puede ser larga de verdad porque el fichero se está
  descargando entero. Ahora laten cuatro círculos en cascada —cada uno con su onda
  expansiva— y el texto dice si el vídeo está bajando de la nube o solo abriéndose. Se
  retira cuando el vídeo AVANZA de verdad, no cuando dice estar abierto: con un fichero
  descargándose eso ocurre mucho antes que el primer fotograma.


- **Vista previa del fotograma al recorrer la barra en Recortes.** Encontrar el punto de
  corte mirando una barra lisa era adivinar: ahora, al pasar o arrastrar por la linea de
  tiempo, sale un globo con el fotograma de ese punto y su minuto. Se sacan bajo demanda de
  donde esta el cursor y se sueltan enteros al cambiar de video o salir de la pagina: ni un
  fichero temporal ni un mapa de bits se quedan acumulados.
- **La pagina avisa mientras prepara el video.** Analizar y sacar los fotogramas lleva unos
  segundos: durante ese rato los controles estan deshabilitados y se ve que se esta
  haciendo y cuanto queda, en vez de parecer que la app se ha colgado.
- **Mas feedback al exportar:** el boton dice cuantos tramos va a sacar («Exportar 2
  tramos») y el tramo que se esta procesando se resalta en la lista con la marca
  EXPORTANDO.
- **Recortes tiene una pista de edición, como un editor de vídeo.** Donde antes había dos
  cosas separadas —una línea morada que solo se miraba y una barra debajo para moverse—
  ahora hay una sola pista: el fondo son fotogramas del propio vídeo, cada tramo es un
  bloque con su número y el nombre del fichero que va a salir, y lo que has quitado se ve
  oscurecido, así que de un vistazo sabes qué se va a exportar y qué no.
- **Las juntas entre tramos se arrastran para afinar el corte.** Cortas a ojo y luego tiras
  del tirador hasta el fotograma exacto, con el globo de la previa siguiéndote. Una junta
  no puede pasarse de los tramos de al lado, así que ningún tramo se queda del revés ni a
  cero. Si has quitado un trozo y hay hueco, cada borde se estira por su lado.
- **Las acciones van donde actúan.** «Cortar aquí» ya no está abajo a la derecha: es una ✂
  pegada al cabezal, justo por donde va a partir. Y cada bloque lleva su ✕ para quitarlo,
  que asoma al pasar por encima.


- **Un fichero que contiene dos episodios ya no se renombra solo.** Hay ficheros que
  emparejan dos historias que el catálogo cuenta como episodios distintos: ponerles el
  número de uno pierde al otro en silencio. Ahora se detecta y se te pregunta, diciendo
  cuáles son los dos. No confunde esto con un remake —la misma historia en un episodio
  viejo y en uno moderno es lo normal—: solo salta si el episodio elegido no cubre lo que
  el fichero trae.


- **Progreso de verdad al exportar:** que tramo va, de cuantos, con su nombre y el
  porcentaje; y si el motor se salta uno, se ve el motivo en la propia barra.
- **Se puede elegir donde guardar los recortes.** Por defecto van junto al video original.
- Exportar ya no se puede lanzar dos veces a la vez: cada clic arrancaba otra tanda entera
  sobre los mismos ficheros.


- **Recortes: una tercera sección para partir un vídeo o quitarle un trozo.** Sirve para el
  caso de «este fichero son dos capítulos»: cargas el vídeo, lo llevas por donde separan,
  pulsas «Cortar aquí» y salen dos tramos — cada uno será un fichero. Quitar un tramo
  descarta ese trozo, así que recortar es lo mismo con un paso menos. Si el nombre del
  fichero trae las dos historias («A ┃ B»), cada tramo se nombra solo con la suya.
  Desde Organizar, el botón derecho sobre una fila lo abre ahí directamente.
- **La salida de Recortes usa los mismos ajustes y la misma estimación que Comprimir.**
  Formato, códec, calidad, resolución y audio son los de siempre, y el tamaño estimado sale
  de la misma fórmula, ajustada a lo que de verdad se va a exportar.

### Cambiado

- **Muchas menos decisiones a mano: de 1 a 17 renombrados automáticos sobre la misma
  biblioteca, y de 40 a 17 pendientes.** Tres cosas que obligaban a mirar y no lo merecían:
  las etiquetas de la fuente pegadas al nombre («[Boing HD]») restaban parecido; los
  separadores de historias «A + B» y «A - B» no se reconocían —solo «┃» y «|»—, así que el
  título entero se comparaba contra medio episodio y salía un 58 %; y el nombre de la serie
  se quedaba delante del título («Doraemon (2005) - - El elixir…»), donde su guion se
  confundía con el que separa historias.
- **Que dos ficheros apunten al mismo episodio ya no manda a los dos a revisión.** Solo se
  pide mirarlo cuando el rival llegaba con la misma solvencia. Que uno lo clave por título
  y el otro solo trajera un número dudoso no es una ambigüedad: es un número dudoso.

### Corregido

- **Lo ya renombrado deja de contar como «corregido».** Tras aplicar, esas filas seguían
  sumando en el chip de corregidos y saliendo al filtrar por él, como si quedara trabajo
  pendiente que ya no existe. Ahora cuentan como limpias —están bien en el disco— y, si
  tenías el filtro de corregidos puesto, salen de la vista solas.
- **Simular tarda la mitad.** Sobre 546 ficheros: de ~50 s a ~28 s. El motor preguntaba al
  catálogo dos veces lo mismo — un recorrido completo comparando títulos para elegir el
  mejor episodio y otro idéntico para sacar las alternativas que se te ofrecen. Ahora es un
  solo recorrido del que salen las dos cosas. Ni un fichero de los 546 cambia de respuesta.
- **La app ya no gasta media máquina parada, y todo va más suelto.** Con la app en reposo
  absoluto se consumía un 6-8 % de CPU (y de batería) en repintar decoraciones: la luz
  ambiental redibujaba TODA la interfaz 60 veces por segundo para un latido de 9 segundos,
  el brillo de cada barra de progreso recalculaba su desenfoque en cada fotograma (con una
  cola larga, la app entera se arrastraba: escribir iba a tirones y arrastrar ventanas a
  golpes), y el halo del campo con foco se recalculaba mientras el haz giraba. Medido pieza
  a pieza y arreglado sin quitar nada: los mismos brillos y latidos, pero componiendo
  texturas ya pintadas en vez de repintar. En reposo: de 6-8 % a ~1 %.


- **La app ya sabe releer los ficheros que ella misma marcó como una sola historia.** Al
  decidir «esto es solo la historia b», escribe la letra pegada al número («S2017E487b»)
  — pero no sabía volver a leerla: el fichero se quedaba sin número ni segmento, se
  reidentificaba solo por el título, casaba con el episodio entero y proponía deshacer tu
  decisión. Cada pasada deshacía la anterior.
- **Renombrar y volver a simular ya no puede cambiar de episodio.** Si el título de un
  episodio del catálogo llevaba un número entre corchetes —los hay: «Cuido de mamá
  (LA)[30]»— ese número acababa dentro del nombre propuesto, y al releerlo ganaba al
  «S2005E536» que la propia app había escrito: la segunda pasada creía que era el episodio
  30. Ahora el marcador explícito manda sobre cualquier número suelto. Los ficheros que
  usan la convención de corchetes («[499b] Título») siguen funcionando igual.


- **En Recortes ya se puede escribir el nombre de un tramo entero.** Los atajos de la
  página se comían las teclas antes de que llegaran al cuadro de texto: en el nombre de un
  tramo no se podía poner un espacio, ni escribir una «c», ni mover el cursor con las
  flechas. Ahora, mientras estás escribiendo, las teclas son letras y no atajos.
- **Recortes no exportaba nada.** El motor comprueba que nadie tenga el fichero cogido
  (para no pillar una descarga a medias) y quien lo tenia cogido era el propio reproductor
  de la pagina: se saltaba el video y no salia ni un fichero. Ahora se suelta antes de
  codificar y vuelve al terminar.
- **Recortes decia «2 ficheros creados» sin haber creado ninguno.** Daba por bueno que la
  llamada al motor volviera. Ahora se comprueba el fichero en disco y, si falta, se dice
  cuantos no salieron y con que nombre.

## [0.13.0] - 2026-07-22

### Añadido

- **Menú contextual en la tabla de Organizar.** Clic derecho sobre un fichero para
  reproducirlo o **abrir su ubicación** en el explorador, con el fichero ya seleccionado.
  Abrir la ubicación no descarga nada, así que sirve igual para los que están en la nube.
  También responde a la tecla Menú del teclado, sobre la fila seleccionada.
- **Los vídeos que se descargan de la nube para verlos vuelven a la nube al cerrar.**
  Identificar un capítulo mirándolo medio minuto no debería dejar 250 MB ocupados para
  siempre: si el fichero estaba solo en la nube antes de abrirlo, al cerrar el reproductor
  se pide que se libere. Solo se toca lo que ya estaba en la nube — lo que tengas guardado
  a propósito se queda.

### Cambiado

- **Lo de la nube ya no habla de un proveedor concreto ni promete tirones.** El aviso del
  reproductor pasa a ser «En la nube · descargando para verlo». El mecanismo lo define
  Windows, no un proveedor: funciona igual con OneDrive, Nextcloud, Dropbox, Google Drive
  o iCloud, porque se miran los atributos del fichero y no quién los puso.

## [0.12.1] - 2026-07-22

### Corregido

- **Identificar una carpeta ya no descarga vídeos de la nube.** Para los ficheros que
  quedaban en duda sin `.nfo`, la app abría el vídeo con ffprobe a leer su título — y con
  «Archivos a petición» de OneDrive abrir un fichero lo descarga **entero**: medido, 277 MB
  en 18 segundos por vídeo. En una biblioteca con 467 ficheros así son 90 GB de tarifa y de
  disco sin haberlo pedido. Ahora se reconoce el marcador y no se abre; el resumen dice
  cuántos se han dejado sin mirar por eso. Los `.nfo`, que pesan nada, se siguen leyendo.

## [0.12.0] - 2026-07-22

### Añadido

- **Doble clic sobre una fila para ver el vídeo, sin salir de la app.** Ante la duda de qué
  capítulo es, verlo gana a cualquier metadato — también después de aplicar, donde abre el
  fichero con su nombre nuevo. Se abre una ventana oscura en modo focus. Los controles flotan
  sobre la imagen y se apartan solos a los 2,6 s de no usarlos (vuelven al mover el ratón;
  en pausa se quedan). Barra de posición con punto, salto de ±10 s, volumen, silencio y
  pantalla completa. Atajos: espacio pausa, flechas saltan (con Mayús, 30 s), F pantalla
  completa, M silencio, Esc sale. Doble clic sobre la imagen también expande. Si el códec
  no está soportado, lo dice y ofrece el reproductor del sistema con un botón.
- **Aviso cuando el vídeo está solo en la nube.** Con «Archivos a petición» de OneDrive el
  fichero se descarga mientras se reproduce y la imagen va a tirones. El reproductor lo
  detecta y lo dice, en lugar de parecer que está roto.
- **Los dudosos se identifican también por su `.nfo` y por los metadatos del vídeo.** Un
  fichero sin título en el nombre («S2018E01.mkv») suele llevarlo en su `.nfo` de Kodi o en
  la etiqueta del contenedor. Tras la primera pasada, la app lee esas dos fuentes SOLO de
  los que quedaron en duda —el `.nfo` primero, que es instantáneo— y re-identifica. La
  Season 2018 pasó de 18 dudas a 18 listos con su título de verdad.
- **Los ficheros numerados por temporada («S2018E01», el 1.º de 2018) ya se entienden.**
  Cuando el número del fichero contradice a su carpeta —el episodio 1 del catálogo es de
  2005, no de 2018— o directamente no existe en la numeración global, se relee como «el N.º
  de esa temporada». Sale en ámbar (sin título ni fecha que lo confirme, se revisa) con la
  lectura global de alternativa y la etiqueta «nº de temporada» en la columna del porqué.
- **Los ficheros compañeros (.nfo, .srt…) se renombran junto al vídeo.** Un .nfo con el
  nombre viejo queda huérfano y tu reproductor de biblioteca deja de asociarlo. Van al mismo
  diario del lote, así que «Deshacer» también los devuelve. Un subtítulo «.es.srt» conserva
  su sufijo completo.
- **Buscador dentro de la tabla de Organizar (Ctrl+K).** Filtra en vivo por el nombre
  original o por la propuesta, con la misma normalización del identificador: «animo» sin
  tilde encuentra «¡Ánimo, antepasado!». Esc lo limpia. Se combina con los filtros de
  estado que ya había.
- **Un fichero puede ser SOLO una historia de un episodio, y ya hay forma estándar de
  decirlo.** En el resolutor, «Elegir otro episodio…» abre por fin el explorador (buscando ya
  el título del fichero); al elegir un episodio con varias historias, la app pregunta si el
  fichero es el episodio completo o solo una de ellas. Si es solo una, la letra va pegada al
  número —`E413b`, para no pisarse con el episodio completo ni con la otra mitad— y el título
  es el de esa historia, no el del episodio entero. La decisión se recuerda para ese fichero.

### Corregido

- **«Deshacer este lote» ya no te saca de la tabla.** Deshace en el sitio: las filas del
  lote vuelven de «Hecho» a su estado anterior —con su casilla y su propuesta intactas,
  listas para re-aplicar si era eso lo que querías— y sigues exactamente donde estabas.
- **El texto de los campos se veía ligeramente borroso.** El halo de foco (el efecto de
  sombra) envolvía al propio texto y lo rasterizaba sin ClearType. El halo sigue; el texto
  ya vive fuera de él.
- **Buscar el nombre viejo de un fichero ya renombrado ya no lo encuentra.** Tras aplicar,
  la fila solo responde a su nombre nuevo — que es el que existe en disco. Encontrarla por
  el viejo hacía dudar de si el renombrado había ocurrido de verdad.
- **Volver a simular tras aplicar enseñaba el pasado.** La lista de ficheros se escaneaba al
  elegir la carpeta y no se refrescaba nunca: después de renombrar 462 ficheros, re-simular
  volvía a resolver los nombres viejos y la tabla enseñaba los mismos «Corregido» de antes
  — como si aplicar no hubiera hecho nada, cuando el renombrado sí se había hecho. Ahora
  cada simulación re-escanea la carpeta.
- **Aplicar cientos de renombrados ya no congela la ventana.** Los movimientos van en
  segundo plano y la barra dice «Renombrando N ficheros…» mientras tanto.

## [0.11.0] - 2026-07-22

### Cambiado

- **El panel de ficheros es ahora el centro de la identificación, con progreso animado.**
  La carpeta a organizar, el recuento y «Simular» estaban repartidos por tres sitios de la
  pantalla; ahora viven juntos en el panel. Y al simular, el panel enseña las tres fases
  reales del trabajo —leer los nombres, identificar contra el catálogo, preparar la
  revisión— cada una con su círculo en espera, su arco girando mientras corre y su check
  verde que se dibuja y da un pequeño salto al terminar, con el haz de luz recorriendo el
  borde del panel mientras trabaja.

### Corregido

- **La casilla de aplicar salía recortada por la columna de al lado.** Su columna medía lo
  justo sin contar el relleno interno de la celda.

## [0.10.0] - 2026-07-22

### Añadido

- **Eliges qué se aplica, fichero a fichero.** Cada fila lista lleva su casilla (marcadas
  todas de inicio), la cabecera marca o desmarca todas, y el botón dice exactamente cuántos
  va a tocar («Aplicar 30 de 31»). El cuadro de confirmación cuenta también lo que se queda
  fuera y por qué: dudas, conflictos y lo que tú hayas desmarcado. Los conflictos no llevan
  casilla a propósito: no se aplican jamás, estén como estén.
- **Un explorador del catálogo para comprobar propuestas sin abrir el JSON.** La lupa junto a
  «Catálogos…» abre el catálogo elegido con buscador por número («175») o por título
  («planeta espejo»), con la misma normalización que usa el identificador. Antes, dudar de
  una sugerencia obligaba a rebuscar en el JSON a mano — y esa fricción deja dudas razonables
  sin comprobar.
- **Al elegir un episodio en el explorador, su JSON emerge en el lateral.** Es el fragmento
  del catálogo tal y como lo está leyendo el identificador —no una reconstrucción— con botón
  de copiar. Para cuando la vista bonita no basta y quieres ver la fuente. El JSON va
  **coloreado** (claves, textos, números y símbolos, con los colores del tema) y el panel se
  cierra con su aspa.

### Corregido

- **Los botones de las tarjetas de catálogo, ahora opacos y sin pisar el texto.** Seguían
  siendo transparentes (se leía el resumen a través) y el texto corría por debajo de ellos.
  Ahora tienen acabado de cristal opaco con brillo en el canto, y el resumen se recorta en su
  columna en vez de invadir la de los botones.
- **Las casillas de aplicar no respondían al ratón y ya se pueden marcar arrastrando.** El
  «volver a pinchar una fila la cierra» oía el clic antes que la casilla y se lo comía
  cuando la fila estaba seleccionada: la casilla parecía muerta. Ahora el clic de la casilla
  es de la casilla, y además puedes arrastrar por la columna para marcar o desmarcar varias
  de una pasada — el arrastre contagia el valor del primer toque, sin alternar fila a fila.
- **El nombre de la serie dentro del fichero ya no estropea la identificación por título.**
  «Doraemon (2005) S2009E175 - El planeta espejo» se comparaba con el prefijo incluido, el
  parecido caía por debajo del umbral y acababa ganando el número equivocado del propio
  fichero. Ahora el título se compara también sin la serie delante: ese fichero pasa a
  identificarse como E173 con el título al 100 %. En la biblioteca de prueba, los conflictos
  bajan de 49 a 31.
- **«2.ª parte» ya iguala a «segunda parte»** (y 1.ª/3.ª/4.ª): el fichero y el catálogo suelen
  escribir el ordinal de forma distinta y eso restaba parecido justo donde más dolía.
- **Importar un catálogo también lo guarda como última serie.** Quitar uno y reimportarlo
  dejaba la preferencia vacía y el siguiente arranque volvía a caer en el primero por
  alfabeto.

## [0.9.0] - 2026-07-21

### Añadido

- **Un botón «← Volver» para salir de la simulación** y regresar a la pantalla de inicio. Antes,
  una vez simulabas te quedabas en la tabla y la única salida era cambiar de página y volver.
  No pregunta nada porque no se pierde nada: las decisiones que hayas tomado a mano se guardan
  en cuanto las tomas y se reaplican solas al volver a simular. La carpeta elegida se conserva.

### Eliminado

- **La pestaña «Pasos» se retira.** Se añadió en la 0.8.0 y no ha convencido en el uso, así
  que se quita entera en vez de dejarla ocupando sitio. El registro sigue igual: nunca llegó
  a sustituirlo, así que no se pierde nada de lo que ya había.

### Corregido

- **Las marcas de la plantilla salían dentadas.** Cada una empezaba en una sangría distinta,
  así que la lista se leía como texto centrado en vez de como una tabla. La plantilla de los
  botones clavaba su contenido al centro e ignoraba a quien pidiera otra cosa — le pasaba
  igual a la lista de idiomas.
- **El ejemplo del nombre final ya se puede leer entero.** La línea «Quedaría:» se corta casi
  siempre porque estos títulos son larguísimos; ahora el nombre completo está también en el
  globo de ayuda del campo, junto con la explicación de para qué sirve.

## [0.8.0] - 2026-07-21

### Añadido

- **Una caja de pasos enseña por dónde va el vídeo que se está comprimiendo.** Nueva pestaña
  «Pasos», que se abre sola al empezar: leer el vídeo, elegir pistas y calidad, codificar y
  guardar, cada uno con su marca y lo que se ha averiguado («audio: spa», «42 %», «1,2 GB →
  380 MB»). **No sustituye al registro**: el registro cuenta qué pasó *después* y sirve para
  revisar; esto contesta «¿por dónde va?» de un vistazo, que es lo que se mira *mientras*
  corre. Si algo se tuerce, el paso que falló se marca y los siguientes quedan como
  «saltados», no como fallidos: no fallaron, es que ya no se intentan.
- **La plantilla admite relleno con ceros y separador propio: `<num:000>` y `<título: ┃ >`.**
  Sin esto no se podía describir una biblioteca que ya estuviera ordenada con otra
  convención, y entonces salía todo como pendiente de renombrar aunque el trabajo estuviera
  hecho. El caso que lo destapó: ficheros `S2005E001 - A ┃ B`, que la app sabía **leer** —la
  barra `┃` ya era separador de historias— pero no sabía **escribir**.
- **Los catálogos dicen de qué fichero salieron y cuándo, y se pueden quitar.** La app trabaja
  con una copia del JSON que importas, así que si luego editas el original tu copia se queda
  vieja sin que nada lo delate: ahora cada tarjeta lo dice. Y «Quitar» lo saca de la app sin
  tocar tu fichero, que sigue donde estaba por si quieres volver a importarlo.
- **Se recuerda la última serie elegida.** Con más de un catálogo, cada arranque empezaba en
  el primero por orden alfabético y había que volver a elegir.
- **El selector de idiomas es ahora la norma ISO entera, con buscador.** Antes eran siete
  opciones fijas elegidas a ojo; si tu serie venía titulada en cualquier otro idioma, no
  había forma de decirlo. Ahora se busca por nombre o por código, sin tildes y a medias
  («japones», «ja», «catal»), los elegidos quedan a la vista como etiquetas y se quitan de
  una en una.

### Cambiado

- **Dos códigos de idioma estaban mal y se han corregido a ISO**: el japonés era `jp` —que
  es el código del *país*, no del idioma— y ahora es `ja`; el español de Hispanoamérica era
  `lat` y ahora es `es-419`. **Tus catálogos existentes se siguen leyendo igual**: los
  códigos viejos se traducen solos al abrirlos, así que no hay que regenerar nada. Lo que
  cambia es que los catálogos nuevos ya salen con códigos correctos.
- **«Idiomas para reconocer los ficheros» se llama ahora «Idiomas en los que vienen
  titulados tus ficheros»**, y explica en el globo de ayuda para qué sirve exactamente y en
  qué se diferencia del idioma del nombre final. El rótulo viejo se leía como si fuera el
  idioma del programa.


- **Los avisos y las preguntas ya no son los cuadros grises de Windows.** Toda la app usa
  ahora su propio diálogo, con el mismo tema que el resto, y el texto se puede seleccionar
  y copiar — que es lo primero que quieres hacer cuando el aviso trae una ruta o el texto
  de un error.
- **Un haz de luz recorre el borde de lo que tiene el foco**, en toda la app: campos,
  botones, desplegables y casillas. Se ve de un vistazo dónde estás, sobre todo moviéndote
  con el tabulador. Solo gira mientras ese control tiene el foco, así que nunca hay más de
  uno encendido.
- **Lo que se corta con puntos suspensivos enseña el texto completo al pasar el ratón.** No
  hace falta ensanchar la ventana para leer un nombre largo. Solo aparece cuando el texto
  está recortado de verdad, para no repetir lo que ya se ve.

- **El encargo para la IA ya no da por hecho que el anexo lo tiene todo.** Antes enseñaba un
  único ejemplo con todos los campos rellenos, y ante una tabla pobre la IA acababa
  inventándose fechas o improvisando estructura. Ahora lista los campos que admite el
  programa separando lo obligatorio de lo opcional, enseña también un catálogo mínimo
  igual de válido, y deja una sola regla sin excepción: se pueden omitir campos, nunca
  inventarlos.

### Corregido

- **En las tarjetas de catálogo, «Usar», «Quitar» y «seleccionado» se dibujaban unos encima
  de otros.** Iban los tres pegados a la derecha en el mismo sitio; mientras «Quitar» no
  existía no se notaba, porque los otros dos nunca salen a la vez. Ahora van en fila y con
  fondo sólido: transparentes sobre el título de al lado no había quien los leyera.
- **La lista de idiomas ya no ocupa media ventana ni pisa la vista previa.** Estaba siempre
  desplegada —183 idiomas— cuando lo normal es tocarla una vez y olvidarse. Ahora se ven las
  etiquetas de los elegidos y el buscador se abre con «+ Añadir», igual que el menú de marcas.
- **Un fichero que ya se llama exactamente como debe sale en verde y no cuenta como
  pendiente.** Antes el color lo decidía la confianza de la identificación, así que un
  fichero al que no había que tocarle nada podía salir en ámbar. Y es al revés: que el
  nombre coincida entero con el que produciría la plantilla es la confirmación más fuerte
  que hay. El recuento lo dice aparte («46 ya estaban bien»), para que no parezca que se han
  perdido por el camino.
- **Volver a pinchar una fila abierta la cierra.** Antes el desplegable se quedaba abierto y
  la única forma de recogerlo era abrir otro. Los botones de dentro siguen funcionando: solo
  cierra el clic sobre la fila, no sobre sus opciones.
- **Un «Limpio» en ámbar ya explica por qué.** Desconcertaba con razón: la palabra dice que el
  fichero ya se llama como toca y el color dice que hay algo que mirar. Son dos cosas
  distintas —el nombre puede estar bien y aun así no ser ese episodio— y ahora el globo de
  ayuda lo cuenta al pasar por encima.
- **Los campos de Origen, Destino, Carpeta y Plantilla ya se encienden al escribir en ellos.**
  Eran los únicos que se quedaron sin el haz de foco, porque estaban montados a mano en cada
  pantalla en vez de ser el mismo componente. Ahora lo son, así que lo que se arregle en uno
  vale para todos.
- **Organizar ya lee la serie entera, no solo el primer nivel de la carpeta.** Al apuntar a
  la carpeta de una serie —la que tiene dentro `Season 2005`, `Season 2006`…— decía «no hay
  vídeos» sobre una carpeta con cientos, porque solo miraba los ficheros sueltos de arriba.
  Ahora baja por las subcarpetas y te dice en cuántas ha encontrado los ficheros, así que se
  ve al momento si has apuntado demasiado adentro.
- **La tabla sale separada por temporada**, con su cabecera y su recuento entre una y otra,
  en el orden de la biblioteca: 2005, 2006, 2007… y los vídeos sueltos de la raíz al final.
  Saber de qué carpeta viene cada fila es la mitad de la información cuando hay que decidir
  si una propuesta tiene sentido.


- **El texto se cortaba por abajo en los campos de «Generar con IA».** Tenían una altura
  fija que no daba para una línea con su espaciado.

## [0.7.0] - 2026-07-21

### Añadido

- **El formato del catálogo está documentado y se comprueba al importar.** Un botón
  «¿Qué formato?» abre la especificación con todos los campos, las reglas y un ejemplo
  completo, y «Crear ejemplo…» te guarda un catálogo válido para que lo edites en vez de
  escribirlo a ciegas. Si el archivo tiene fallos, se te dicen **todos juntos** y con el
  episodio concreto: números repetidos (que antes hacían perder un episodio en silencio),
  fechas imposibles o números negativos.
- **Generador de catálogos con IA.** El botón «Generar con IA…» arma el encargo para que una
  IA convierta un anexo de episodios (Wikipedia, Fandom, el que uses) en el catálogo, con el
  formato y las reglas ya dentro. Eliges la serie, la dirección y los idiomas, y lo copias.
  Cada anexo está montado a su manera, así que el texto le dice cómo resolver lo que cambia
  entre ellos: qué columna es el número, qué hacer si solo numeran por temporada, cómo
  tratar los episodios con varias historias y qué fecha usar si hay más de una.

### Cambiado

- **Reconoce ficheros en un idioma y los nombra en otro.** Antes solo se comparaba contra
  los títulos en español, así que un fichero titulado en inglés no se identificaba nunca.
  Ahora el catálogo declara en qué idioma quieres el nombre final y con cuáles hay que
  comparar: `Help Wanted.mkv` se reconoce por su título inglés y se renombra al español.
- **La ventana se puede encoger mucho más.** El mínimo baja de 1000×660 a 820×560, y el
  contenido se adapta en vez de recortarse: los campos se estiran, los botones bajan de
  línea si no caben, el panel de detalle se pliega cuando le quita sitio a la tabla, y en
  la barra de título el conmutador se queda en iconos para que no se coma el menú.
- **El generador de prompts avisa de la trampa de la numeración.** Muchos anexos traen a la
  vez el «número de transmisión» (orden de emisión) y el «número de episodio» (oficial), y
  no dan el mismo resultado: en Doraemon (2005) el estreno es la transmisión 1 pero en la
  numeración oficial es un especial, así que elegir mal desplaza la serie entera. Ahora el
  encargo manda usar el de transmisión salvo que digas otra cosa, y lo explica con ese caso.

- **La plantilla de nombres se construye, no se adivina.** El botón «Editar» no hacía nada
  que no hicieras pinchando en el propio campo; en su sitio hay un desplegable de marcas que
  las inserta donde tengas el cursor y explica qué hace cada una. Debajo, un ejemplo en vivo
  con un episodio real del catálogo, para ver cómo queda el nombre antes de aplicar nada.

### Corregido

- **Se ofrecían «alternativas» en filas que ya estaban resueltas.** Un episodio identificado
  al 100 % venía acompañado de dos candidatos al 67 % y al 65 %: ruido en una fila correcta,
  que encima invitaba a un clic equivocado. Ahora las alternativas solo salen donde hay algo
  que decidir de verdad.
- **El resolutor de conflictos ofrecía dos episodios y ninguno era el bueno.** Enseñaba
  solo los descartados, así que la propuesta correcta no aparecía por ningún lado y las dos
  opciones parecían equivocadas —lo estaban—. Ahora la primera tarjeta es la que propone la
  app, marcada como «más probable», y **cada opción enseña el nombre que quedaría** si la
  eliges, para no decidir a ciegas.
- **La temporada del fichero no se usaba para nada.** Se leía del nombre y de la carpeta y
  después se tiraba, así que un episodio de 2014 competía de tú a tú con el de 2005 que el
  propio fichero estaba declarando. Además, cuando un fichero trae varios títulos, ahora
  gana el episodio que explica **más** de ellos, no el que casa mejor con uno solo. Entre las
  dos cosas, muchas preguntas que antes te hacía se resuelven solas.
- **Cuando dos ficheros se disputaban el mismo episodio no se veía qué episodio era.** El
  aviso nombraba el número pero no su título, así que no había forma de juzgar cuál de los
  dos ficheros tenía razón — que es justo lo que hay que decidir ahí. Ahora se dice qué
  título espera el catálogo para ese número, y con qué fichero compite.
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
