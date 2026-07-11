# Movimiento IV — Linaje (agente Linaje)

> Estado: **terminado y verde**. El motor de grafo compartido (`d3-force` headless + primitivas SVG, D18) y siete features de linaje construidas sobre datos reales: 2342 aristas `MemberOf` con fechas e instrumentos + 67 `InfluencedBy` (Wikidata P737) + 309 embeddings centrados. Frontera respetada: **solo `src/web/server/**` y `src/front/**` (+ tests)**. No se tocó `src/shared/**`, `src/console/**` ni migraciones. Fecha: 2026-07-11.

Complementa `rite-engine.md` / `rite-front.md` (mov. II), `gantt.md` (mov. III) y `data-backbone.md` (ola D, que pobló las aristas que este movimiento consume). Consume el esquema existente sin cambiarlo.

---

## 1. La técnica de grafo (D18 / SPEC §9)

**Layout con `d3-force` headless en `core/`** (`core/domain/graph.ts`) + **pintado con primitivas SVG en `ui/`** (`ui/graph/GraphCanvas.tsx`). NO `react-force-graph` (rompe invariante 6 y ata repintado a la simulación — D18). `d3-force` es JS puro sin DOM, así que vive en `core/` y pasa el check 4 del audit.

- `layoutGraph(graph)` corre la simulación de fuerzas **headless y determinista**: posiciones iniciales sembradas en círculo por índice + RNG mulberry32 fijo (`randomSource`), `simulation.stop()` y `tick(300)`. Mismo grafo → mismo layout, testeable sin navegador. No muta la entrada.
- `fitToViewport(bounds, viewport, padding)` — **auto-fit por bounding box transformando posiciones en JS** (nunca escalando un `<g>`). Escala uniforme + centrado; caja de tamaño cero (un nodo, o una línea recta) no divide por cero (fallback a escala 1).
- `transformPoint`, `computeBounds`, `shouldShowLabel({focused,matched,zoom})` con `LABEL_ZOOM_THRESHOLD = 1.6` (etiquetas solo en foco / coincidencia de búsqueda / `k≥1.6`, SPEC §9).
- `GraphCanvas.tsx`: pan (arrastre), zoom (rueda, clamp 0.4–4 alrededor del centro), búsqueda que resalta/atenúa, glifos a **tamaño de píxel constante** (contra-escala `1/k` implícita al colocar, no escalar). Bandas = círculo lleno, personas = círculo hueco menor; ego/source/target en azufre (único acento, DESIGN §5); arista `member` línea tenue, `influence` discontinua en azufre. Clic/Enter en nodo → ficha. Estado vacío diseñado (nodo aislado). **Sin degradación tipográfica por rank** (Q1, prohibido); corte base.

Reutilizado por B16 Bloodline y C17 grimorio-grafo.

---

## 2. Endpoints (`src/web/server/**`)

Controlador `LineageController` (`/api/lineage`), servicio scoped `LineageGraph` (carga las ~2.4k aristas una vez por request y deriva dos adyacencias: **artista** —personas+bandas, para Bloodline/Six Degrees— y **banda** —bandas que comparten miembro o influencia, para Rabbit Hole/grimorio-grafo). Algoritmos puros en `GraphAlgorithms` (BFS `ShortestPath`, `Neighbourhood`, `Walk`) y `LineageMath` (`WentAfterLeaving`, `Midpoint`), testeados sin base de datos.

| Método | Ruta | Feature | Respuesta |
|---|---|---|---|
| GET | `/{id}/bloodline?hops=` | **B16** | `GraphDto` — ego-grafo, miembros compartidos + influencia, N saltos (1–4) |
| GET | `/six-degrees?from=&to=` | **B19** | `PathDto {nodes, degrees}` — BFS banda→persona→banda; `nodes` vacío = sin camino |
| GET | `/{id}/diaspora` | **B11** | `DiasporaDto` — miembros con `end_date` y las bandas a las que fueron después |
| GET | `/{id}/bands` | **B3** | `MemberBandsDto` — bandas donde tocó la persona, con stint e instrumentos |
| GET | `/missing-link?from=&to=` | **C5** | `MissingLinkDto` — vecinos del punto medio `(A+B)/2`; 422 si falta embedding |
| GET | `/{id}/rabbit-hole?length=` | **C8** | `RabbitHoleDto` — paseo no repetido por la adyacencia de bandas |
| GET | `/grimoire-graph` **[Authorize]** | **C17** | `GraphDto` — bandas invocadas + aristas banda-a-banda entre ellas |

**C5 respeta el doble-centrado (D26/D31)**: `LineageMath.Midpoint` promedia dos embeddings **ya centrados** → el punto medio está centrado y es directamente comparable contra el índice HNSW; **no se resta el medio otra vez**. Query pgvector `Embedding.CosineDistance(mid)` (tres líneas), excluye A y B y los null.

