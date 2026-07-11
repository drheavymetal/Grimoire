# Movimiento II — Import de grabaciones/tracks/versiones (agente recordings)

> Estado: **completado y verificado contra la base viva.** `bash scripts/audit.sh --strict` → PASS.
> Desbloquea **C7** (duración como eje), **C21** (minería de títulos) y **C10** (grafo de versiones).
> Frontera del agente: `src/console/server/**` (n/a este pase), `src/shared/**`, migraciones EF,
> `scripts/**`, e infra local (un contenedor Postgres MB temporal propio). **No se tocó**
> `src/web/server/**` ni `src/front/**` — las UIs de C7/C21/C10 las construye otro agente sobre
> estos datos (contrato al final de este documento). Mismo patrón que `mb-dump-import.md`: el
> trabajo masivo es **SQL de conjuntos** (D5, el mirror MB es artefacto de build), no un verbo de
> consola; una migración EF única (soy el dueño de migraciones este pase) crea las dos tablas.

---

## 0. Insumos y arquitectura

- Dump: `/var/tmp/grimoire-mb/mbdump.tar.bz2` (core). El import anterior **no** extrajo
  `recording`/`track`/`medium` (las gigantes); este pase las extrae. `release_group`, `release`,
  `link`, `link_type`, `l_recording_recording`, `medium` ya estaban del pase anterior.
- **Contenedor MB temporal propio**: `grimoire-mb-recordings`, `postgres:16-alpine`, puerto
  **5435** (distinto del 5433 de Grimoire y del 5434 del import de artistas). Tuneado para carga
  masiva (`fsync=off`, `shared_buffers=2GB`, `--shm-size=1g`). Desechable (D5).
- Transferencia MB→Grimoire: se destila dentro del Postgres MB a tablas con forma Grimoire, se
  vuelca a TSV, se carga en un esquema `mb_import` de Grimoire y se hace el upsert por clave
  natural con SQL de conjuntos.

### Scripts (en el repo, reproducibles) — `scripts/mb-import/recordings/`

- `10-load-schema.sql` — DDL del Postgres MB temporal (orden de columnas exacto del esquema MB
  para que el `COPY` cuadre; columnas no usadas tipadas `TEXT`).
- `11-copy-and-index.sql` — `COPY` de los ficheros del dump + índices para la destilación.
- `12-distill.sql` — selección de la release representativa por grupo + tracklist + aristas de
  versión, en tablas staging con forma Grimoire.
- `13-upsert.sql` — upsert **aditivo** en la base viva (solo escribe `recordings` y
  `cover_versions`; no toca ninguna otra tabla ni columna de enriquecimiento).
- `run.sh` — orquestador end-to-end (idempotente).

**Nota de extracción (GNU tar):** nombrar varias tablas en un solo `tar xjf` **descarta las
últimas** (se perdió `track` la primera vez, corrupción por dos `tar` escribiendo el mismo
fichero a la vez). `run.sh` extrae **una tabla por pase** para evitarlo.

---

## 1. Tablas cargadas del dump (counts reales)

```
release_group   4 396 444      recording           39 443 078
release         5 623 968      track               56 754 399
medium          6 178 498      l_recording_recording  351 633
link            1 132 345      link_type                  697
```

Del listado del brief: `medium`, `track`, `recording`, `l_recording_recording`, `link`,
`link_type`. Añadidas (justificadas): `release_group` + `release`, necesarias para mapear
nuestro `releases.mbid` (que es un **release-group gid**) a sus releases y de ahí a los medios y
tracks — MB no enlaza `medium` al release-group, solo al `release`.

### Hechos MB verificados (usados en la destilación)

- **`recording.length` y `track.length` están en milisegundos** → van directos a `length_ms`.
- **Una release-group tiene varias releases** (prensajes/países). Se elige **UNA representativa
  por grupo**: la de tracklist más completa (máximo `SUM(medium.track_count)`), desempate por
  `release.id` menor. Determinista, y da la lista más completa sin inventar.
