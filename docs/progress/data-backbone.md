# Ola D — Data backbone (agente Data Backbone)

> Estado: **completada y verde**. Frontera del agente: `src/shared/**`, `src/console/server/**`, y las migraciones EF (dueño único). No se tocó `src/web/server/**` ni `src/front/**`. `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones, 0 skips).

Cuatro tareas: (1) una migración con todo el esquema restante de SPEC §10, (2) verbo `influence` (Wikidata P737), (3) verbo `deaths` (Wikidata P570/P20), (4) verbo `atlas` (proyección 2D). Todo ejecutado de verdad contra la base viva; counts reales abajo.

---

## 1. Migración — `20260711004055_AddCreditsWorksDeathsAndProjection`

**Una sola migración** (el agente es dueño único de migraciones). Aplicada a la base viva (`dotnet ef database update` → *Done*). Contenido:

- **Tabla `credits`** — el modelo `Credit` pasa de reservado (sin tabla) a tabla real y **vacía**: `id, artist_id, release_id, recording_id, role, instrument, is_guest, source, confidence`. Se añadieron al modelo los dos campos que faltaban respecto a SPEC §10: `source` (`discogs | musicbrainz | inferred`, texto no-null) y `confidence` (`real`). FKs: `artist_id`→artists (cascade), `release_id`→releases (set null, un crédito puede ser solo de grabación); `recording_id` es columna `uuid` nullable sin FK (no existe tabla Recording aún). Índices en `artist_id` y `release_id`. **La puebla el ETL de créditos después, sin migración.**
- **Tabla `works`** (reservada mov. VII, D11) — mínima pero real: `id, mbid, title, kind`. `kind` es texto abierto (como `ArtistKind`). Índice único en `mbid`. Creada **vacía**; nada la escribe en esta ola.
- **`artists.death_date date null`** y **`artists.death_place text null`** (C12 In Memoriam).
- **`artists.xy_x double precision null`** y **`artists.xy_y double precision null`** (C18/B22 Atlas).

### Elección de `xy`: dos `double precision`, no `point`

SPEC §10 esboza `xy point`. Se guardan en su lugar **dos columnas `double precision` nullable** (`xy_x`/`xy_y`). Motivo: el `NpgsqlPoint` que mapea `point` es un `struct` cuya nullabilidad y comparación de valores son incómodas bajo EF (habría que un value-converter y un value-comparer solo para esto), mientras que dos `double?` son triviales, null-safe (ambos null = «sin proyectar todavía»), y el front lee el par directamente para el Atlas. La intención de SPEC §10 se respeta; solo cambia la representación física. Documentado en `Artist.cs`.

Verificado en la base: `credits` (9 columnas, 0 filas), `works` (0 filas), `artists.death_date/death_place/xy_x/xy_y` presentes con los tipos correctos.

---

## 2. Verbo `influence` — Wikidata P737 (B16)

`InfluenceJob` + `WikidataClient` (SPARQL, cadencia suave 1 req/2 s, UA honesto `Grimoire/0.1 ( pmanso@go2chain.es )`, `format=json`) + parsers puros en `src/shared/GrimoireLibrary/Wikidata/`.

- **Mapa QID→artista**: de `artists.links['wikidata']` (URL con QID). `Links` es un jsonb value-converted a string opaco, **no filtrable en SQL**, así que se leen id+links y se filtra en memoria (corpus pequeño, ~2.5k filas).
- **Consulta batched**: `VALUES ?a { wd:… }` en lotes de 50 (fija el sujeto a nuestro corpus; Wikidata nunca calcula sobre el grafo entero). El objeto `?b` (influencer) se filtra contra el corpus **en código** (`WikidataInfluence.ToEdges`).
- **Dirección de la arista**: `?a wdt:P737 ?b` = «a fue influenciado por b» → arista `InfluencedBy` con `From = a` (influenciado), `To = b` (influencer). Solo pares con **ambos** extremos en el corpus; lo demás se descarta (no se inventan nodos). Self-edges y duplicados se caen.
- **Idempotente**: upsert por `(from, to, InfluencedBy)`.

**Ejecución real**: `242 artists carry a Wikidata QID → 5 batches → 283 raw influence pairs → 67 within the corpus → 67 edges inserted`. Re-ejecución: `0 inserted, 67 already present` (idempotencia confirmada). Muestra verificada: `Guns N' Roses → The Beatles`, `Alice in Chains → Black Sabbath` (From=influenciado, To=influencer, dirección correcta).

---

## 3. Verbo `deaths` — Wikidata P570/P20 (C12)

`DeathsJob` + mismo `WikidataClient`. Solo artistas `Person` con QID (un grupo no muere). `Kind` filtra en SQL; el QID se lee en memoria (mismo motivo que arriba). SPARQL: `?a wdt:P570 ?death` (obligatorio → solo vuelven los fallecidos) + `OPTIONAL ?a wdt:P20 ?place` + label service para `?placeLabel`. Fecha: se parsea la parte `yyyy-MM-dd` del literal ISO (precisión reducida llega como `YYYY-01-01`); BCE/no-parseable → null. Solo lo que Wikidata afirma; sin dato → null. Idempotente (reescribe los mismos valores).

**Ejecución real**: `2 people carry a Wikidata QID → 1 batch → 0 death dates, 0 places written`. **No es un bug**: solo 2 `Person` del corpus tienen link de Wikidata (los folk anchors Einar Selvik y Danheim, **ambos vivos**). Ver el hueco declarado abajo.

