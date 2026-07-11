# Ola Q — Firma visual (Q1/Q2) + El Atlas (C18)

> Estado: **terminada y verde**. Cierra Q1 (degradación tipográfica por rank) y Q2 (modo claro
> híbrido) por la autorización explícita de Pedro (Q1 = Opción 1 con el mapa corregido de D38;
> Q2 = híbrido), y construye C18 (El Atlas). Frontera respetada al 100 %: **solo `src/web/server/**`
> y `src/front/**`** (+ tests). Cero migraciones, cero `src/shared/**`, cero `src/console/**`.
> `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones, 0 skips). Fecha: 2026-07-11.

Complementa `skeleton.md` (fontsource/Redaction), `rite-front.md` (el reveal que aquí se sustituye)
y `data-backbone.md` (los 309 `xy` que el Atlas consume).

---

## 1. Q1 — degradación tipográfica por rank, cableada (D14/D38)

Antes: `redactionCutForRank` existía, testeada, **sin cablear** (rite-front.md), y el reveal usaba
un filtro blur/contraste como sustituto. Ahora el rank está poblado (290/309 con rank real), así que
el corte por rank **renderiza la verdad, no una mentira** — se cablea de verdad.

### Qué se hizo

- **Seis paquetes graduados a fontsource** (`@fontsource/redaction-10/-20/-35/-50/-70/-100`,
  v5.2.5) añadidos a `package.json` e importados en `main.tsx` (peso 400). Las familias reales son
  `'Redaction 10'` … `'Redaction 100'` (verificado en los woff2 instalados). El de `10` es el más
  pesado (126 KB woff2), confirmando D38: **10 = corroído, 100 = nítido**.
- **`core/domain/redaction.ts`** (puro, sin DOM): `redactionCutForRank` ahora acepta `Rank | null`
  y **null → 100 (base nítido)** — desconocido ≠ raro (misma regla que el null neutro del motor,
  D35). Nuevas funciones puras: `redactionFontFamily(cut)` (string de font-family con fallbacks) y
  `revealCutSequence(target)` (la escalera de cortes 10→target que recorre el reveal).
- **`ui/RankedName.tsx`** (nuevo): renderiza un nombre de banda en el corte que su rank merece. Se
  usa en el **`<h1>` de la ficha** (`ArtistPage`). Known → 100 nítida, Nameless → 10 corroída, rank
  null → 100. La corrosión es **solo para el nombre de banda** (el dato); el icono/marca no se toca
  (D27).
- **`ui/rite/RevealName.tsx`** (reescrito): el reveal ya no es blur/contraste, es un **revelado por
  cortes graduados reales** (DESIGN §3.1). El nombre empieza en el corte 10 (máx. corrosión) y
  camina la secuencia hasta el corte de su rank en 600 ms, dirigido por estado de React (D12
  permite "transiciones dirigidas por estado"). Una `Known` llega a 100 (nítida); una `Nameless`
  es un solo frame en 10 que **nunca se resuelve**. `prefers-reduced-motion` → muestra el corte
  final al instante (gate puro en `core/reveal.ts`, ya existente). El reveal recibe `rank` desde
  `RiteConsole` (`reveal.artist.rank`).

### Tests que muerden (Vitest node)

- `redaction.test.ts`: null → base 100; `revealCutSequence(100)` = `[10,20,35,50,70,100]`;
  `revealCutSequence(10)` = `[10]` (nunca resuelve); monotonía; `redactionFontFamily`.
  **Comprobado que muerden**: invertir el filtro de `revealCutSequence` a `cut >= target` → 3
  fallos; revertido → verde.

---

## 2. Q2 — modo claro híbrido (DESIGN §2)

Antes: el semitono se pintaba en **todo el `body`** en modo claro (incluidos los cuerpos de lectura
larga de la ficha). Q2 híbrido lo restringe a **superficies de impacto**.

### Qué se hizo (`styles.css` + dos surfaces)

- El grano del `body` global se retira. Se añade la clase **`.flyer`**: en claro pinta el flyer
  fotocopiado (dos capas de halftone/tóner con `radial-gradient` + `color-mix`); en **oscuro no
  pinta nada** (la cinta es limpia, el vacío no tiene grano — D14). Semitono **solo en claro**.
- Aplicada a las dos superficies de impacto: el **splash** (la cabecera de `SearchPage`) y el
  **reveal del Rito** (la caja de la banda revelada en `RiteConsole`). Los cuerpos de lectura larga
  de la ficha quedan **papel limpio**.
- **Contraste D27 ya cumplido desde mov. I**: `--sulphur-light` es la variante oscura (`#8F7C18`,
  L≈0.587), no el azufre brillante `#D6C34A`; en claro el azufre nunca es texto ni se introdujo el
  brillante. No se tocó la paleta.