- **MB NO tiene una relación atómica "cover" grabación→grabación.** La atribución de versión
  propiamente dicha vive a nivel de **`work`** (el modelo clásico reservado, D11). Lo que MB sí
  expone a nivel de grabación es la familia **"covers and versions"** de `l_recording_recording`
  (`other versions`, `edit`, `remaster`, `a cappella`, `instrumental`, `karaoke`, `remix`).
  Esa familia es la señal honesta de v1 para C10; cada arista guarda su nombre de relación MB.
- **`track.is_data_track`** se excluye (pistas de datos, no audio).

---

## 2. Migración (única, mía): `AddRecordingsAndCoverVersions`

`src/shared/GrimoireLibrary/Migrations/20260711160347_AddRecordingsAndCoverVersions.cs`.
Aditiva y no destructiva. Crea dos tablas:

**`recordings`** (desbloquea C7 + C21):

| columna | tipo | nota |
|---|---|---|
| `id` | uuid PK | |
| `mbid` | uuid | recording gid; **NO único** (una grabación puede ser track en varias releases) — indexado |
| `release_id` | uuid FK→`releases` cascade | |
| `title` | text | nombre del track (o de la grabación si el track no lo trae) |
| `length_ms` | int **null** | duración en ms; **null si MB no la da** (C7 degrada con honestidad) |
| `position` | int | 1-based a lo largo de todos los medios de la release |

Clave natural / índice único **`(release_id, position)`** → idempotencia por track.

**`cover_versions`** (desbloquea C10):

| columna | tipo | nota |
|---|---|---|
| `id` | uuid PK | |
| `original_recording_id` | uuid FK→`recordings` cascade | MB `entity0` |
| `cover_recording_id` | uuid FK→`recordings` cascade | MB `entity1` |
| `relation` | text | nombre de la relación MB (`other versions`, `remix`, …) |

Único **`(original_recording_id, cover_recording_id)`**. Ambos extremos son grabaciones **de
nuestro set** (dos FK cascade a la misma tabla — Postgres lo permite), así que **ninguna arista
queda colgando**. Se eligió una **tabla de versiones dedicada** en vez de reusar `artist_edges`
porque una versión es a nivel de **grabación** (qué canción), no de artista: colapsarla a
artista→artista perdería la canción, que es el núcleo del "grafo de versiones". Los artistas de
cada extremo se derivan por `recordings.release_id → releases.artist_id`.

**Contrato de mapeo testeado** (`RecordingMapper`, `src/shared/GrimoireLibrary/Services/`): el
import masivo es SQL, pero las reglas (MBID parseable, título track > grabación, duración track >
grabación, duración no-positiva → null, posición ≥ 1, self-edge de cover rechazado) están
encapsuladas y testeadas en `RecordingMapperTests` (xUnit). Es la implementación de referencia
que el SQL replica.

---

## 3. Mapeo MB → Grimoire

| Grimoire | Origen MB | Nota |
|---|---|---|
| `recordings.mbid` | `recording.gid` | no único aquí |
| `recordings.release_id` | `release_group.gid` = nuestro `releases.mbid` → `releases.id` | vía release representativa |
| `recordings.title` | `track.name` (fallback `recording.name`) | nunca null |
| `recordings.length_ms` | `track.length` (fallback `recording.length`) | ms; null si ambos null |
| `recordings.position` | `row_number()` sobre `(medium.position, track.position, track.id)` | 1-based, único por release |
| `cover_versions.*` | `l_recording_recording` ∩ familia de versión, ambos extremos ∈ nuestras grabaciones | relation = `link_type.name` |

---

## 4. Counts (base viva)

```
recordings                8 925 364   (7 627 644 mbid distintos)
  con length_ms           8 135 734   (91.2 %)                         <- C7
  con título              8 925 364   (100 %, título nunca null/blank)  <- C21
releases con ≥1 track       668 237 / 668 885  (99.9 %)
grupos con release repr.    668 596   (de 668 884 de nuestros mbid que existen en MB)
cover_versions               21 418                                     <- C10
  cross-artista (covers "de verdad")   858
  por relación: remix 15 419 · edit 3 760 · karaoke 1 458 ·
                instrumental 587 · remaster 158 · a cappella 36
```

