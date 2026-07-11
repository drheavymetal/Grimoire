# Movimiento II — Features de grabaciones (web + front)

> Estado: **cerradas y verificadas contra la base viva.** `bash scripts/audit.sh --strict` → PASS
> (dotnet-build/test, pnpm-lint/build todos verdes). Frontera respetada: **solo** `src/web/server/**`
> y `src/front/**` (+ tests). **No** se tocaron migraciones, `src/shared/**` ni `src/console/**`;
> las tablas `recordings` (8.9M) y `cover_versions` (21 418) ya existían del pase de import.
> Construye sobre el contrato de `recordings-import.md`. Fecha: **2026-07-11**.

Cinco features, todas leyendo datos reales por hooks de `core/`, sin mocks, estados vacíos diseñados
en la voz v2, i18n es/en, y grafos envueltos en `GraphErrorBoundary`.

---

## B5 — Tracklist en la ficha

- **Endpoint**: `GET /api/artists/{id}/releases/{releaseId}/tracks` → `TrackDto { position, title, lengthMs }`,
  `ORDER BY position`. Valida que la edición pertenece al artista (404 si no — sin fuga cross-artista).
  `lengthMs` es `int?`: **null si MusicBrainz no cronometró la pista** → la UI pinta "—", nunca un 0:00 inventado.
- **UI**: dentro de la fila de la discografía (`ArtistPage`), al expandir un release se carga la
  tracklist **de forma perezosa** (`useReleaseTracks(..., enabled: open)`), así una discografía de 40
  discos no dispara 40 peticiones al montar. Debajo, los créditos B9 ya existentes.
- **Verificado en vivo**: Darkthrone / *Soulside Journey* → 22 pistas, `Cromlech 4:11`, `Sunrise Over
  Locus Mortis 3:30`. Edición de otro artista sobre esa release → 404.

## C7 — Duración como eje (funeral doom ↔ grindcore)

- **Endpoint**: `GET /api/catalogue/duration-axis?pole=long|short&limit=` → `ArtistDurationDto`
  con la **media de la duración por banda** sobre sus `recordings.length_ms`, **excluyendo null del
  promedio** (SQL `avg()` ya ignora NULL; se exige `MinTimedTracks = 20` para que un solo tema de 6h
  no defina un catálogo). Solo `Group`. Ordena hacia el polo pedido.
- **UI**: sección en `ExplorePage` con toggle Más largos / Más cortos. Enmarcado como **curiosidad,
  no afirmación de género** (el hint lo dice).
- **Verificado en vivo**: polo largo → Green Buffalo Steak Ensemble (4:27:04 media, 166 temas); polo
  corto → Anal Trump (0:06 media, 150 temas). El toggle cambia el orden.

## C21 — Minería de títulos

- **Endpoint**: `GET /api/artists/{id}/themes` → `ArtistThemesDto { titleCount, themes[] }`.
- **Léxico** (`TitleLexicon`, puro y testeado): **vocabulario cerrado bilingüe es/en** (12 temas:
  death, blood, war, winter, forest, fire, night, darkness, ritual, cosmos, religion, sea). Normaliza
  (minúsculas + sin diacríticos, `frío`→`frio`) y hace match por **palabra entera** (conservador:
  "Deathcrush" **no** cuenta como death, "Death" sí). Cuenta a **nivel de título** (un título aporta
  ≤1 por tema).
- **UI**: badges en la ficha con el contador, con un hint que lo declara **aproximación por títulos,
  no dato curado** (D17/C21). Estado vacío si ningún título casa.
- **Verificado en vivo**: Darkthrone → Death 32, Fire 28, Darkness 27, Winter 23, Religion 13 (sobre
  784 títulos).

## C10 — Grafo de versiones ("quién versionó a quién")

- **Endpoint**: `GET /api/artists/{id}/versions` → `VersionGraphDto { graph, versions[] }`. Une
  `cover_versions` → `recordings` (ambos extremos) → `releases` → `artists`, **filtrando cross-artist**
  (`original.artist ≠ cover.artist`, regla aislada y testeada en `CoverGraphBuilder.CrossArtist`) para
  quedarse con las versiones **de verdad** — se descartan los remixes/remasters propios del artista.
