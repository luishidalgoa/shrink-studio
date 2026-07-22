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