**C17 es un grafo de BANDAS**, no de artistas: dos bandas invocadas se unen cuando comparten miembro (label = el miembro puente) o hay influencia. El músico puente **no** está en el grimorio, así que la arista se dibuja banda-a-banda (una arista cruda persona→banda nunca aparecería). Registrado como decisión de implementación abajo.

Todos públicos salvo `grimoire-graph`. Nada inventado: sin camino / sin diáspora / sin vecino → resultado vacío que el front pinta como estado vacío diseñado (R2). Registro en `LineageController`, `LineageGraph`, `GraphAlgorithms`, `LineageMath`, `LineageDtos`, y `Program.cs` (registro DI del servicio scoped).

---

## 3. Componentes y vistas (`src/front/**`)

### core/ (portable, sin DOM — invariante 6)
- **`core/domain/graph.ts`** (NUEVO) — el motor de grafo (`d3-force` + auto-fit + reglas de etiqueta). Dependencia nueva: `d3-force` + `@types/d3-force`.
- **`core/domain/types.ts`** — tipos aditivos: `GraphNode`, `GraphEdge`, `Graph`, `PathResult`, `Diaspora*`, `MemberBand*`, `MissingLink*`, `RabbitHole`.
- **`core/api/client.ts`** — 7 métodos nuevos (`bloodline`, `sixDegrees`, `diaspora`, `memberBands`, `missingLink`, `rabbitHole`, `grimoireGraph`).
- **`core/hooks/useLineage.ts`** (NUEVO) — un hook por endpoint (TanStack Query), disabled hasta que los ids estén presentes.

### ui/ (solo web)
- **`ui/graph/GraphCanvas.tsx`** (NUEVO) — el pintor SVG reutilizable.
- **`ui/lineage/`** (NUEVOS): `Bloodline.tsx` (B16, control de saltos + GraphCanvas), `Diaspora.tsx` (B11, lista), `MemberBands.tsx` (B3, lista), `RabbitHole.tsx` (C8, opt-in, "cae dentro"/"cae de nuevo"), `ArtistPicker.tsx` (buscar+elegir banda, reutilizado por B19/C5).
- **`ui/pages/LineagePage.tsx`** (NUEVO, ruta `/lineage`) — hub con Six Degrees (B19) y el eslabón perdido (C5), ambos con dos `ArtistPicker`.
- **`ui/pages/ArtistPage.tsx`** — Bloodline en toda ficha; Diáspora + Rabbit Hole en bandas; "Bandas" (B3) en personas.
- **`ui/pages/GrimoirePage.tsx`** — grafo del grimorio (C17) bajo la lista, solo con ≥2 bandas.
- **`ui/routes.tsx`** + **`ui/Layout.tsx`** — ruta y nav `/lineage`.
- **`locales/en.json` + `es.json`** — secciones `graph.*` y `lineage.*` + `nav.lineage` en **ambos** catálogos.

### Tests que muerden
- **Backend** (`GraphAlgorithmsTests` 10 + `LineageMathTests` 8): BFS del camino más corto (elige el de menos aristas, vacío si desconectado, nodo-a-sí-mismo), frontera de saltos de `Neighbourhood`, no-repetición y parada en dead-end de `Walk`, `WentAfterLeaving` (inclusivo mismo día, false si falta fecha), `Midpoint` (promedio, no muta, rechaza dims dispares/vacías). **Muerden**: mutar `depth == hops` → `hops+1` rompió 2 tests de neighbourhood.
- **Front** (`core/domain/graph.test.ts`, 14): auto-fit (escala llena el viewport, doblar viewport dobla escala, cada esquina cae dentro, un nodo no divide por cero), `shouldShowLabel`, `computeBounds`, `layoutGraph` (posiciones finitas, determinista, separa nodos, no muta, grafo vacío). **Muerden**: mutar `Math.min`→`Math.max` en `fitToViewport` rompió 1 test.

---

## 4. Verificación (comando → salida real, API :5080, Postgres :5433 vivos)

```
B16  bloodline Darkthrone hops=2   → 6 nodos, 5 aristas; ego=Darkthrone, miembros Fenriz/Nocturno Culto/…, labels de instrumento reales
B19  six-degrees Megadeth→Slayer   → degrees 1, camino [Megadeth, Kerry King, Slayer]
B3   bands of Kerry King           → Slayer (1981–, guitar), Megadeth (1983–1983, background vocals, guitar)
B11  diaspora Uriah Heep           → Bob Daisley→[Black Sabbath 1986, Dio 1998]; Chris Slade→[AC/DC 1989, Michael Schenker Group 2008]
     diaspora Megadeth             → vacío (Kerry King entró en Slayer 1981, ANTES de dejar Megadeth 1983 → correctamente excluido)
C5   missing-link Megadeth↔Slayer  → 0.5322 Metallica, 0.6193 Machine Head, 0.6429 Anthrax… (los thrash del medio; Metallica es lo más cercano al punto medio)
C8   rabbit-hole Megadeth len=8    → Megadeth→Savatage→Doro→Rainbow→Michael Schenker Group→Toto→Led Zeppelin→Aerosmith
C17  grimoire-graph {Megadeth,Slayer,Anthrax}  → arista member "Kerry King" entre Megadeth y Slayer (Anthrax sin arista, honesto)
```

