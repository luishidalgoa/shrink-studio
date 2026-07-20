# Brief para rediseño visual — "Comprimir vídeos"

> Pega esto en Claude Design (adjunta también `docs/icon.png` y, si tienes, una captura actual de la app).

---

Eres un diseñador de producto. Quiero un **lavado de cara visual** de mi app de escritorio Windows llamada **"Comprimir vídeos"**. Mantén toda la funcionalidad y la estructura general; céntrate en la estética: jerarquía, espaciado, color, tipografía, iconografía y microinteracciones.

## Qué es la app
Una utilidad de escritorio que **comprime vídeos** (series, películas, capítulos) a HEVC para reducir su peso ~80–90 %, conservando los idiomas de audio elegidos. El usuario elige una carpeta o archivos sueltos, la app analiza los vídeos y él marca cuáles comprimir.

## Plataforma y restricciones (IMPORTANTE)
- App **de escritorio Windows, hecha en C#/WPF**. El diseño debe ser **implementable en WPF**: ventana redimensionable, controles nativos (botones, ComboBox, CheckBox, tabla tipo DataGrid/ListView, ProgressBar, tarjetas con esquinas redondeadas, iconos como glifos o vectores). Evita efectos que WPF no pueda reproducir con facilidad.
- Ventana ~1180×780, redimensionable (mínimo 980×640). **Una sola ventana** (no hay navegación entre páginas).
- Tema oscuro.

## Estilo actual (punto de partida; puedes proponer otro)
- Fondo `#0F1216`, tarjetas `#1A1F26`, campos `#232A33`, texto `#E8EAED`, texto atenuado `#8A93A0`, **acento turquesa `#6CE8D0`**.
- Icono: squircle con degradado turquesa→teal, botón *play* central y flechas de compresión hacia dentro (adjunto).

## Inventario de la interfaz (todo esto debe seguir existiendo)
1. **Barra superior — Origen y Destino:** campo "Origen" (ruta) + botón *elegir carpeta* + botón *añadir archivos sueltos*; campo "Destino" (opcional; vacío = subcarpeta `comprimido`) + botón *elegir*.
2. **Panel de opciones** (todas con valor por defecto): Idioma principal (editable), Calidad de vídeo (Automática / 22 / 24 / 27 / 30), Resolución máx. (Sin cambio / 1080p / 720p / 480p), Audio (Máxima = copiar original / AAC 192·160·128·96 kbps), y checkboxes *Subcarpetas*, *Reprocesar hechos*, *Solo simular*. Debajo, filas "Audio detectado" y "Subtítulos" con **chips** (un checkbox por idioma) que aparecen tras analizar, para elegir qué idiomas conservar.
3. **Lista central de vídeos** (tabla) con columnas: casilla, Vídeo (nombre), Carpeta, Tamaño, Duración, Códec, Audio (idiomas), Subs, Estado.
4. **Panel lateral derecho:** miniatura (fotograma) del vídeo seleccionado + nombre + info; botones *Marcar todos*, *Desmarcar todos*, *Eliminar marcados* (a papelera), *Eliminar carpeta* (a papelera).
5. **Barra de acciones inferior:** *Analizar*, *Comprimir marcados*, *Cancelar*, *Abrir destino*. A la derecha: texto de estado + *Buscar actualizaciones*.
6. **Barra de progreso** (aparece al comprimir).
7. **Registro** (log) plegable, abajo.
8. **Banner de actualización** (arriba, oculto hasta que hay versión nueva): mensaje + *Actualizar ahora* / *Después*.

## Estados a diseñar (mockups)
- **Vacío** (recién abierta, sin analizar).
- **Con lista analizada** (vídeos detectados, chips de idioma visibles, uno seleccionado mostrando miniatura).
- **Comprimiendo** (barra de progreso activa, botón *Cancelar* habilitado).
- **Con banner de actualización** visible.

## Qué quiero de ti
1. Una **dirección visual** (paleta, tipografía, estilo de iconos, tratamiento de tarjetas/bordes/sombras): refina la actual o propón una nueva, manteniendo el aire oscuro y moderno.
2. **Mockups** de la ventana principal en los estados de arriba.
3. **Componentes clave**: botón primario/secundario, campo, combo, chip de idioma, fila de tabla (normal/seleccionada/hover), barra de progreso, banner.
4. **Notas de implementación en WPF** donde algo no sea evidente.

Prioriza claridad y jerarquía: el recorrido principal es **elegir origen → Analizar → marcar → Comprimir**.
