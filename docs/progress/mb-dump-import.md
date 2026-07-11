# Movimiento II — Import del dump completo de MusicBrainz (agente MB-dump)

> Estado: **completado y verificado contra la base viva**. `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones). Sustituye el corpus de juguete (2 501 artistas, muestra WS/2) por el catálogo real destilado del dump de MusicBrainz (**207 622 artistas, 199 971 aristas de miembro, 668 885 releases, 65 600 sellos**). Frontera del agente: `src/console/server/**`, `src/shared/**`, `scripts/**`, e infra local (un contenedor Postgres MB temporal). **No se tocó** `src/web/server/**` ni `src/front/**`. **No se creó ninguna migración** — el esquema de Grimoire ya soporta todo. D5: el mirror MB es artefacto de build; el trabajo se hizo como **scripts SQL** (`scripts/mb-import/`), no como verbo de consola, porque es una operación masiva de conjuntos, no llamadas por fila.

Las cuatro fases del brief: (1) extraer las tablas del tar, (2) montar Postgres MB temporal y cargar, (3) destilar el subgrafo metal/rock/folk a Grimoire (upsert por MBID, sin pisar enriquecimiento), (4) verificar contra la base viva.

---

## 0. Insumos y arquitectura

- Dumps: `/var/tmp/grimoire-mb/{mbdump.tar.bz2(7.34 GB), mbdump-derived.tar.bz2(504 MB)}`. Extraídos a `/var/tmp/grimoire-mb/mbdump/` (solo las tablas necesarias, un pase de descompresión por tar).
- Postgres MB temporal: contenedor `grimoire-mb-import`, `postgres:16-alpine`, puerto **5434** (distinto del 5433 de Grimoire), tuneado para carga masiva (`fsync=off`, `synchronous_commit=off`, `shared_buffers=2GB`). Dump montado read-only en `/dump`. Es temporal (D5) — no forma parte de producción.
- Transferencia MB→Grimoire: se destila dentro del Postgres MB a tablas ya con forma Grimoire (enums como texto EF, gids como clave), se vuelca a TSV, se carga en un esquema `mb_import` de Grimoire y se hace el upsert por MBID con SQL de conjuntos. Sin FDW/dblink (no requieren extensiones extra).

### Scripts (en el repo, reproducibles)

- `scripts/mb-import/01-load-schema.sql` — DDL del Postgres MB temporal (columnas en el orden exacto del esquema MB para que el COPY cuadre; columnas no usadas tipadas TEXT).
- `scripts/mb-import/02-copy-and-index.sql` — `COPY` de los ficheros del dump + índices para la destilación.
- `scripts/mb-import/03-distill.sql` — selección de corpus (D23) + construcción de las tablas staging con forma Grimoire.
- `scripts/mb-import/04-upsert.sql` — upsert por MBID en la base viva de Grimoire, **preservando el enriquecimiento** existente.
- `scripts/mb-import/run.sh` — orquestador end-to-end (idempotente).

---

## 1. Tablas cargadas del dump (counts reales)

```
artist              2 927 951      release_group        4 396 444
artist_credit_name  7 012 236      release              5 623 968
artist_tag            745 654      release_group_meta   4 396 444 (4 197 295 con fecha)
tag                   240 973      label                  342 879
l_artist_artist       844 283      url                 20 505 482
l_artist_url        6 249 039
```
(lookups pequeñas: `artist_type`, `area`, `iso_3166_1`, `link`, `link_type`, `link_attribute`, `link_attribute_type`, `release_group_primary_type`, `release_group_secondary_type`, `release_group_secondary_type_join`, `release_label`, `artist_credit` — todas cargadas.)

Del core (`mbdump.tar.bz2`): `artist, artist_type, area, iso_3166_1, l_artist_artist, link, link_type, link_attribute, link_attribute_type, release_group, release_group_primary_type, release_group_secondary_type, release_group_secondary_type_join, release, artist_credit, artist_credit_name, label, release_label, url, l_artist_url`.
Del derived (`mbdump-derived.tar.bz2`): `tag, artist_tag, release_group_meta`.

**Añadidas al listado del brief** (justificadas): `iso_3166_1` (para el código ISO de país vía `area`, que el brief pide explícitamente pero no listaba) y `release_group_meta` (para la fecha de primer lanzamiento del grupo — `release` **no tiene columna de fecha** en MB; las fechas viven en `release_country`/`release_group_meta`, y `meta` es una fila por grupo, lo limpio). **Ojo**: `release_group_meta` es dato **derivado**, así que vive en `mbdump-derived.tar.bz2`, no en el core — extraerlo del core falla con "not found in archive".
**Del listado del brief NO cargadas**: `gender`, `artist_alias` — Grimoire no tiene columna destino para ellas (sin género ni alias en el modelo). Extraídas-pero-no-destiladas.

### Hechos MB verificados empíricamente (usados en la destilación)

- **`member of band` = `link_type` 103**; `link_phrase="member of"` va en `entity0` → **`entity0` = miembro (Person), `entity1` = banda**. Coincide exactamente con las aristas Grimoire existentes (from=miembro, to=banda) — sin inversión. Verificado contra Darkthrone (Fenriz 1986 abierto, Anders 1987-1988, etc.).
- **Instrumentos** = atributos con `link_attribute_type.root IN (14 instrument, 3 vocal)`. Los cualificadores (`original`/`additional`/`eponymous`/`minor`) quedan fuera por construcción.
- **Invitado** = un `link` con atributo de `root 194 (guest)` → la membresía se **excluye** (igual que el `EdgesJob` original).
- **MB no tiene relación artista-artista `influenced by`** (la query la buscó y no existe). La influencia sigue siendo **solo Wikidata P737** (D3) — la "influence de MB (opcional)" del brief no aplica.

---

## 2. Selección de corpus (D23)

Corpus = **filas existentes en Grimoire** ∪ **allowlist de tags** ∪ **expansión por `member of band`** (2 saltos).

### Allowlist de géneros (3 523 tags MB coincidentes)

- **`LIKE '%metal%'`** — captura todos los subgéneros de metal (black/death/doom/thrash/heavy/power/folk/viking/… metal, metalcore, post-metal) de un plumazo.
- **Lista explícita** para lo metal-adyacente que no lleva la palabra "metal", el hard rock / punk que orbita, y los folk con nombre: `grindcore, goregrind, deathcore, powerviolence, mathcore, crossover thrash, crust(+punk), d-beat, djent, blackgaze, dungeon synth, sludge, doom, stoner, drone, noise rock, post-hardcore` · `hard rock, heavy rock, stoner rock, psychedelic rock, progressive rock, gothic rock, southern rock, blues rock, garage rock, acid rock, proto-metal` · `punk(+rock), hardcore punk, melodic hardcore, post-punk, psychobilly, horror punk` · `neofolk, viking/nordic/pagan/celtic/dark/ritual/medieval folk, folk metal, martial industrial, neoclassical darkwave`.
- **Nunca** `folk`/`rock`/`pop` a secas. Filtro `artist_tag.count >= 1` (solo tags con voto neto positivo — descarta ruido con downvotes).

### Crecimiento del corpus (medido)

```
existentes en Grimoire            2 501
+ tag-match (H0)                 62 520   (allowlist ∩ artist_tag)
+ expansión grafo salto 1       138 562
+ expansión grafo salto 2       207 622   <- corpus final
```

La expansión desacelera (76k en el salto 1, 69k en el 2): no explota a millones. La expansión es el criterio de admisión Bloodline de D23 — un artista entra por una arista `member_of` real, no por opinión. Se paró en 2 saltos (el brief: "1-2 saltos").

---

## 3. Mapeo MB → Grimoire

| Grimoire | Origen MB | Nota |
|---|---|---|
| `artists.mbid` | `artist.gid` | clave de upsert |
| `artists.kind` | `artist_type.name` | Person/Orchestra/Choir; **todo lo demás → Group** (Character/Other/null), igual que `MbMapping.MapKind` |
| `artists.country` | `area` → `iso_3166_1.code` | ISO-2; null si el `area` es sub-país (hueco honesto, no se inventa) — 62% de cobertura |
| `artists.city` | `begin_area.name` | |
| `artists.formed_year` / `dissolved_year` | `artist.begin_date_year` / `end_date_year` | |
| `artists.tags` | top-8 de `artist_tag` por `count` (≥1) | |
| `artists.links` | `l_artist_url`→`url`+`link_type.name` | jsonb {tipo→url}; **solo filas nuevas** |
| `artist_edges` (MemberOf) | `l_artist_artist` link_type 103 | from=entity0(miembro), to=entity1(banda) |
| `.begin_date`/`.end_date` | `link.*_date_*` | día forzado a 1 (año/mes; a prueba de días inválidos, igual que los edges previos) |
| `.instruments` | `link_attribute`→`link_attribute_type` root 14/3 | merge de estancias: min begin, fin abierto gana, unión de instrumentos; invitados (root 194) excluidos |
| `releases.mbid` | `release_group.gid` | atribuido a UN artista corpus (min posición de crédito, luego min id) — D29 |
| `releases.type` | primary + secondary type | Demo/Compilation/Live > Album/EP; Single/Broadcast/Other **descartados** (Grimoire no tiene Single) |
| `releases.release_date` | `release_group_meta.first_release_date_*` | |
| `releases.label_id` | `release_label` de la release de menor id del grupo con sello | monovaluado, arbitrario (documentado) |
| `labels.mbid/name/country` | `label.gid/name`, `area`→iso | |

**Regla de no-destrucción (upsert por MBID)**: en una fila existente solo se refrescan campos estructurales; **nunca** se tocan `listeners, rank, embedding, preview_url, links, abstract, image_url, death_date, death_place, xy_x, xy_y` (artists), `artist_id` (releases, D29) ni `cover_url`. País/ciudad/año usan `COALESCE(nuevo, existente)` (un null del dump no borra un dato de WS/2). Las filas nuevas quedan con el enriquecimiento en null (enriquecimiento perezoso posterior — D5/D19).

---

## 4. Counts (antes → después)

| tabla / campo | antes | después |
|---|---|---|
| `artists` | 2 501 | **207 622** (Group 108 657, Person 98 250) |
| `artist_edges` MemberOf | 2 342 | **199 971** (60 172 con begin, 83 436 con instrumentos) |
| `artist_edges` total | 2 434 | 200 063 (+80 InfluencedBy, +12 Teacher/Student intactos) |
| `releases` | 5 320 | **668 885** (560 641 con label_id) |
| `labels` | 470 | **65 600** |

**Enriquecimiento preservado — verificado idéntico antes/después**: `listeners`=290, `rank`=290, `embedding`=309, `preview_url`=80, `death_date`=65, `xy`=309, `credits`=34 451. Links de filas existentes intactos (verificado el jsonb completo de una fila con previews: conserva sus `listen:apple_music`/`listen:deezer`/… y sus url-rels).

**Idempotencia**: garantizada estructuralmente (upsert por `mbid`/clave de arista únicos; `gen_random_uuid()` solo en insert nuevo). Re-ejecutado el transfer+upsert completo → counts idénticos, 0 filas nuevas.

---

## 5. Verificación (Manowar / Gamma Ray / Overkill)

Las tres presentes con país, año de formación y **formación real `member_of` con fechas**:

```
Gamma Ray  DE 1989  12 member-edges  32 releases
Manowar    US 1980  11 member-edges  56 releases
Overkill   US 1980  14 member-edges  55 releases
```

Muestra (Gamma Ray, fechas + instrumentos, fin abierto para los actuales): Kai Hansen (1989–, guitar), Ralf Scheepers (1989–1994, lead vocals), Henjo Richter (1997–, guitar), Frank Beck (2015–, lead vocals). Justo lo que el Gantt (B7/B8) necesita.

---

## 6. Qué queda para enriquecer (para el coordinador, perezoso — D5/D19)

De los **207 622** artistas:
- **sin embedding**: 207 313 (solo 309 embebidos). Los ~80k con tags (más los que tienen país/miembros) tienen señal para el `EmbeddingTextBuilder`; el resto (filas mínimas sin señal) quedan null a propósito. **NO se corrieron embeddings** (100k+ por Ollama son horas — lo lanza el coordinador).
- **sin listeners/rank**: 207 332 (solo 290). Necesita el verbo `listeners` (Last.fm por mbid, D37) sobre el nuevo catálogo.
- **sin preview_url**: 207 542 (solo 80). Necesita el verbo `previews` (iTunes/Deezer, D25) — perezoso y por lotes.
- `abstract` sigue null (sin pase Wikidata masivo); `credits`/`death`/`atlas` solo cubren el corpus viejo hasta que se re-corran sobre el nuevo.

El catálogo estructural (artistas, aristas con fechas/instrumentos, releases con tipo/fecha/sello, labels, links) queda **cargado y verificado**. El Postgres MB temporal (`grimoire-mb-import`, puerto 5434) puede pararse/borrarse — es desechable (D5).
