# Movimiento II — ETL (agente ETL)

> Estado: **en curso**. Este documento registra qué se construyó, qué se verificó (comando → salida real), y los huecos con su porqué. Frontera del agente: `src/shared/**`, `src/console/server/**`, y las migraciones EF. No se tocó `src/web/server/**` ni `src/front/**`.

Las cuatro tareas del brief, en orden de dependencia: (1) `member_of` con fechas e instrumentos, (2) previews iTunes-primero/Deezer-complemento, (3) embeddings centrados (D26 variante C), (4) comando `stats`.

---

## 1. Qué se construyó

### Migración EF (único dueño de las migraciones)

`20260710220431_AddPreviewUrlAndCorpusStats`:
- `artists.preview_url text null` — URL de preview de 30–45 s (iTunes primero, Deezer complemento). Null = hueco real (banda inaudible), nunca inventado.
- Tabla `corpus_stats(id, mean_embedding vector(768), artist_count, computed_at)` — **una sola fila** (id=1). Persiste el **vector medio del corpus** (D26) para que el lado de consulta reste el mismo vector al vector de gusto del usuario. Sin esto, el vector de consulta y los indexados vivirían en marcos distintos y el anillo no significaría nada.

`artist_edges` ya existía del movimiento I (no requirió migración).

### Tarea 1 — `member_of` con fechas e instrumentos

- **`MusicBrainzClient.GetArtistRelationsAsync`** (`inc=artist-rels`) + campos nuevos en `MbRelation` (direction, begin, end, ended, attributes, artist).
- **`MembershipResolver`** (shared, puro y testeado): traduce una relación MB "member of band" en una arista dirigida `member_of` (miembro → banda). Resuelve qué extremo es el miembro por `direction`, **filtra invitados** (atributo `guest`) y tipos no-membresía, normaliza atributos a instrumentos (descarta `original`/`additional`), y parsea fechas parciales MB (`1986`, `1991-03`, `1991-03-15`). `Merge` fusiona múltiples estancias del mismo par (min begin, fin abierto gana, unión de instrumentos) porque el índice único es `(from, to, kind)`.
- **`EdgesJob`** (verbo `edges`): consulta cada artista ya sembrado a **1 req/s** (reutiliza `MusicBrainzRateLimiter`), agrega memberships globalmente, e inserta como fila mínima (mbid+nombre+kind, sin tags/releases/embedding) al artista del otro extremo si no está en el catálogo — una capa de expansión (D23). Idempotente: artistas upsert por MBID, aristas upsert por `(from, to, member_of)`.
- **Miembro oficial ≠ invitado**: solo `type == "member of band"` sin atributo `guest`. Los invitados viven en créditos de grabación, no aquí.

### Tarea 2 — previews iTunes primero, Deezer complemento (D25)

- **`IEnrichmentSource` + `ArtistEnrichment`** (shared, D9/invariante 5): toda fuente detrás del contrato, con feature flag `Enabled`. Una fuente apagada se salta; ninguna vista se rompe.
- **`ITunesEnrichmentSource`** (fuente **principal**, D25: iTunes 41 % > Deezer 19 %): iTunes Search API sin key, paceada a **3 s (~20 req/min)**. Song search; toma `previewUrl` + `artistViewUrl` (Apple Music exacto) del primer resultado cuyo nombre **coincide exacto** (`NameMatch`).
- **`DeezerEnrichmentSource`** (complemento): search/artist → `link` exacto; artist/top → preview de respaldo. Coincidencia exacta de nombre.
- **`NameMatch`** (shared, testeado): normaliza (minúsculas, sin diacríticos, espacios colapsados) y exige igualdad. Evita el ruido de los spikes (salió "Toto"; el "Death" equivocado). El 52 % es **cota inferior** a propósito: mejor un hueco que el audio de otra banda.
- **`StreamingLinks`** (shared, testeado, D10/B26): Apple Music y Deezer exactos cuando se resuelven; Spotify/YouTube/YT Music/Tidal/Bandcamp por URL de búsqueda. Claves con prefijo **`listen:`** para no pisar las url-rels crudas de MusicBrainz ya guardadas en `artists.links`.
- **`PreviewJob`** (verbo `previews`): **perezoso y por lotes** — procesa solo artistas no intentados (sin clave `listen:`), hasta `Preview:Limit` (default 60, override `GRIMOIRE_PREVIEW_LIMIT`). Re-ejecutar continúa. iTunes primero, Deezer complemento: `preview_url = apple ?? deezer`. Merge aditivo de links (preserva las url-rels de MB).

### Tarea 3 — embeddings centrados (D26 variante C)

