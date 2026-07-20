# Brief para rediseño visual — ShrinkStudio

> Pega esto en Claude Design (adjunta también `docs/icon.png` y, si puedes, una captura actual de la app).

---

Eres un diseñador de producto. Quiero un **lavado de cara visual** de mi app de escritorio Windows **ShrinkStudio**, un transcodificador de vídeo pensado como **relevo ligero y moderno de HandBrake**. Mantén la funcionalidad; céntrate en la estética y en una estructura que pueda crecer: jerarquía, espaciado, color, tipografía, iconografía y microinteracciones.

## Qué es
Comprime y convierte vídeos (series, películas, capítulos) a **H.265 / H.264 / AV1** en **MP4 o MKV**, con aceleración por hardware, conservando los idiomas de audio elegidos. Su punto fuerte frente a HandBrake es el **procesamiento por lotes**: el usuario elige una carpeta o archivos, la app analiza los vídeos (pistas, duración, tamaño) y él marca cuáles convertir.

## Plataforma y restricciones (IMPORTANTE)
- Escritorio Windows en **C#/WPF**. El diseño debe ser **implementable en WPF**: ventana redimensionable, controles nativos (botones, ComboBox, CheckBox, tabla tipo DataGrid/ListView, ProgressBar, tarjetas redondeadas, iconos como glifos/vectores, y si hace falta **pestañas/TabControl** o paneles laterales). Evita efectos que WPF no dé con facilidad.
- Ventana ~1180×780, mínimo 980×640, **una sola ventana**. Tema oscuro.

## Estilo actual (punto de partida; puedes proponer otro)
Fondo `#0F1216`, tarjetas `#1A1F26`, campos `#232A33`, texto `#E8EAED`, atenuado `#8A93A0`, **acento turquesa `#6CE8D0`**. Icono: squircle turquesa→teal con *play* central y flechas de compresión (adjunto).

## Inventario ACTUAL (debe seguir existiendo)
1. **Origen / Destino**: campo Origen + botón carpeta + botón añadir archivos sueltos; campo Destino opcional + botón.
2. **Opciones** (todas con valor por defecto): Idioma principal, **Formato (MKV/MP4)**, **Códec (H.265/H.264/AV1)**, Calidad (Automática/22/24/27/30), Resolución máx. (Sin cambio/1080p/720p/480p), Audio (Máxima copiar / AAC 192·160·128·96), y checkboxes Subcarpetas, Reprocesar hechos, Solo simular. Debajo, chips de idioma de **Audio detectado** y **Subtítulos** (checkbox por idioma) que aparecen tras analizar.
3. **Lista de vídeos** (tabla): casilla, Vídeo, Carpeta, Tamaño, Duración, Códec, Audio, Subs, Estado.
4. **Panel lateral**: miniatura + nombre + info; botones Marcar todos, Desmarcar todos, Eliminar marcados (papelera), Eliminar carpeta (papelera).
5. **Acciones**: Analizar, Comprimir marcados, Cancelar, Abrir destino; a la derecha estado + Buscar actualizaciones.
6. **Barra de progreso**, **Registro** plegable, y **banner de actualización** superior (oculto hasta que hay versión nueva: mensaje + Actualizar ahora / Después).

## Funcionalidades EN CAMINO (el diseño debe dejarles sitio con elegancia)
Vienen de HandBrake y ampliarán el panel de opciones. Propón cómo acomodarlas sin recargar (p. ej. **pestañas** tipo Resumen / Vídeo / Audio / Subtítulos / Filtros, o secciones plegables):
- **Presets**: guardar/cargar configuraciones con nombre (+ presets de fábrica).
- **Vídeo avanzado**: modo bitrate objetivo, preset de velocidad, **recorte (crop) y dimensiones exactas**, **filtros** (desentrelazado, denoise, nitidez).
- **Audio avanzado**: elegir códec por pista (AAC/AC3/Opus/FLAC/passthrough), mezcla estéreo/5.1, varias pistas de salida.
- **Subtítulos**: quemar (burn-in), *forced*, importar `.srt`.
- **Flujo**: cola de trabajos con ajustes por trabajo, recortar por tiempo (inicio–fin), previsualización.

## Estados a diseñar (mockups)
- **Vacío** (recién abierta).
- **Con lista analizada** (chips de idioma, una fila seleccionada con miniatura).
- **Comprimiendo** (barra de progreso, Cancelar activo).
- **Con banner de actualización**.
- **Opciones avanzadas abiertas** (muestra tu propuesta de pestañas/secciones para lo "en camino").

## Qué quiero de ti
1. **Dirección visual** (paleta, tipografía, iconos, tarjetas/bordes/sombras): refina la actual o propón otra, manteniendo el aire oscuro y moderno.
2. **Mockups** de la ventana en los estados de arriba.
3. **Sistema para las opciones avanzadas** (cómo organizar Resumen/Vídeo/Audio/Subtítulos/Filtros/Cola sin abrumar).
4. **Componentes**: botón primario/secundario, campo, combo, chip, fila de tabla (normal/seleccionada/hover), barra de progreso, banner, pestaña.
5. **Notas de implementación en WPF** donde no sea evidente.

Prioriza el recorrido principal: **elegir origen → Analizar → marcar → Comprimir**. Lo avanzado debe estar disponible pero no estorbar a quien solo quiere comprimir rápido.
