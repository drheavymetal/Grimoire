# Movimiento III — Features pendientes de ficha (agente Completar)

> Estado: **terminado y verde**. Cuatro features cerradas contra la base viva: **B9** créditos por
> disco, **C12** In Memoriam, **C15** instrumentos raros, **B12** el disco donde cambió todo.
> Frontera respetada al 100 %: solo `src/web/server/**` y `src/front/**` (+ tests). **Cero
> migraciones**, **cero cambios en `src/shared/**` o `src/console/**`** (los consume tal cual).
> `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones, 0 skips). Sin commit.
> Fecha: 2026-07-11.

Se consume el esquema existente (`credits` ~21k, 24 `artists.death_date`, 2342 aristas `member_of`,
releases con type/date). Reutiliza `LineupIntervalResolver` (Gantt) para B12 y el patrón de
controladores/DTOs/hooks/páginas ya establecido en olas anteriores.

---

## 1. Qué existe (nuevo en este pase)

### Backend — `src/web/server/**` (lógica pura testeada + endpoints)

- **`Services/CreditGrouping.cs`** (puro) — agrupa las filas planas de `credits` por release:
  intérpretes (miembro oficial vs invitado, con sus instrumentos) y producción
  (producer/engineer/mix/master). Regla D9: **invitado solo si TODAS sus filas de intérprete en
  ese release son `is_guest`** — un crédito oficial lo hace miembro. Miembros antes que invitados.
- **`Services/InstrumentClassifier.cs`** (puro) — `IsRare(instrument)`: clasifica por el **kit
  estándar de rock** (guitar/bass/drum/vocal/keys por substring) más un set de percusión/estudio
  común; **todo lo demás con nombre real es raro**. Elegido clasificar por lo estándar y no por
  una allowlist de raros (una allowlist soltaría en silencio lo que nadie listó, y el underground
  es justo donde aparece lo raro). Trampa cubierta: `whistling` (voz) fuera, `tin whistle` (folk)
  dentro.
- **`Services/LineupTurnover.cs`** (puro) — B12. Reusa `LineupIntervalResolver.MembersActiveOn`:
  formación activa una ventana (±365 d) **antes** y **después** de cada release; `joined` = después
  y no antes, `left` = antes y no después; `score = joined+left`. `MostPivotal` toma el máximo,
  desempata por el release más temprano, y **devuelve null si nada cambió nunca** (sin drama
  inventado).
- **`Controllers/ArtistsController.cs`** (ampliado) — `GET /api/artists/{id}/credits` (B9) y
  `GET /api/artists/{id}/pivotal-release` (B12, **204** cuando no hay cambio).
- **`Controllers/MemoriamController.cs`** — `GET /api/memoriam` (C12): fallecidos por
  `death_date` asc, con lugar (P20) y sus bandas (aristas `member_of`).
- **`Controllers/InstrumentsController.cs`** — `GET /api/instruments/rare` (C15): instrumentos
  raros por nº de intérpretes desc, con quién los toca y en qué banda.
- **DTOs**: `CreditsDtos.cs` (B9/B12), `MemoriamDtos.cs`, `InstrumentsDtos.cs`.

### Front — `src/front/**`

- **`core/domain/credits.ts`** (puro, sin DOM — invariante 6) — `splitPerformers` (miembros vs
  invitados) y `hasCredits`. + `credits.test.ts` (Vitest node).
- **`core/domain/types.ts`** — tipos aditivos: `ReleaseCredits`, `PerformerCredit`,
  `ProductionCredit`, `PivotalRelease`, `TurnoverMember`, `MemoriamEntry`, `MemoriamBand`,
  `RareInstrument`, `RareInstrumentPlayer`.
- **`core/api/client.ts`** — `artistCredits`, `pivotalRelease` (204→null), `memoriam`,
  `rareInstruments`.
- **`core/hooks/`** — `useArtistCredits` (+ `usePivotalRelease`), `useMemoriam`,
  `useRareInstruments`.
- **`ui/pages/ArtistPage.tsx`** — B9: cada release de la discografía es **expandible** y muestra
  Miembros / Invitados / Producción, o un estado vacío diseñado (`artist.noCredits`) para el
  release sin créditos (mucho underground). B12: callout «el disco donde cambió todo» + badge en
  el release pivote de la discografía. (Se extrajo `ArtistBody` para colgar los hooks sin romper
  las guardas loading/error.)
- **`ui/pages/MemoriamPage.tsx`** (NUEVO) + ruta `/memoriam` + enlace en `Layout`. Cronología de
  tono cuidado (año como ancla, fecha+lugar en mono, bandas enlazadas). Estado vacío diseñado.
- **`ui/pages/ExplorePage.tsx`** — nueva sección **Instrumentos raros** (C15): tarjeta por
  instrumento con sus intérpretes (enlace a persona y a banda). Estado vacío diseñado.
- **`locales/en.json` + `es.json`** — claves en **ambos**: `nav.memoriam`, `artist.noCredits`/
  `creditsMembers`/`creditsGuests`/`creditsProduction`, `creditRole.*`, `pivotal.*`, `memoriam.*`,
  `explore.rare*` (con plural `rarePlayers`).

### Tests (muerden)

- xUnit: `CreditGroupingTests` (agrupado + miembro-vs-invitado), `InstrumentClassifierTests`
  (detección de raro + trampa whistle/whistling), `LineupTurnoverTests` (rotación B12). +
  Vitest: `credits.test.ts` (partición miembro/invitado).
- **Bite comprobado**: `is_guest` `All`→`Any` rompe `CreditGroupingTests` (4→1 fallo); romper la
  diferencia simétrica de `LineupTurnover` rompe `LineupTurnoverTests` (4/4 fallan). Revertidos,
  verde.

---

## 2. Verificación (comando → salida real)

```
bash scripts/audit.sh --strict          → RESULT: PASS (Violations 0, Skipped 0)
dotnet build src/web/Grimoire.slnx -warnaserror → 0 Advertencias, 0 Errores
dotnet test  src/web/Grimoire.slnx      → Superado: 313, Con error: 0, Omitido: 0
pnpm test (front, Vitest node)          → 82 passed (10 files)
pnpm lint / pnpm build                  → 0 errores (4 warnings react-refresh preexistentes)
```

### En vivo contra la API :5080 y Postgres :5433

**B9 — Whitesnake** (`66a862b9…`): release `0265567f` = 7 miembros + 3 productores (David
Coverdale, Joel Hoekstra, Reb Beach); release `06015388` = 7 miembros + **1 invitado** (Vivian
Campbell, guitar, `is_guest=true`). Miembro vs invitado separados, producción aparte, instrumentos
reales.

**B12 — In Extremo** (`5d16c3a5…`): pivote = «In Extremo» (1997), score 2 — entró Boris Pfeiffer,
salió Sen Pusterbalg. Datos reales de las aristas con fecha.

**C12 — In Memoriam**: 24 fallecidos, cronológicos, con lugar y bandas reales — Phil Lynott (1986,
Salisbury, Thin Lizzy), Eric Carr (1991, KISS), Gar Samuelson (1999, Megadeth).

**C15 — Instrumentos raros**: 29 instrumentos. flute (9), bagpipe (7), fiddle (6), harmonica (6),
mandolin (5), tin whistle (5), accordion, bodhrán, cello, shawm, uilleann pipes, violin… con
intérpretes reales (Eluveitie, In Extremo, The Chieftains, Ulver, Agalloch). **Cero fuga del kit
estándar.**

**Anti-cascarón (reactividad contra Postgres)**:
```
GET /api/memoriam  Phil Lynott -> Salisbury
UPDATE artists SET death_place='Clondalfin (reactivity-probe)' WHERE name='Phil Lynott'
GET /api/memoriam  Phil Lynott -> Clondalfin (reactivity-probe)
UPDATE ... SET death_place='Salisbury'  → GET vuelve a Salisbury
```
La vista lee la base viva. Base **revertida**.

**Smoke dev server** (:5173): root 200; `MemoriamPage.tsx`, `ArtistPage.tsx`, `ExplorePage.tsx` y
`core/domain/credits.ts` transforman a 200 en el grafo de Vite. Puertos 5080/5173 liberados por
pid al terminar (no `pkill`).

---

## 3. Huecos declarados (y su porqué)

- **B9: algunos releases acreditan la banda como intérprete**, no a las personas (p. ej. In
  Extremo), porque el artist-credit de MB para esas grabaciones es la banda. Es lo que dice la
  fuente; no se descompone en miembros individuales (no se inventa el reparto). Se agrupa y muestra
  tal cual; los releases con créditos por persona (Whitesnake, Dio, Sodom…) los muestran bien.
- **Solo hay créditos para 610 releases** (de 5320) — la mayoría del underground no los tiene. El
  estado vacío `artist.noCredits` cubre el resto con dignidad (R2). Se poblarán al re-ejecutar el
  ETL de créditos (agente console).
- **B12 con ventana fija de ±365 d**: un miembro que entra y sale **dentro** de la misma ventana no
  cuenta (before/after ambos inactivos). Es el criterio estándar de «cambió alrededor de esta
  fecha»; anotado, no accidental.
- **C12 sin fecha de nacimiento**: los `Person` no traen `formed_year` (solo 2 en todo el corpus,
  ninguno fallecido), así que la ficha muestra solo fecha y lugar de muerte — honesto, no se
  inventa un rango de vida.
- **Sin captura E2E de navegador headless** (igual que pases previos: no hay herramienta de
  navegador). Se verificó (a) el grafo de módulos por `pnpm build` + dev server, (b) la lógica
  pura por tests que muerden, y (c) los cuatro endpoints en vivo contra Postgres, más la prueba de
  reactividad.
- **Firma tipográfica por rank (D38)**: reusada donde ya estaba (`RankedName` en la ficha). Los
  nombres de créditos/memoriam/instrumentos usan cuerpo `Archivo` (son personas y roles, no el
  titular de banda cuyo rank degrada); coherente con el resto de la app.

---

## 4. Notas para el coordinador

- **Frontera al 100 %**: cero cambios fuera de `src/web/server/**` y `src/front/**`. Ninguna
  migración; ningún modelo de `src/shared` tocado.
- **Aviso de carrera con el agente console/shared**: durante el pase, `bash scripts/audit.sh`
  falló una vez por un error de compilación **transitorio** en
  `src/console/server/Classical/ClassicalJob.cs` (`ComposerCandidate` a medio editar por el otro
  agente) — fuera de mi frontera. Se aclaró solo; el re-run quedó en PASS. No es mío.