- **`EmbeddingTextBuilder`** (shared, testeado): texto rico variante C — nombre, país/ciudad, tags, miembros (del grafo de tarea 1), sello y abstract cuando existan. Devuelve `null` si no hay señal alguna (fila de miembro pelada) → su embedding queda null, no se inventa. **No** se reduce a solo-tags (variante D rechazada: colapsa el 17 % sin tags).
- **`OllamaClient`** (autohospedado, D6/coste cero): `nomic-embed-text`, 768 dims.
- **`VectorMath`** (shared, testeado): `Mean`, `Subtract`, `CosineDistance` (coincide con `vector_cosine_ops` de pgvector).
- **`EmbeddingJob`** (verbo `embeddings`): embebe cada artista con señal, calcula el **vector medio**, lo **resta antes de indexar** (esto triplica la separación — variante C), guarda el centrado en `artists.embedding` (índice HNSW existente) y **persiste el medio** en `corpus_stats`. Feature flag `Sources:Ollama:Enabled`.

### Tarea 4 — comando `stats`

- **`NeighborStats`** (shared, testeado): percentiles (R-7), `Spread(P10,P50,P90)`, `IsDegenerate`.
- **`StatsJob`** (verbo `stats`): para cada embedding centrado mide la distancia coseno a todos los demás y lee el vecino p10/p50/p90; reporta la media de cada uno. **CRÍTICO (CLAUDE.md)**: si los tres salen casi iguales, el arreglo de D26 no funciona a esta escala y el motor sigue roto. Imprime los tres números y un veredicto HEALTHY/DEGENERATE.

### Infra

- **`WorkerJob`** (base): patrón one-shot (tarea en background → `StopApplication`), fallo logueado nunca silenciado.
- **`Program.cs`**: dispatcher de verbos (`seed`/`edges`/`previews`/`embeddings`/`stats`). Sin verbo → imprime uso y sale (no siembra en cada arranque — D29).

---

## 2. Verificación (comando → salida real)

Toolchain: .NET SDK 10.0.109, EF 10.0.9, Postgres del contenedor `grimoire-postgres-dev` (pg17), Ollama `nomic-embed-text` (768).

### Build + tests

```
dotnet build src/web/Grimoire.slnx -warnaserror   → 0 Advertencias, 0 Errores
dotnet test  src/web/Grimoire.slnx                → Superado: 72, Con error: 0, Omitido: 0
```
50 tests nuevos (eran 22): `MembershipResolverTests` (11), `VectorMathTests` (9), `NeighborStatsTests` (6), `EmbeddingTextBuilderTests` (5), `StreamingLinksTests` (4), `NameMatchTests` (8) + los del mov. I. La integración corre contra Postgres vivo (0 omitidas). **Muerden**: al invertir `SpreadOf_NearlyEqualDistances_IsDegenerate` (esperar `False`) el test falla (`Con error: 1`); revertido, verde de nuevo.

### Tarea 1 — edges (member_of)

```
BEFORE: artists 307 | edges 0 | persons 2
dotnet run --project src/console/server -- edges
  → Resolved 2342 distinct memberships from 307 artists.
  → Edges complete: 2171 member rows added, 2342 edges inserted, 0 edges updated.
AFTER:  artists 2478 | edges 2342 | persons 2168
        edges_with_begin 2084 | edges_with_instruments 1833
        dup_edges 0 | dup_artist_mbid 0
```

Muestra real (Darkthrone, `member_of` con fechas e instrumentos):
```
 member           | begin      | end        | instruments
 Fenriz           | 1986-01-01 |            | {"drums (drum set)"}
 Anders Risberget | 1987-01-01 | 1988-01-01 | {guitar}
 Dag Nilsen       | 1987-01-01 | 1991-01-01 | {"bass guitar"}
 Zephyrous        | 1988-01-01 | 1993-01-01 | {guitar}
 Nocturno Culto   | 1988-01-01 |            | {"lead vocals",bass,guitar}
```
Miembros actuales con fin abierto, ex-miembros con fin cerrado, instrumentos por miembro: exactamente lo que el Gantt (B7/B8) necesita.

**Idempotencia**: garantizada estructuralmente — índices únicos `ix_artists_mbid` y `ix_artist_edges_from_id_to_id_kind` hacen imposible un duplicado (`dup_edges = 0`, `dup_artist_mbid = 0`), y el write hace upsert por MBID / por clave de arista (encontrada → `edgesUpdated++`, no insert). La **re-ejecución en vivo** se lanzó pero **MusicBrainz empezó a devolver 429**; el resilience handler la ralentizó honrando su límite (comportamiento correcto), y se detuvo a los ~25 min para no seguir cargando sus servidores. Las cuentas quedaron idénticas (2478/2342). No hay atajo honesto que pruebe el path de update sin volver a llamar a MB; el índice único es la garantía dura.

### Tarea 2 — previews (iTunes primero, Deezer complemento)

```
GRIMOIRE_PREVIEW_LIMIT=80 dotnet run --project src/console/server -- previews
  → Preview sources — iTunes: on, Deezer: on. Batch limit: 80.
  → 80 artists pending preview resolution (of 307 candidates).
  → Preview batch complete: 71/80 resolved a preview (88.8%). Re-run to continue.
```