---

## 3. C18 — El Atlas (B22/B23)

El catálogo entero como campo de estrellas 2D, desde los **309 `artists.xy`** (proyección PCA del
backbone). **Canvas 2D**, la excepción explícita al invariante 6 (D18/D24), declarada en el código;
toda la matemática de coordenadas vive **pura en `core/`** y se testea sin canvas.

### Backend (`src/web/server`)

- **`Controllers/AtlasController.cs`** — `GET /api/atlas`, **anónimo**. Devuelve las 309 estrellas
  (`id, name, kind, rank, x, y`) y, si el llamante lleva un bearer válido y tiene taste, su
  **posición de gusto proyectada** ("you are here"). Nada inventado: una banda sin `xy` simplemente
  no aparece (no se cae al origen), y el marcador de taste se **omite** en vez de falsearse cuando
  no hay vector.
- **`Services/AtlasProjection.cs`** (puro) — el hallazgo del pase: la proyección PCA no persiste sus
  componentes, pero **se reconstruyen exactos** desde los pares (embedding, xy) guardados. Como
  `s = C·pc1` (la columna `xy_x`) y `pc1` es autovector, `pc1 = (Cᵀs)/‖s‖²` — una suma ponderada de
  los embeddings centrados. Reproduce las coordenadas de cada estrella y, por linealidad, se aplica
  igual al vector de taste. Cero re-centrado por la media del corpus (D26/D31).
- **`Services/AtlasProjector.cs`** — singleton que carga y cachea la base reconstruida (la
  proyección solo cambia cuando el pase offline `atlas` se re-ejecuta).
- **`Program.cs`** — registra `AtlasProjector`.

### Frontend (`src/front`)

- **`core/domain/atlas.ts`** (puro): `atlasBounds`, `fitAtlas` (reusa el auto-fit tuneado de
  `graph.ts`), `atlasScreenOf` (mundo→pantalla con zoom/pan), `starsNearTaste` (las N estrellas más
  cercanas al taste, que se pintan vivas). Tipos `AtlasStar/AtlasTaste/Atlas` en `types.ts`.
- **`core/hooks/useAtlas.ts`** + `client.atlas()` (auth opcional: adjunta el bearer solo si existe).
- **`ui/atlas/AtlasCanvas.tsx`** — el canvas: nebulosa de densidad (glows aditivos que se acumulan
  en los cúmulos → los huecos entre cúmulos son los **gaps B23**) + estrellas; las cercanas al taste
  vivas y sulfuro; el marcador "you are here" un anillo sulfuro. **Todas clicables** (hit-test →
  ficha). Zoom/pan; redibuja al cambiar de tema (MutationObserver sobre la clase de `<html>`).
  Declara en el código la excepción al invariante 6.
- **`ui/pages/AtlasPage.tsx`** + ruta `/atlas` + nav en `Layout`. Estados vacíos diseñados: sin
  estrellas ("no sky yet"); sin taste (nota "siembra tu gusto" con enlace al Rito, distinta para
  anónimo vs logueado).
- i18n `atlas.*` + `nav.atlas` en **es y en** ambos.

### Tests que muerden

