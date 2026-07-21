# Formato del catálogo de referencia (`reindex/1.0`)

Un **catálogo** es la lista de episodios de una serie: qué número tiene cada uno, cuándo se
emitió y cómo se titula. La página **Organizar** compara los nombres de tus ficheros con esa
lista para saber qué episodio es cada uno.

Es un archivo **JSON en UTF-8**. Este documento define qué debe contener.

---

## Lo mínimo que funciona

```json
{
  "esquema": "reindex/1.0",
  "serie": "Doraemon (2005)",
  "episodios": [
    { "num": 1, "titulos": { "es": ["Con calma y con prisa"] } },
    { "num": 2, "titulos": { "es": ["El interruptor del despotismo"] } }
  ]
}
```

Con eso ya se puede identificar por título. Todo lo demás es opcional, pero cuanto más
completes, menos dudas tendrás que resolver a mano.

---

## Campos de la raíz

| Campo | Obligatorio | Qué es |
|---|:---:|---|
| `esquema` | **sí** | Siempre `"reindex/1.0"`. Es lo que permite que la app rechace un archivo que no es un catálogo, en vez de intentar leerlo y fallar de mala manera. |
| `serie` | **sí** | Nombre de la serie tal cual quieres que aparezca en el nombre de los ficheros. Ojo: esto acaba escrito en el disco, así que escríbelo como lo quieres ver. |
| `episodios` | **sí** | La lista. No puede estar vacía. |
| `clave` | no | Qué significa `num` en esta serie: `"oficial"`, `"segmento"`, `"continuo"`… Es documentación para ti; la app no decide nada con ello. |
| `notas` | no | Texto libre. Un buen sitio para apuntar rarezas de la serie. |
| `total` | no | Cuántos episodios esperas. **La app no lo usa para recorrer nada**, solo es informativo. |
| `idiomas` | no | Qué idioma se escribe y con cuáles se compara (ver abajo). Ausente = se escribe `es` y se compara contra todos. |

> **Por qué `total` no manda:** las numeraciones oficiales saltan números. Doraemon (2005) no
> tiene el 56, el 138 ni el 173. Si el programa recorriera `1..total` daría por perdidos
> episodios que sencillamente no existen. Se recorre la lista real, siempre.

---

## Campos de cada episodio

| Campo | Obligatorio | Qué es |
|---|:---:|---|
| `num` | **sí** | El número **de destino**: el que acabará en el nombre del fichero. Entero ≥ 0 y **único** en todo el catálogo. |
| `titulos` | recomendado | Títulos por idioma: `{ "es": [...], "lat": [...], "jp": [...] }`. **Siempre listas**, aunque solo haya un título. |
| `temporada` | recomendado | Año o número de temporada. Se usa en el nombre final (`S2005E1`). |
| `fecha` | recomendado | Fecha de emisión en `AAAA-MM-DD`. Es la señal **más fiable** que existe. |
| `especial` | no | `true` para especiales. Por defecto `false`. |
| `aliases` | no | Otros títulos por los que se conoce el episodio. Cuentan igual que los de `titulos`. |
| `emitido_es` | no | Informativo. |

### `titulos` son listas, y no por capricho

Un episodio puede contener **varias mini-historias** en la misma emisión. Cualquiera de
ellas sirve para identificarlo, porque tu fichero puede llamarse por una sola:

```json
{
  "num": 1,
  "temporada": 2005,
  "fecha": "2005-04-22",
  "titulos": {
    "es": ["Con calma y con prisa", "La mujer de Nobita"],
    "jp": ["のろのろ、じたばた", "のび太のおよめさん"]
  }
}
```

Con esto, un fichero llamado `La mujer de Nobita.mkv` se identifica igual de bien que uno
llamado `Con calma y con prisa.mkv`.

Solo se comparan **`es`, `lat` y `aliases`**. El japonés se guarda como referencia pero no se
usa para emparejar, porque tus ficheros no vienen en japonés.

### Reconocer en un idioma, nombrar en otro

Son dos cosas distintas, y por eso se configuran por separado:

```json
"idiomas": { "salida": "es", "comparar": ["es", "en"] }
```

- **`salida`** es el idioma del título que acaba escrito en el nombre del fichero.
- **`comparar`** son los idiomas con los que se intenta reconocer el fichero.

El caso que lo justifica: tus ficheros llegan titulados en inglés (`Help Wanted.mkv`) pero
los quieres en español. Con `en` entre los comparables, el motor reconoce el fichero por su
título inglés y propone `… - Ayudante de cocina.mkv`. Sin él, ese fichero no se identifica
en absoluto.

Si omites `comparar`, se comparan **todos** los idiomas del catálogo, que es lo razonable:
comparar de más no hace daño —los idiomas que no comparten alfabeto con el nombre del
fichero se descartan solos al normalizar, porque el japonés se queda en cadena vacía— y
comparar de menos deja ficheros sin reconocer.

Si a un episodio le falta el idioma de salida, se usa el primero que tenga: mejor un nombre
en otro idioma que un «Episodio 437» a secas.