---

## 4. Verbo `atlas` — proyección 2D (C18/B22)

`AtlasJob` proyecta los embeddings (`embedding not null`) a 2D y escribe `xy_x`/`xy_y`.

### Elección de proyección: **PCA a mano en Python puro**

El brief pedía umap-learn → sklearn PCA → PCA a mano con numpy. En este entorno **no está instalado ni umap-learn, ni scikit-learn, ni numpy, y no hay pip** (`python3 -m pip` → *No module named pip*). Así que `scripts/atlas_project.py` hace una **PCA a mano en Python puro** (stdlib solo): power-iteration para las dos primeras componentes principales (aplica `C v = Xᵀ(X v)` sin formar la covarianza 768×768), con deflación para la 2ª. Coste cero, offline, local (D6). **Determinista** (inicialización fija, sin aleatoriedad) → idempotente.

El `AtlasJob` vuelca id+embedding a un temp JSON, invoca `python3 scripts/atlas_project.py <in> <out>`, lee de vuelta `{id:[x,y]}` y persiste. Si python3 falta o el script falla, loguea el error y **no escribe coordenadas** (no inventa). Ruta del script overridable por `GRIMOIRE_ATLAS_SCRIPT`.

**Ejecución real**: `Projecting 309 embeddings → projected 309 → 309 artists projected to 2D`. Rango: `xy_x ∈ [-4.77, 8.12]`, `xy_y ∈ [-5.51, 7.79]` (dispersión real, no colapso). Re-ejecución determinista (idéntica). Validado además en un test sintético de 3 clusters: PC1 separa los clusters (medias -4.98 / 0.15 / 4.83).

---

## 5. Verificación

```
dotnet build src/web/Grimoire.slnx -warnaserror   → 0 Advertencias, 0 Errores
dotnet test  src/web/Grimoire.slnx                 → Superado: 168, Con error: 0, Omitido: 0
bash scripts/audit.sh --strict                     → RESULT: PASS (0 violaciones, 0 skips)
```

**Tests nuevos que muerden** (30, en 3 clases): `WikidataQidTests` (extracción de QID: page-url, entity-uri, fragment/query, no-QID, null), `WikidataInfluenceTests` (parseo SPARQL, emparejado QID→artista, dirección de arista, self-edges, dedupe, extremo sin emparejar), `WikidataDeathsTests` (parseo de fecha ISO/precisión-reducida/BCE/null, place opcional, fila sin QID). **Comprobado que muerden**: al romper la guarda de self-edge (`fromId == toId` → siempre falso) el test `ToEdges_DropsSelfEdges` falla; revertido, verde de nuevo.

Estado de la base tras la ola: `artist_edges` = 2342 `MemberOf` + **67 `InfluencedBy`**; `credits` = 0; `works` = 0; `xy` poblado en **309**; `death_date`/`death_place` = 0 escritos.

---

## 6. Huecos declarados (y su porqué)

- **`credits` y `works` vacías, a propósito.** El esquema existe; poblarlas es de otro agente (ETL de créditos/sellos) **sin migración** — mi migración les deja la forma. El `EmbeddingTextBuilder` ya acepta sellos; en cuanto exista `labels`/`releases.label_id`, el sello entra en el texto sin cambio de código.
- **`deaths` no escribió nada porque casi ningún `Person` tiene QID.** 240 de 310 grupos tienen `links['wikidata']`, pero solo **2 de 2168 personas** (los folk anchors, vivos). Las ~2166 filas de miembro las insertó la pasada `edges` como filas mínimas (sin `url-rels`), así que no llevan QID de Wikidata. **Para que C12 In Memoriam tenga datos hace falta, antes, una pasada de MusicBrainz que traiga `inc=url-rels` de las filas de miembro** (para que ganen su QID de Wikidata); entonces `deaths` re-ejecutado las pobla. Esa pasada MB queda fuera de esta ola (el brief pedía evitar MB aquí). El verbo `deaths` está construido, probado y es correcto — el dato de entrada es el que falta.
- **`atlas` es PCA lineal, no UMAP.** UMAP daría una nube más separada perceptualmente, pero no está disponible sin pip/numpy. La PCA a mano es la mejor opción de coste cero/offline aquí. Si en el futuro hay numpy/umap, se cambia solo el `scripts/atlas_project.py` (la interfaz JSON in/out no cambia) — el `AtlasJob` no se toca.
- **`recording_id` en `credits` es columna suelta sin FK** — no existe tabla Recording; el ETL de créditos decidirá si merece una.

---

## 7. Para el siguiente agente (ETL de créditos/sellos, sobre mi esquema)

- Poblar `credits` y `works` **sin migración**: las tablas ya existen con la forma de SPEC §10. `credits` lleva `source`+`confidence` — un crédito inferido (intersección de intervalos, D9) debe llevar `source='inferred'` y confidence < 1, y marcarse como inferido en la UI.
- Poblar `labels` + `releases.label_id` exige bajar a nivel `release` en MusicBrainz (label-rels). Es el paso que también desbloquea el sello en el texto de embedding.
- Si se hace la pasada MB de `url-rels` para las filas de miembro, **re-ejecutar `deaths`** después: poblará In Memoriam sin más trabajo.