- **UI**: reusa el **motor de grafo D18** (`GraphCanvas`) dentro de `GraphErrorBoundary`; los nodos son
  artistas (la banda vista marcada `ego`), la **arista lleva la relación** (`GraphCanvas` extendido con
  un prop `showEdgeLabels` **opt-in, por defecto off** para no ensuciar Bloodline/splits). Lista
  compañera con `original → cover [relación] canción`, cada artista clicable. Estado vacío diseñado
  para la mayoría del underground sin versiones.
- **Verificado en vivo**: The Beatles → 19 filas de versiones cross-artist + grafo; Satyricon → Enslaved
  (`edit`, *Hal Valr*) es una versión metalera cross-artist real; Darkthrone → estado vacío.

## C26 — Deriva cromática (portadas en el tiempo)

- **Fix de taint**: el proxy de portadas (`CoversController`) ahora añade `Access-Control-Allow-Origin: *`
  en la respuesta (found y 404) para que un `<img crossOrigin="anonymous">` pueda leerse en canvas sin
  contaminarlo. Arte público → ACAO abierto es seguro.
- **UI**: `ChromaticDrift` dibuja cada portada de álbum en un canvas 12×12 offscreen y promedia sus
  píxeles opacos (**la media es una función pura de `core/`**, `averageColor`, testeada; el pegamento
  de canvas vive en `ui/`, invariante 6). Tira de swatches ordenada por fecha; las portadas que el
  archivo no tiene quedan como **hueco rayado**, no un bloque negro. Estado vacío/`tainted` diseñado.
- **Verificado en vivo**: Black Sabbath → 6 swatches con colores reales y distintos
  (`rgb(101,84,80)`, `rgb(50,26,33)`, `rgb(56,52,72)`…), leídos en cliente sin error de taint. Los 404
  de portadas ausentes se degradan a huecos (son fallos de carga del navegador, no de nuestro código).

---

## Ficheros

**Backend** (`src/web/server`): `Services/DurationMath.cs`, `Services/TitleLexicon.cs`,
`Services/CoverGraphBuilder.cs`, `Dtos/RecordingDtos.cs`; endpoints añadidos a
`Controllers/ArtistsController.cs` (tracks, themes, versions) y `Controllers/CatalogueController.cs`
(duration-axis); ACAO en `Controllers/CoversController.cs`.

**Front** (`src/front/src`): `core/domain/recordings.ts` + `palette.ts` (+ sus `.test.ts`),
`core/hooks/useRecordings.ts`, métodos y tipos nuevos en `core/api/client.ts` y `core/domain/types.ts`
(edge kind `cover`); `ui/recordings/{Tracklist,ArtistThemes,Versions,DurationAxis,ChromaticDrift}.tsx`;
`ui/graph/GraphCanvas.tsx` (prop `showEdgeLabels` + estilo de arista `cover`, opt-in); cableado en
`ui/pages/ArtistPage.tsx` y `ui/pages/ExplorePage.tsx`; claves i18n en `locales/{en,es}.json`.

**Tests que muerden**: `DurationMathTests` (media excluyendo null; "—" para null),
`TitleLexiconTests` (palabra entera, diacríticos, conteo por título), `CoverGraphBuilderTests` (filtro
cross-artist), y en front `recordings.test.ts` (formato mm:ss/em-dash) + `palette.test.ts`
(promedio saltando transparentes). Totales verdes: **438 xUnit**, **108 Vitest**.

## Notas / lo que quedó fuera

- **C7 outliers**: los polos están dominados por actos experimentales/grind reales (temas de 6h vs de
  6s). Se enmarca honestamente como "duración media de tema", no como género; el umbral de 20 temas
  cronometrados quita los flukes de una sola pista larga. No se recortó ni se usó mediana — sería un
  número inventado sobre datos reales.
- **C10 relación**: `GraphCanvas` no dibujaba etiquetas de arista; se añadió el prop **opt-in**
  `showEdgeLabels` (default off) en vez de tocar el render de los grafos existentes, así los specs
  Playwright de Bloodline/splits siguen igual.
- **Playwright**: la suite apunta a `:5173`/`:5080`, ocupados aquí por CromoWin y un server viejo que
  **no se debía matar**. Se verificó estáticamente que los selectores de los specs existentes (headings
  scoped; el único `getByRole('group')` está scopeado a splits en Explore) **no** chocan con las
  secciones nuevas, y se hizo una verificación de render real en un front+API propios en puertos libres
  (5178/5099, liberados al terminar). La corrida completa de la suite debería hacerla el coordinador
  contra `:5173`/`:5080` limpios.