### La fecha vale más que el título

El orden de fiabilidad que usa el motor es: número + fecha exacta → título → número + fecha
aproximada. Un catálogo **sin fechas** obliga a tirar solo del título, y eso multiplica las
dudas: la app te avisa de ello al importarlo.

---

## Reglas que se comprueban al importar

Si algo de esto falla, el archivo **no se importa** y se te dice exactamente qué corregir.

1. `esquema` empieza por `reindex/` y su versión mayor es 1. Un `reindex/2.0` se rechaza
   antes que leerlo a medias con reglas que esta versión no conoce.
2. `serie` no está vacío.
3. `episodios` existe y tiene al menos uno.
4. Cada episodio tiene `num`, entero y ≥ 0.
5. **`num` no se repite.** Dos episodios con el mismo número son un error: uno pisaría al
   otro y perderías un episodio sin enterarte.
6. `fecha`, si está, es una fecha real en formato `AAAA-MM-DD`. Un `2005-13-45` se rechaza.
7. `temporada`, si está, es un entero ≥ 0.

Se comprueban **todos** los episodios y se te enseñan los fallos juntos, no de uno en uno.

### Lo que NO es error, pero se avisa

Al importar verás advertencias en la tarjeta del catálogo. No impiden nada, pero cambian
cuánto puedes fiarte del resultado:

- Saltos en la numeración (los números vecinos no son consecutivos).
- Episodios sin fecha.
- Episodios sin ningún título comparable (solo en japonés): a esos no se llega por título.
- Remakes: episodios distintos con el mismo título años después.
- Especiales: se numeran aparte y siempre piden confirmación.

---

## Especiales

Los especiales van en la misma lista, con `especial: true`. Conviene numerarlos en un rango
propio para que no choquen con la numeración regular — los catálogos de ejemplo usan `900+`:

```json
{ "num": 901, "especial": true, "temporada": 2005,
  "titulos": { "es": ["Especial de Navidad"] } }
```

Un fichero marcado como especial en su nombre (`[S]`, `[S3]`) **solo** se compara contra
episodios con `especial: true`, nunca contra la numeración regular.

---

## Compatibilidad hacia delante

- Los campos que la app no conoce **se ignoran**, no dan error. Puedes añadir anotaciones
  tuyas sin romper nada.
- Un `reindex/1.9` se lee igual que un `1.0`: mientras la versión mayor sea 1, las reglas
  son estas.
- Un `reindex/2.0` se rechaza a propósito, con un mensaje que te dice que actualices la app.

---

## Ejemplo completo

```json
{
  "esquema": "reindex/1.0",
  "serie": "Doraemon (2005)",
  "clave": "oficial",
  "notas": "La numeración oficial salta el 56, 138 y 173: no son huecos.",
  "total": 769,
  "episodios": [
    {
      "num": 1,
      "temporada": 2005,
      "fecha": "2005-04-22",
      "especial": false,
      "titulos": {
        "es": ["Con calma y con prisa", "La mujer de Nobita"],
        "jp": ["のろのろ、じたばた", "のび太のおよめさん"]
      },
      "aliases": []
    },
    {
      "num": 2,
      "temporada": 2005,
      "fecha": "2005-04-29",
      "titulos": { "es": ["El interruptor del despotismo"] }
    },
    {
      "num": 901,
      "temporada": 2005,
      "especial": true,
      "titulos": { "es": ["Especial de Navidad"] }
    }
  ]
}
```

---

## Generarlo con una IA

Escribir a mano el catálogo de una serie de 800 episodios no es razonable. En la página
**Organizar**, el botón **«Generar con IA…»** arma el encargo por ti: le pones el nombre de
la serie, la dirección del anexo (Wikipedia, Fandom…) y los idiomas, y te copia un texto
listo para pegárselo a una IA que sepa leer páginas web.

Ese texto ya lleva dentro este formato y estas reglas, así que no tienes que explicar nada
más. Además le dice cómo resolver lo que cambia de un anexo a otro: qué columna es el
número cuando hay dos numeraciones, qué hacer si solo numeran por temporada, cómo tratar
los episodios con varias historias en una emisión, y qué fecha usar cuando aparecen la de
emisión original y la de estreno en España.

Lo que devuelva, impórtalo con **«Importar JSON»**. Si algo no cuadra, la validación te lo
dirá con el episodio concreto.

## Cómo se compone el nombre final

La **plantilla de biblioteca**, en la barra superior de Organizar. Por defecto:

```
<serie> - S<temp>E<num> - <título>
```

Con el episodio 1 del ejemplo sale:

```
Doraemon (2005) - S2005E1 - Con calma y con prisa + La mujer de Nobita.mkv
```

Marcas disponibles: `<serie>`, `<temp>`, `<num>`, `<título>` (o `<titulo>`) y `<seg>` para
la letra de sub-segmento. La extensión original se conserva siempre, y los caracteres que
Windows no admite en un nombre (`: ? " < > | / \ *`) se sustituyen por espacios.