La familia de versiones está dominada por remix/edit del **mismo** artista (versiones propias);
el cover cross-artista (858) es el subconjunto "quién versionó a **otro**". La UI de C10 puede
filtrar por `original.artistMbid <> cover.artistMbid`. `other versions` (link_type 233) salió a
0 entre nuestras grabaciones — se deja en el allowlist por corrección, no por volumen.

**Enriquecimiento preservado:** este pase escribió **solo** `recordings` y `cover_versions`
(`INSERT` puro, sin `UPDATE` a ninguna otra tabla). Ninguna columna de
artists/releases/edges/labels se tocó — verificado contra las baselines de `mb-dump-import.md`
(artists 207 622, releases 668 885, artist_edges 200 063 sin cambio). Los contadores de
`embedding`/`listeners` sí subieron, pero por los **jobs de enriquecimiento paralelos** del
coordinador (embedding/listeners), no por este import.

**Idempotencia:** upsert por `(release_id, position)` y `(original, cover)`; re-ejecución → 0
filas nuevas, counts idénticos. (Nota de robustez, ya en los scripts: Postgres no tiene
`min(uuid)` — se resuelve el mbid→id representativo con `DISTINCT ON`; y varios pares mbid pueden
colapsar al mismo par de ids, deduplicado con `DISTINCT ON` antes del `ON CONFLICT`.)

---

## 5. Qué necesita la UI (para el agente de front) — C7 / C21 / C10

Estos datos viven ahora en `recordings` y `cover_versions`. **No hay endpoint todavía** — los
crea el agente de front en `src/web/server/**` + `src/front/**`. Contrato sugerido:

### C7 — Duración como eje  ·  C21 — Títulos  ·  B5 — Discografía con tracklist

- **Fuente**: `recordings WHERE release_id = @id ORDER BY position`.
- **Campos por track**: `position`, `title`, `length_ms` (int? — **puede ser null**, render
  "—" o sin barra; nunca inventar duración).
- **Endpoint sugerido**: `GET /api/artists/{mbid}/releases/{releaseMbid}/tracks` → lista
  `{ position, title, lengthMs }`. O anidar la tracklist en la ficha de release existente (B5).
- **C7 (duración como eje)**: agregado por artista/release, p.ej. duración media/total del
  catálogo del artista (`SELECT avg(length_ms), sum(length_ms) ... WHERE length_ms IS NOT NULL`).
  El eje funeral-doom↔grindcore se calcula sobre `length_ms`; **excluir null del promedio**.
- **C21 (minería de títulos)**: los `title` son el corpus. La UI/función de vocabulario cerrado
  + contador se construye sobre `recordings.title` (offline o en endpoint). **No** hay tabla de
  temática — es aproximación por títulos (D7/D8, sin Metal Archives).

### C10 — Grafo de versiones ("quién versionó a quién")

- **Fuente**: `cover_versions` join `recordings` (ambos extremos) join `releases` → `artists`.
- **Endpoint sugerido**: `GET /api/recordings/{recordingMbid}/versions` → aristas
  `{ original: {recordingMbid, title, artistMbid, artistName}, cover: {...}, relation }`.
  O un grafo por artista: todas las versiones que tocan sus grabaciones.
- **Render**: grafo `d3-force` + SVG (D18), nodos = grabaciones o artistas, aristas etiquetadas
  con `relation`. **Ojo**: la familia incluye versiones del **mismo** artista (remaster/edit
  propios); el cover cross-artista es el caso "de verdad" — la UI puede filtrar por
  `original.artistMbid <> cover.artistMbid` si quiere solo covers ajenos.
- **Cobertura honesta (R2)**: la mayoría de releases oscuras **no** tendrán versiones ni
  duraciones completas — estados vacíos diseñados, no huecos rotos.

---

## 6. Limpieza

El contenedor MB temporal (`grimoire-mb-recordings`, puerto 5435) puede pararse/borrarse — es
desechable (D5). Los dumps y el scratch viven en `/var/tmp/grimoire-mb`, fuera del repo.