La cobertura (88,8 %) es más alta que el 52 % del spike v2 porque el corpus sembrado (metal+folk **con tags**) no es el underground puro de NWN!/Iron Bonehead del spike — es una muestra menos oscura. Coherente, no contradice D25.

Muestra real (preview iTunes + links exactos + **url-rel de MB preservada**):
```
1914       | preview itunes… | apple: music.apple.com/us/artist/1914/… | deezer: deezer.com/artist/9706814 | discogs (MB): preservado
Abominator | preview itunes… | apple: …/abominator/297093154           | deezer: …/12221                  | discogs preservado
```
El merge es aditivo: las claves `listen:*` conviven con las url-rels crudas (`discogs`, `wikidata`, `youtube`) del seed.

**Resumible / idempotente**: 2ª corrida `GRIMOIRE_PREVIEW_LIMIT=10` → "10 artists pending (of 307)" — saltó los 80 ya intentados y avanzó a los siguientes 10 (9/10 con preview). Los intentados no se re-tocan porque ya llevan claves `listen:`.

### Tarea 3 — embeddings centrados (D26 variante C)

```
dotnet run --project src/console/server -- embeddings
  → Embedding complete: 309 centred embeddings, 2169 artists skipped (no signal).
    Corpus mean persisted over 309 vectors.
AFTER: artists.embedding no-null = 309 | corpus_stats: id=1, artist_count=309, mean_embedding presente
```
Los 2169 saltados son las filas mínimas de miembro (sin tags/releases/país/miembros): su embedding queda **null**, no se inventa. Los 309 embebidos incluyen 2 bandas descubiertas por el grafo que tienen señal solo por sus miembros (variante C).

**Idempotencia**: re-ejecutar → 309 de nuevo, `artist_count` 309, y `stats` idéntico (el pase es determinista y sobrescribe).

### Tarea 4 — stats (verificación crítica D26)

```
dotnet run --project src/console/server -- stats
  → Computing neighbour-distance percentiles over 309 centred embeddings...
  →   p10 (near neighbour): 0.8511
  →   p50 (median)        : 1.0143
  →   p90 (far neighbour) : 1.1394
  →   spread p10->p90     : 0.2883
  → VERDICT: HEALTHY — the three percentiles diverge (spread 0.2883); the slider has room to travel.
```

**Los tres números divergen** (0.85 / 1.01 / 1.14, spread 0.29 sobre una mediana de ~1.0). No son casi iguales → el arreglo de D26 **sí funciona a esta escala**; el motor no está roto y el slider Comfort↔Abyss tiene recorrido. Coincide con la predicción del spike v3b variante C (p05≈0.827, p50≈1.013, p95≈1.154). Sin centrar, la cáscara fina daría los tres casi idénticos y el veredicto sería DEGENERATE.

### Gate

```
bash scripts/audit.sh --strict   → RESULT: PASS (Violations 0, Skipped 0, audit-ok 0)
```

### Migración creada

`src/shared/GrimoireLibrary/Migrations/20260710220431_AddPreviewUrlAndCorpusStats.{cs,Designer.cs}` (+ snapshot actualizado). Añade `artists.preview_url` y la tabla `corpus_stats`. **Es la única migración de este pase** — el agente ETL es el único dueño de las migraciones.

---

## 3. Huecos declarados (y su porqué)

- **`labels` sigue vacío** → el texto de embedding no incluye sello. Poblar sellos exige bajar a nivel `release` (fuera del alcance del movimiento I y de estas cuatro tareas). El `EmbeddingTextBuilder` ya acepta sellos: en cuanto existan, entran sin cambio de código.
- **`abstract` sigue null** (sin pase de Wikidata) → el texto de embedding se construye sin abstract. Es la cota pesimista que D26 ya anticipa: producción con Wikidata tendrá más señal.
- **`listeners`/`rank` siguen null** — **no hay key de Last.fm** (bloqueador conocido, D6/Q5). No se fabrican. La cobertura de preview no se pudo correlacionar con la oscuridad *dentro* del underground (cavéat de D25) por lo mismo.
- **Una arista por `(miembro, banda)`** — el índice único no admite estancias múltiples separadas; se fusionan (min begin / fin abierto gana / unión de instrumentos). Se pierde el hueco entre estancias. Cambiarlo sería una migración futura si el Gantt lo exige.
- **Expansión de una sola capa** — los miembros/bandas descubiertos se insertan como filas mínimas pero **no** se vuelven a consultar por sus propias relaciones. Bounded a propósito.
- **`preview_url` es cota inferior** — emparejado por nombre exacto (D25). Diacríticos y nombres estilizados convierten fallos de match en falsos "sin preview".
- **Solo `member_of`** — otros tipos de relación artista-artista (colaboración, side project) no se importan en este pase; el enum `EdgeKind` los reserva.