- `core/domain/atlas.test.ts` (8): bounds vacío/extremos, fit deja toda estrella dentro del
  viewport, el taste lejano no se recorta, el zoom aleja del centro, `starsNearTaste` elige las más
  cercanas y respeta el count.
- `AtlasProjectionTests.cs` (7, xUnit): reconstrucción reproduce las coordenadas de cada estrella;
  proyecta un vector fresco por sus coords centradas; nulls para <2 estrellas / mismatch /
  degenerado; throw por dimensión. **Comprobado que muerden**: quitar el centrado en `Project`
  (`vector[j]` en vez de `vector[j] - Mean[j]`) → 2 fallos; revertido → verde.

---

## 4. Verificación en vivo (comando → salida real)

API `:5080` (Development) + Postgres `:5433`, corpus de 2478 artistas (309 con `xy`).

```
GET /api/atlas (anónimo)        → stars: 309 | taste: None
  distribución de rank en stars → Known 76, Obscure 104, Hidden 67, Forgotten 28, Nameless 15, null 19
register → seed 5 bandas → GET /api/atlas (con bearer):
  taste: (2.057, -0.689)
  star x∈[-4.77, 8.12]  y∈[-5.51, 7.79]   taste within bounds: True
  centroide de las 5 estrellas sembradas: (2.06, -0.69)  ← IDÉNTICO al taste proyectado
```

El match **exacto** taste↔centroide confirma que la proyección es correcta (el taste es la media de
5 embeddings centrados y, por linealidad de la PCA, cae exactamente en la media de sus 5 `xy`) — no
un punto inventado.

**Reactividad (prueba anti-cascarón del REVIEW)**: `update artists set xy_x=999.5 where id=...` en
Postgres → `GET /api/atlas` devuelve `x=999.5` en esa estrella; restaurado. La vista lee la base
viva, no una constante.

**Ruta de datos del rank→corte**: `GET /api/artists/{Accept}` → `rank: Known` (→ corte 100 nítido);
existen bandas `Nameless` (Albert Bell's Sacro Sanctus → corte 10 corroído). El `<h1>` y el reveal
consumen ese rank real.

### Gate

```
bash scripts/audit.sh --strict → RESULT: PASS (0 violaciones, 0 skips)
dotnet test → 216 passed        pnpm test → 74 passed        pnpm build → ✓ (6 caras graduadas en dist)
```

---

## 5. Huecos declarados (y su porqué)

- **Sin captura E2E de navegador headless.** No hay herramienta de navegador en este entorno (igual
  que skeleton/rite-front). Se verificó (a) el grafo de módulos vía `pnpm build`, (b) la lógica pura
  vía tests que muerden, (c) **toda la ruta de datos que la UI consume** contra la API viva,
  incluida la reactividad contra Postgres. El render del canvas no se fotografió en un navegador
  real.
- **La base del Atlas se cachea por vida del proceso.** `AtlasProjector` reconstruye la base una vez;
  si el pase offline `atlas` re-proyecta (raro), hay que reiniciar la API para recoger la nueva base.
  Las **estrellas** sí se leen frescas en cada request (por eso la reactividad de `xy` funciona en
  caliente); solo la proyección del taste usa la base cacheada. Anotado en el servicio.
- **Proyección lineal (PCA), heredada de data-backbone.** Si algún día hay UMAP, cambia el pase
  offline; la reconstrucción exacta aquí **solo vale para una proyección lineal** — con UMAP habría
  que persistir el modelo y proyectar el taste de otra forma (o omitir el marcador). Declarado.
- **La degradación por rank se aplica a ficha + reveal, no a los resultados de búsqueda ni al
  grimorio.** Deliberado: el nombre en la lista de búsqueda es texto pequeño donde el corte 10 sería
  ilegible; el brief acotó Q1 a "ficha y reveal del Rito". `RankedName` es reutilizable si se decide
  extenderlo.
- **17 bandas con `rank` null** (Last.fm no indexa su mbid, D37) y **19 estrellas con rank null** en
  el Atlas: renderizan nítidas (base 100) y como estrella lisa. Null honesto, no corrosión inventada.
```