**Anti-cascarón (reactividad contra Postgres)**: se cambió `MemberOf`→`SideProject` en la arista Kerry King→Megadeth y se re-consultó → Six Degrees re-enrutó a **6 grados** (Megadeth→Dave Mustaine→Metallica→…→Slayer); revertido → vuelve a **1 grado** por Kerry King. La vista lee la base viva.

**Gate**: `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones, 0 skips). Gates verdes: `dotnet-build`, `dotnet-test` (**209 pasan**, +26 nuevos), `pnpm-lint` (0 errores; 2 warnings preexistentes de fast-refresh), `pnpm-build`. `pnpm test` → **60 pasan (7 files)**, +14 nuevos. Smoke del dev server: `dev-root: 200`, y `graph.ts`/`GraphCanvas.tsx`/`LineagePage.tsx`/`useLineage.ts` transforman a 200 en Vite. Base dejada limpia (0 usuarios/rites/taste; corpus intacto 2478 / 2342 / 67).

---

## 5. Huecos declarados (y su porqué)

- **C9 splits y C10 versiones: NO en este pase** (declarados para la siguiente ola). Dependen de `credits` (C9: varios créditos de artista sobre un mismo `split`) y de `works` + relaciones de cover (C10), **ambas tablas vacías** — las puebla el agente de ETL de créditos sobre el esquema de la ola D, sin migración. Sin ese dato, un grafo de splits/versiones sería un cascarón. El motor de grafo compartido ya está listo para pintarlos en cuanto exista el dato.
- **Bloodline de bandas underground sale pequeño** (Darkthrone: 6 nodos a 2 saltos) porque los otros proyectos de sus miembros **no están sembrados** en el corpus de ~2.5k. No es un bug: es la cobertura real. La expansión por grafo (D23) traería más si el corpus creciera.
- **In Memoriam (C12) no es de este movimiento** (mov. V/VI); `death_date` está poblado en 0 filas (ola D lo dejó construido pero sin dato — casi ningún `Person` tiene QID de Wikidata). No afecta al linaje.
- **Aristas no-`MemberOf`/`InfluencedBy`** (SideProject, Collaboration, Teacher) **no existen** en la base todavía; el grafo solo pinta las dos que hay. El enum está abierto; entran sin cambio de código cuando el ETL las cree.
- **Sin captura E2E de navegador headless** (igual que los pases previos: no hay herramienta de navegador en este entorno). Se verificó (a) el grafo de módulos vía `pnpm build` + dev server, (b) la lógica pura vía tests que muerden, y (c) **toda la ruta de datos** ejecutando las llamadas HTTP reales contra el motor vivo, incluida la reactividad contra Postgres. El pintado SVG (posiciones, zoom, etiquetas) se ejercita indirectamente vía el layout puro que lo alimenta.
- **Rabbit Hole y Six Degrees no distinguen visualmente arista member vs influence en el camino textual**; el GraphCanvas sí (línea vs discontinua). Suficiente para este pase.

---

## 6. Decisiones de implementación (para ratificar como `D<n>` si Pedro quiere)

1. **El grafo de linaje tiene dos adyacencias.** *Artista* (personas+bandas, aristas crudas `MemberOf`/`InfluencedBy`) para Bloodline (muestra a los miembros como nodos) y Six Degrees (camino banda→persona→banda, con los músicos puente visibles). *Banda* (dos bandas adyacentes si comparten miembro o hay influencia) para Rabbit Hole y el grimorio-grafo. Ambas se derivan de la misma carga de aristas.
2. **C17 grimorio-grafo es un grafo de bandas.** Las aristas se dibujan banda-a-banda con el miembro puente como label, porque el músico que conecta dos bandas invocadas **no** está él mismo invocado — una arista cruda persona→banda nunca aparecería en el subgrafo. "Las aristas entre ellas" (SPEC C17) = conexiones banda-banda, no las aristas literales del grafo de artistas.
3. **`d3-force` entra como dependencia de `core/`.** Es JS puro sin DOM (invariante 6 intacto, check 4 verde). El pintado sigue en `ui/` con primitivas SVG que `react-native-svg` acepta; el port RN cambia el import del pintor, no el layout.
