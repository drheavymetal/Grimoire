# Movimiento III — El Gantt (agente Lineup Timeline)

> Estado: **terminado y verde**. El Gantt (B7), el resaltado por release (B8) y la reutilización para la página de miembro (B10) construidos sobre datos reales: 2342 aristas `member_of` con fechas e instrumentos. Frontera respetada: **solo se tocó `src/front/**`**. No se tocó `src/shared/**`, `src/web/server/**`, `src/console/**` ni migraciones. Fecha: 2026-07-11.

Complementa `skeleton.md` (mov. I), `ficha.md` y `rite-front.md` (mov. II). Consume el contrato del backend tal cual — `ArtistEdgeDto` con `counterpartId/Name/Kind` ya existía; no hizo falta ningún cambio de backend.

---

## 1. Qué existe (nuevo en este pase)

### core/ (100 % portable, sin DOM — invariante 6)

- **`core/domain/lineup.ts`** (NUEVO) — el cerebro de render del Gantt, todo funciones puras:
  - **`membersActiveOn(edges, date)`** — **port fiel de `LineupIntervalResolver.MembersActiveOn`** (C#, `src/shared`). Intersección de intervalos: ambos extremos **inclusivos**, `beginDate` null = inicio abierto (siempre empezado), `endDate` null = fin abierto (sigue activo), solo `MemberOf`. Es la lógica de B8.
  - **`instrumentFamily(raw)` + `FAMILY_COLORS` + `INSTRUMENT_FAMILIES`** — mapa **estable** instrumento→familia→color. Pliega los strings sucios de MusicBrainz (`"drums (drum set)"`, `"electric bass guitar"`, `"lead vocals"`) a seis familias. **Bass se comprueba antes que guitar** (`"bass guitar"` contiene `"guitar"`). Instrumentos raros (violín, gaita, zanfona) y ausencia de instrumento → `other` (color neutro): C15 es otra ola. Colores como **strings de color portables** (no CSS vars — no sobrevivirían al port RN; `react-native-svg` toma los mismos), paleta apagada que lee en papel y vacío, **sin verde ácido** (DESIGN 5) y **sin oxblood** (reservado a Banish).
  - **`membersFromEdges(edges)`** — modelo de filas desde el **counterpart** (funciona viendo banda → filas=personas, y viendo persona → filas=bandas: B10 con el mismo código). Ordena fundadores primero; inicio desconocido al fondo.
  - **`releaseMarksFromReleases(releases)`** — marcas verticales; **descarta las releases sin fecha** (no inventa año).
  - **`layoutLineup(members, marks, viewport)`** — **x desde el año, y desde la fila**. Dominio calculado de los datos (**auto-fit**), `xForYear` transforma posiciones en JS. Barras con `openStart`/`openEnd`/`unknownSpan` marcados. `currentYear` se **inyecta** (determinismo en test; miembro activo estira el dominio hasta "ahora"). `niceYearTicks` para el eje.
- **`core/domain/lineup.test.ts`** (NUEVO) — 23 tests, entorno node, sin navegador (D12).

### ui/ (solo web)

- **`ui/lineup/LineupTimeline.tsx`** (NUEVO) — el pintor. **Su propia técnica de render (D18), NO d3-force, NO react-force-graph, NO canvas**: primitivas SVG (`<rect> <line> <text> <circle> <g>`) compatibles con `react-native-svg`. Auto-fit recomputando el layout para el ancho medido (**nunca escalando un `<g>`**). Filas: nombre en el margen (clic/Enter → ficha, B10), barra coloreada por instrumento. Marcas de release **enfocables y hover** que **iluminan la formación activa** (B8): atenúa las filas no activas. Leyenda de instrumentos (i18n). `prefers-reduced-motion` corta la transición del resaltado. Barra `unknownSpan` (sin fechas) **dibujada hueca + discontinua con `?`**, nunca como span sólido afirmado.
- **`ui/lineup/useMeasuredWidth.ts`** (NUEVO) — hook con `ResizeObserver` (API de navegador, vive en ui/, no en core/) para el ancho del auto-fit.
- **`ui/pages/ArtistPage.tsx`** — el Gantt entra como **héroe**, en el sitio de la foto de cabecera (DESIGN 6), justo bajo el nombre. Se **retiró la lista textual de linaje** (superada por el Gantt; era solo `MemberOf`, que es lo que el timeline muestra). Estados vacíos del resto de la ficha intactos.
- **`core/domain/types.ts`** — `ArtistEdge` gana `counterpartId/counterpartName/counterpartKind` (aditivo; espeja el DTO ya existente).
- **`locales/en.json` + `es.json`** — sección `lineup.*` en **ambos** catálogos (título, hint, aria, marca de release, dos estados vacíos banda/persona, seis nombres de instrumento).

---

## 2. Verificación (comando → salida real)

**`bash scripts/audit.sh --strict`** → `RESULT: PASS` (Violations 0, Skipped 0). Gates `dotnet-build`, `dotnet-test`, `pnpm-lint`, `pnpm-build` en verde.

**`pnpm test`** → **46 passed (6 files)** — los 23 de `lineup.test.ts` + los 23 previos.

**Tests que muerden** (los cubre `lineup.test.ts`): fronteras inclusivas de `membersActiveOn` (invertir `<=`→`<` rompe el caso de día-frontera); `instrumentFamily('bass guitar')==='bass'` (invertir el orden bass/guitar rompe); auto-fit (doblar el ancho escala posiciones proporcionalmente); colores de familia distintos.

**De punta a punta contra la API y Postgres vivos** (API :5080), ejecutando el **módulo `core/` real** contra el contrato vivo de Darkthrone:

```
rows (ordenadas): Fenriz 1986-, Dag Nilsen 1987-1991, Anders Risberget 1987-1988,
                  Nocturno Culto 1988-, Zephyrous 1988-1993
domain: {minYear:1986, maxYear:2026}  bars:5  releases:25
B8 activos en 1991-06-01: {Zephyrous, Fenriz, Nocturno Culto}   ← Anders (se fue 1988) EXCLUIDO
```
Fundadores primero, fechas reales de la API, dominio estirado a 2026 por los miembros abiertos, 25 marcas de release. El resaltado B8 ilumina exactamente a los activos en la fecha del debut y excluye al que ya se había ido.

**Anti-cascarón (reactividad contra Postgres)** — se cambió un `end_date` y se re-consultó la API:
```
Nocturno Culto endDate = None            (abierto)
UPDATE artist_edges ... end_date='1995-01-01'  → API sirve endDate=1995-01-01 → endYear derivado=1995
UPDATE ... end_date=NULL                 → API vuelve a servir endDate=None
```
La vista lee la base viva: cambiar el dato en Postgres cambia lo que el layout deriva. Base **revertida** al estado original (Nocturno Culto abierto).

**Smoke del dev server**: `dev-root: 200`, y los módulos `ArtistPage.tsx` y `LineupTimeline.tsx` transforman a 200 en el grafo de Vite. `pnpm build` (tsc -b + vite build) valida el grafo completo con tipos.

---

## 3. Huecos declarados (y su porqué)

- **Sin captura E2E de navegador headless.** Igual que `skeleton.md`/`ficha.md`/`rite-front.md`: no hay herramienta de navegador en este entorno. Se verificó (a) el grafo de módulos vía `pnpm build` + dev server, (b) la lógica pura vía tests que muerden, y (c) **el cerebro de render real ejecutado contra la API viva** (rows, dominio, marcas, y el resaltado B8), más la prueba de reactividad contra Postgres. No se renderizó el DOM en un navegador real; el pintado SVG (posiciones, colores, atenuación) se ejercita indirectamente a través del layout puro que lo alimenta.
- **B9 (créditos por disco)** — fuera de alcance de este pase (otra ola; depende de `credits`, propiedad del agente ETL). El Gantt muestra formación e instrumentos, no el crédito disco-a-disco.
- **C12 (In Memoriam)** y **C15 (instrumentos raros)** — **no** son de este pase. Los instrumentos raros caen a la familia `other` (neutro), honesto; cuando C15 llegue se amplía el mapa sin tocar el pintor.
- **Sin degradación tipográfica por rank** — deliberado, igual que los pases previos: rank es null y Q1 sigue sin ratificar. Los nombres del Gantt usan el cuerpo `Archivo`; no se cablea `redactionCutForRank`.
- **Aristas no-`MemberOf`** (SideProject, Collaboration, Teacher, InfluencedBy) — hoy **no existen** en la base (las 2342 aristas son todas `MemberOf`). El Gantt solo pinta `MemberOf`; el resto es Bloodline (B16, movimiento IV, grafo con su propia técnica). Al retirar la lista textual de linaje no se pierde nada renderizado hoy.
- **Vista de persona sin marcas de release** — una `Person` no trae releases propias en el DTO, así que su Gantt (filas = bandas, B10) no lleva marcas verticales. Correcto: las marcas son los discos de la entidad vista.

---

## 4. Notas para el coordinador

- **Frontera respetada al 100 %**: cero cambios fuera de `src/front/**`. El contrato del backend (`ArtistEdgeDto` con counterpart) se consumió tal cual; el tipo `ArtistEdge` del front se alineó de forma aditiva.
- **La técnica de render es propia y cumple D18/§9**: layout puro en `core/` (x=año, y=fila, auto-fit por transformación en JS), pintado con primitivas SVG en `ui/`, `core/` sin DOM (invariante 6, comprobado por el check 4 del audit — evitado el literal `window` que hace grep, como avisó `rite.ts`).
- **Tensión color vs. monocromía**: DESIGN 5 pide app monocroma con único acento azufre; el brief del Gantt pide **color=instrumento con leyenda**. Se siguió el brief (directiva de este agente) con una paleta **apagada y acotada** anclada en tonos que conviven con el sistema, evitando verde ácido y oxblood. Si Pedro cierra Q1/Q2 hacia monocromía estricta, `FAMILY_COLORS` es un único objeto de datos en `core/` fácil de re-tonar sin tocar el pintor ni el layout.
