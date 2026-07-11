# Movimiento VII — Ficha de compositor (agente VII-Front Clásica)

> Estado: **terminado y verde**. Frontera respetada: **solo `src/web/server/**` y `src/front/**` (+ tests)**. No se tocó `src/shared/**`, `src/console/**` ni migraciones. `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones, 0 skips). Fecha: 2026-07-11.

La clásica es otro modelo (D11): la ficha de compositor **no** es la de banda. Sin Gantt, sin miembros, sin rank (los `listeners` de clásica mienten). El héroe es la **lista de obras agrupada** + **dos linajes** (maestro-discípulo e influencia). Los datos ya estaban sembrados por el agente VII-datos (`classical-data.md`): 23 compositores, 2291 works, 6 relaciones teacher/student, influencia P737.

---

## 1. La decisión banda-vs-compositor

Un compositor es un `Artist` con `kind=Person` cuyas `works` lo referencian. Como es un `Person`, la señal de obras **debe** consultarse antes que el `kind`, o todo compositor caería a la página de miembro (B10).

- **Backend**: `ArtistDetailDto` gana un campo aditivo `HasWorks` (bool), calculado en `ArtistDetailBuilder` con un `AnyAsync(w => w.ComposerId == id)` barato contra el índice `ix_works_composer_id`. Único constructor del DTO → cambio centralizado; reveal del Rito y del regalo lo heredan sin tocarse.
- **Front**: función pura `resolveArtistView({ hasWorks, kind })` en `core/domain/artistView.ts`:
  - `hasWorks` → `composer` (gana sobre todo)
  - si no, `Person` → `member` (B10)
  - si no → `band` (el Gantt)
- `ArtistPage` conmuta: `composer` → `<ComposerBody>`; el resto → `<ArtistBody>` (que ya distingue Group/Person). **La ficha de banda no se rompió**: solo se añadió la rama.

---

## 2. Endpoint de compositor

`GET /api/composers/{id:guid}` (`ComposerController` → `ComposerDetailBuilder`, scoped DI). 404 si el id es desconocido. Devuelve `ComposerDetailDto`:

- `WorkCount` — total de obras del compositor.
- `WorkGroups` — obras agrupadas por `kind` (`WorkGroupDto{ Kind, Works }`). `kind` null → grupo "sin clasificar" con `Kind = null`, **mostrado, nunca oculto**.
- `Lineage` (`ComposerLineageDto`):
  - `Teachers` — "estudió con": aristas `Student` del ego → `to` (los maestros).
  - `Students` — "enseñó a": aristas `Teacher` del ego → `to` (los discípulos).
  - `Influences` — "influido por": aristas `InfluencedBy` del ego → `to` (P737).
  - `Graph` (`GraphDto`, reutiliza el shape de linaje D18) — ego-grafo 1 salto sobre teacher/student + influencia, con las aristas **entre** los vecinos (para que se vea la cadena Fauré→Boulanger→Glass desde Boulanger).

La identidad (nombre, país, tags, bio) **no se duplica**: la sirve la `ArtistDetail` que la página ya tiene; el endpoint añade solo lo específico de compositor.

### Lógica pura, testeada (muerde)

- **`WorkGrouping.Group`** (`src/web/server/Services/WorkGrouping.cs`) — agrupa por kind case-insensitive; `null`/blanco → grupo "sin clasificar" (`Kind=null`); named kinds alfabéticos primero, el sin-clasificar **siempre último**; obras por título dentro del grupo. `WorkGroupingTests` (6).
- **`ComposerLineage.BuildGraph`** (`src/web/server/Services/ComposerLineage.cs`) — colapsa el par espejo `Teacher`(maestro→discípulo) + `Student`(discípulo→maestro) que MB materializa por duplicado en **una** arista dirigida `teacher` maestro→discípulo; `InfluencedBy` como arista `influence`; marca el ego; nodo solo si toca una arista dibujada (sin linaje → grafo vacío). `ComposerLineageTests` (5).

**Comprobado que muerden**: invertir la dirección del caso `Student` (`(ToId,FromId)`→`(FromId,ToId)`) rompe `BuildGraph_CollapsesTeacherAndStudentMirrorsToOneDirectedEdge`; revertido, verde. En el front, poner el `kind` antes que `hasWorks` en `resolveArtistView` rompe el test del compositor; revertido, verde.

---

## 3. Componentes y vistas (`src/front/**`)

### core/ (portable, sin DOM — invariante 6)
- **`core/domain/types.ts`** — aditivos: `hasWorks` en `ArtistDetail`; tipos `Work`, `WorkGroup`, `ComposerLink`, `ComposerLineage`, `ComposerDetail`; `'Student'` en `EdgeKind`; `'teacher'` en `GraphEdge.kind`.
- **`core/domain/artistView.ts`** (NUEVO) + `artistView.test.ts` (4) — la decisión pura.
- **`core/api/client.ts`** — método `getComposer(id)`.
- **`core/hooks/useComposer.ts`** (NUEVO) — hook TanStack Query, `enabled` solo cuando la página ya identificó al compositor.

### ui/ (solo web)
- **`ui/composer/ComposerBody.tsx`** (NUEVO) — la ficha de compositor: nombre en el **corte base nítido** de Redaction (los compositores no tienen rank → `RankedName` con rank null cae al corte nítido, nunca corrosión inventada, D38); identidad (origen, tags, bio); **héroe = obras agrupadas por kind** con el grupo "sin clasificar" mostrado; **linaje** = tres columnas clicables (estudió con / enseñó a / influido por) + `GraphCanvas` reutilizado.
- **`ui/graph/GraphCanvas.tsx`** — pinta la arista `teacher` (sólida azufre, más gruesa: relación pedagógica real) frente a `influence` (discontinua). Sin degradación por rank (Q1).
- **`ui/pages/ArtistPage.tsx`** — la rama compositor.
- **`locales/en.json` + `es.json`** — sección `composer.*` en **ambos**.

### Estados vacíos diseñados (honestos)
- Compositor sin obras → `composer.noWorks`.
- Compositor sin linaje (teacher/student/influencia todo vacío y grafo vacío) → `composer.noLineage`. El linaje escaso es real: solo 12 aristas teacher/student en todo el corpus.
- Obra sin kind → grupo "sin clasificar", no se oculta.

---

## 4. Verificación (comando → salida real, API :5080 + front :5173 vivos, Postgres :5433)

```
Beethoven artist   → kind Person · hasWorks True · rank None   (→ vista compositor)
Beethoven composer → workCount 100 · groups [(Sonata,2),(Song,4),(null,94)]
                     studiedWith [Joseph Haydn] · teacher edge Haydn→Beethoven
Boulanger composer → studiedWith [Gabriel Fauré] · taught [Philip Glass]
                     grafo: 3 nodos {Fauré,Boulanger,Glass}, aristas Fauré→Boulanger y Boulanger→Glass
                     (cadena Fauré→Boulanger→Glass legible, sin duplicados)
Megadeth artist    → kind Group · hasWorks False                (→ vista banda, el Gantt)
composer/{id} desconocido → HTTP 404
```

**Reactividad contra Postgres**: se cambió el `kind` de una obra de Beethoven `Song`→`Symphony` y recargó → apareció el grupo `Symphony(1)` y `Song` bajó a 3; revertido → `Song(4)` y `Symphony` desaparece. La vista lee la base viva.

**Smoke del dev server**: `dev-root: 200`; `ComposerBody.tsx`, `useComposer.ts`, `artistView.ts` transforman a 200 en Vite.

**Gate**: `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones). Gates: `dotnet-build`, `dotnet-test` (incluye 11 tests nuevos: 6 WorkGrouping + 5 ComposerLineage), `pnpm-lint` (0 errores; 4 warnings preexistentes de fast-refresh), `pnpm-build`. Vitest: 86 pasan (incl. 4 de `artistView`). Puertos liberados por pid al terminar.

---

## 5. Huecos declarados (y su porqué)

- **`GET /api/composers/{id}` sobre un id sin obras** (p.ej. una banda) devuelve `200` con `workCount 0`, cero grupos y grafo vacío, no `404`. El front **nunca** lo llama para una banda (`useComposer` está gated por `hasWorks`), así que es inalcanzable por la UI; se dejó como respuesta honesta y vacía en vez de un caso especial.
- **Sin página propia de `work`**: las obras se listan por título, no son clicables. MB da `mbid` de work pero no hay ficha de obra en el alcance de este movimiento (el héroe es la lista, no la obra individual).
- **Grafo de linaje a 1 salto**: desde un nodo hoja (p.ej. Glass) solo se ve su vecino inmediato (Boulanger), no el abuelo (Fauré). Es honesto: la cadena completa se ve desde el nodo central. Subir a 2 saltos es cambiar un `Where` en `ComposerDetailBuilder`.
- **Vecinos de influencia en el grafo**: el ego-grafo de influencia trae también bandas/artistas que declararon al compositor como influencia (p.ej. The Beatles→Beethoven vía P737). Es dato real de Wikidata, no ruido inventado; se pinta tal cual.
- **Sin rank, sin Gantt, sin miembros** para compositores — correcto por D11, no es un hueco.
- **Sin captura E2E de navegador headless** (igual que los pases previos: no hay herramienta de navegador en este entorno). Se verificó (a) el grafo de módulos vía `pnpm build` + transform del dev server, (b) la lógica pura vía tests que muerden, y (c) toda la ruta de datos ejecutando las llamadas HTTP reales contra el motor vivo, incluida la reactividad contra Postgres.
