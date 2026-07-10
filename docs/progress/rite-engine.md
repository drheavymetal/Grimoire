# Movimiento II — El Rito (agente Motor del Rito)

> Estado: **terminado y verde**. Motor de descubrimiento, arranque en frío, servido a ciegas con proxy de audio, y Summon/Banish/Again escribiendo de verdad. Verificado contra la base viva. Frontera del agente: `src/shared/**`, `src/web/server/**`, y las migraciones EF. **No se tocó `src/front/**`** (lo consume el agente de ficha después). Fecha: 2026-07-10.

Este documento registra qué se construyó, qué se verificó (comando → salida real), los huecos con su porqué, y las decisiones de esquema/motor a promover a `DECISIONS.md`.

---

## 1. Qué se construyó

### Migración EF (dueño único de migraciones este pase)

`20260710231224_AddUserTasteAndRites`:
- `user_taste(user_id PK, embedding vector(768), repulsion vector(768), depth_score int, updated_at)` — FK a `AspNetUsers` con borrado en cascada. Una fila por usuario (SPEC §10).
- `rites(id PK, user_id, artist_id, state, risk real, served_at, resolved_at)` — `state` como texto (`served|summoned|banished|again`, convención D29), índice `(user_id, artist_id)` de SPEC §10, FKs a `AspNetUsers` y `artists` en cascada.

Aplicada a la base viva; ambas tablas verificadas con `\d`.

### El motor — búsqueda en anillo por percentiles (`RiteEngine`, D4 corregido por D26)

`src/web/server/Services/RiteEngine.cs`. El algoritmo exacto de D26/§6:
1. **Muestrea** el pool servible (`embedding IS NOT NULL AND preview_url IS NOT NULL`) al azar (`ORDER BY random() LIMIT SampleSize`), calcula la distancia coseno de cada muestra al `taste` **con el índice HNSW** (`Embedding.CosineDistance(taste)` de `Pgvector.EntityFrameworkCore`).
2. Con esa distribución de distancias, `RingResolver` obtiene los **dos radios en los percentiles del slider**. El slider Comfort↔Abyss (`comfort ∈ [0,1]`) desliza una ventana de percentiles de ancho `0.20`: en 0 selecciona el extremo cercano, en 1 el lejano. **Percentiles hacia el usuario, radios hacia el índice.**
3. Consulta rangada: pool servible, en el anillo `[rLo, rHi]`, **menos lo desterrado** (`repulsion`: se muestrea la distancia a la repulsión y se excluye el `p20` más cercano — D4 "resta activamente"), **menos lo ya riteado** (`NOT EXISTS`, con la excepción C3 de abajo), con filtros duros opcionales, y se toma **uno al azar** dentro del anillo.

**La trampa de la doble resta (D26 / CLAUDE.md), respetada y documentada en el código**: `taste` y `repulsion` se construyen promediando embeddings **ya centrados** (los de `artists.embedding`), así que ya viven en espacio centrado. **No se resta el vector medio del corpus otra vez.** El medio de `corpus_stats` es solo para centrar un vector de consulta *externo* (crudo), y este pase **no lo usa** — no hay ninguna consulta cruda aquí. Está anotado en `TasteMath`, `UserTaste`, el `DbContext` y `RiteEngine`.

### Arranque en frío (D15)

- **Elegir 5 bandas** (la vía que SÍ se construye): `GET /api/rite/seed-candidates` devuelve bandas con embedding, las más prolíficas primero (reconocibles), **no a ciegas** — es la pantalla de "elige tus bandas". `POST /api/rite/seed` calcula `user_taste.embedding = media de sus embeddings` (ya centrados; **no re-centra** — `TasteMath.Seed`).
- **C1 Import Last.fm** (BLOQUEADO, sin key): adaptador `IColdStartImport` / `LastFmColdStart` **con feature flag apagado** (`Enabled = false` mientras no haya `LastFm:ApiKey`). El endpoint `POST /api/rite/import-lastfm` devuelve **503 con mensaje explícito** en vez de inventar scrobbles. El código de `user.getTopArtists` es real y correcto (no un stub), gateado enteramente por la key; la ruta viva no se puede probar sin key (hueco declarado). El mapeo Last.fm→catálogo usa `NameMatch.Normalize` (D25: mejor un hueco que la banda equivocada).

### The Rite a ciegas + proxy de audio anti-filtración (B13, SPEC §5.3)

- `POST /api/rite/serve` devuelve un DTO **sin nombre, género, país ni portada**: solo `token`, `riskPercentile` y `audioUrl`. La URL de origen del preview **nunca** llega al cliente.
- `GET /api/rite/{token}/audio` (**anónimo**, URL de capacidad — el token es el `rite.Id`, un GUID inadivinable) hace **stream del preview en el servidor** (`HttpCompletionOption.ResponseHeadersRead`).
- **SSRF cerrado dos veces** (`PreviewAudioProxy`): la URL **jamás** viene del cliente — es siempre el `preview_url` que resolvió nuestro ETL — y, defensa en profundidad, el host debe estar en una **allowlist** (CDNs de iTunes/Apple y Deezer), con redirecciones automáticas **desactivadas** (`AllowAutoRedirect = false`) para que no salte a otro host.

### Summon / Banish / Again escriben de verdad (`POST /api/rite/{token}/resolve`)

- **Summon**: `taste = ApplySummon(taste, artistEmb)` (media móvil exponencial con decay 0.25 hacia la banda). `rites.state = Summoned`. **Revela** la banda (el premio) con explicabilidad C4.
- **Banish**: `repulsion = ApplyBanish(repulsion, artistEmb)`. `state = Banished`. **No revela** (se juzgó a ciegas — C3/C20 dependen de no saber qué se rechazó).
- **Again**: skip neutral, ni taste ni repulsion cambian. `state = Again`. No revela.
- Idempotencia de resolución: un rito ya resuelto → 409.

### Post-reveal y variantes

- **C4 explicabilidad** (en el reveal de Summon): `distance` (coseno banda↔taste), `sharedTags` (tags de la banda ∩ tags del grimorio), `sharedMembers` (miembros compartidos vía `member_of`). Datos ya cargados.
- **C3 segunda oportunidad**: la exclusión `NOT EXISTS` **deja volver lo desterrado a los 182 días** (`RiteEngine.SecondChanceAfter`). Served/Summoned/Again se excluyen siempre; Banished vuelve a ser elegible pasado el plazo.
- **C13 filtros duros**: `country`, `decadeFrom`, `decadeTo` en `serve`. **Formato NO** (no existe el campo). **Rank NO** (rank es null — elegir por rank renderizaría una mentira).
- **C27 adivina la década**: el reveal ya lleva `formedYear`, `country` y `tags` — todo lo que el marcador necesita. El marcador es UI (agente de ficha); no requiere endpoint nuevo.
- `GET /api/rite/grimoire`: las bandas invocadas (`state = Summoned`), reveladas, para la vista de grimorio (dato de C17).

### DTOs / contrato para el agente de front

`src/web/server/Dtos/RiteDtos.cs`. Ver §4 (contrato de endpoints).

### Refactor menor (sin cambio de contrato)

`ArtistDetailBuilder` extrae el mapeo de ficha de `ArtistsController.GetById` para reusarlo en el reveal. La respuesta de `GET /api/artists/{id}` es **idéntica** a antes (el front no se ve afectado).

---

## 2. Verificación (comando → salida real)

### Build + tests

```
dotnet build src/web/Grimoire.slnx -warnaserror   → 0 Advertencias, 0 Errores
dotnet test  src/web/Grimoire.slnx                → Superado: 99, Con error: 0, Omitido: 0
```

27 tests nuevos (eran 72): `TasteMathTests` (8: summon acerca el taste a la banda, banish acerca la repulsión, decay, nulos, rangos), `RingResolverTests` (7: Comfort más cerca que Abyss, ventana siempre subrango, radios desde muestra, safe radius), `PreviewAudioProxyTests` (2 teorías / 10 casos: allowlist SSRF acepta iTunes/Deezer https, rechaza host arbitrario, look-alike, http, relativo, null).

**Muerden**: al invertir `Assert.True(comfortHi < abyssLo, ...)` en `RingResolverTests.ResolveRadii` → `Con error: 1`; revertido, verde de nuevo.

### Motor de punta a punta contra la base viva

Base viva: 2478 artistas, 309 embeddings centrados, **80 servibles** (embedding+preview), `corpus_stats` con 1 fila. Registro → seed → serve → audio → summon:

```
1. register → accessToken (392 chars)
2. GET taste → {"hasTaste":false,"summonedCount":0,"updatedAt":null}
3. seed-candidates(5) → Absu, Accept, AC/DC, Agathocles, Alice in Chains
4. POST seed → {"hasTaste":true,...}
5. DB user_taste → emb_present=true repulsion_null=true
6. POST serve {comfort:0.6} → {"token":"5e4f43d9…","riskPercentile":0.58,"audioUrl":"…/audio"}
   blind check (name|country|tags|title en el DTO) → NONE
7. GET {token}/audio → HTTP 200, Content-Type audio/x-m4p, 1169102 bytes,
     file: "ISO Media, Apple iTunes ALAC/AAC-LC (.M4A) Audio"   (preview real proxiado; origen oculto)
8. POST resolve summon → {"state":"Summoned","name":"Abruptum","country":"SE",
     "distance":0.5036,"sharedTags":[],"releaseCount":15}
9. taste md5 before=7535365f… after=3722423c…  → TASTE CHANGED ✓
   rites → state=Summoned, risk=0.58, resolved=t
10. import-lastfm → HTTP 503 {"message":"Last.fm import is unavailable: no Last.fm API key…"}
```

Guardas y variantes:

```
BANISH → {"state":"Banished","revealNull":true}; user_taste.repulsion_present=true
Exclusión: banished artist never re-served over 15 serves ✓
C13 country=US (comfort 0.2/0.5/0.8) → served country=US, US, US ✓
Empty ring (decadeFrom 2200) → HTTP 204
Serve sin taste (usuario nuevo) → HTTP 409
Serve sin auth → HTTP 401
```

Base dejada limpia: los 6 usuarios de verificación borrados (cascade eliminó sus rites/taste). Corpus intacto: **2478 artistas, 80 servibles, 309 embebidos**, 0 rites, 0 user_taste.

### Gate

```
bash scripts/audit.sh --strict   → RESULT: PASS (Violations 0, Skipped 0)
```
Gates verdes: `dotnet-build`, `dotnet-test`, `pnpm-lint`, `pnpm-build` (front intacto).

---

## 3. Huecos declarados (y su porqué)

- **B15 Ranks / Depth Score: BLOQUEADO** sin key de Last.fm. `rank`/`listeners` siguen null; **no se fabrican**. La columna `depth_score` existe y queda en **0** — no se calcula desde rank (degrada con dignidad). El motor **no** usa el término de rareza del SQL de §6 (usa random dentro del anillo), porque `listeners` null lo haría una mentira.
- **C1 import Last.fm: hueco vivo pero declarado.** Adaptador real, flag apagado, endpoint 503. La ruta viva no se probó (no hay key). El path deshabilitado sí está testeado (503) y el mapeo por nombre es puro y testeable.
- **C2 duelo a ciegas: NO construido** este pase. Reusa embeddings existentes; Bradley-Terry sobre pares. Queda para un pase posterior — no cabía sin arriesgar los 1-5. El pipeline de serve/resolve es la base sobre la que montarlo.
- **Pool servible pequeño (80).** Con un slider estrecho o filtros duros, el anillo se vacía y `serve` devuelve **204** (estado vacío diseñado, no error). Es consecuencia de D25 (48 % insonorizable) a la escala del corpus sembrado, no un bug. El agente de front debe diseñar ese estado vacío.
- **`sharedTags`/`sharedMembers` vacíos al principio** es correcto: hasta que el grimorio tenga bandas, no hay con qué solapar. Se llenan según se invoca.
- **`abstract`/`labels` siguen sin poblar** (heredado del ETL) → el embedding es la cota pesimista que D26 anticipa. No afecta al motor, solo a la riqueza de la señal.

---

## 4. Contrato de endpoints (para el agente de front)

Todos bajo `/api/rite`, **`[Authorize]`** (JWT Bearer) salvo el de audio.

| Método | Ruta | Body / Query | Respuesta |
|---|---|---|---|
| GET | `/seed-candidates?limit=` | — | `[{id, name, country, formedYear}]` — pantalla de elegir bandas (NO a ciegas) |
| POST | `/seed` | `{artistIds: guid[]}` (1–20) | `TasteStatusDto {hasTaste, summonedCount, updatedAt}` |
| POST | `/import-lastfm` | `{username}` | `TasteStatusDto` · **503** si no hay key · 404 si no mapea |
| GET | `/taste` | — | `TasteStatusDto` — para saber si correr arranque en frío |
| POST | `/serve` | `{comfort:0..1, country?, decadeFrom?, decadeTo?}` | `ServedRiteDto {token, riskPercentile, audioUrl}` **a ciegas** · **204** anillo vacío · **409** sin taste |
| GET | `/{token}/audio` | — (**anónimo**, capability URL) | stream de audio (`audio/*`) · 404 sin preview |
| POST | `/{token}/resolve` | `{action: "summon"\|"banish"\|"again"}` | `ResolveResultDto {state, reveal?}` — `reveal` **solo en summon** |
| GET | `/grimoire` | — | `[{artist: ArtistSummaryDto, resolvedAt}]` — bandas invocadas |

`reveal` (solo summon) = `{ artist: ArtistDetailDto (misma forma que GET /api/artists/{id}), why: {distance, sharedTags[], sharedMembers[]} }`.

**Audio**: el `audioUrl` es absoluto (`http://host/api/rite/{token}/audio`). Es una URL de capacidad; un `<audio src>` la reproduce cross-origin sin cabecera de auth. **Nunca** se expone la URL de iTunes/Deezer al cliente.

---

## 5. Decisiones a promover a `DECISIONS.md`

> Ninguna contradice un invariante. Marcadas para que Pedro las ratifique como `D<n>`.

1. **Esquema del Rito.** `user_taste` con PK `user_id` (una fila por usuario; el grimorio no es tabla, es `rites WHERE state='summoned'`). `rites` con índice `(user_id, artist_id)`. Ambos vectores de `user_taste` se guardan **centrados** (nunca se re-centra — invariante D26 anotado en el código).
2. **El anillo, en la práctica.** Ventana de percentiles de ancho **0.20** que el slider desliza (comfort 0 → cercano, 1 → lejano). Radio seguro de repulsión = **p20** de la distribución de distancias a la repulsión (excluye el 20 % más cercano a lo desterrado). Muestreo del pool servible (`SampleSize` 2000, configurable en la sección `Rite`). Dentro del anillo se elige **al azar** (no por el término de rareza de §6, que exige `listeners`, hoy null).
3. **Proxy de audio como URL de capacidad.** El token es el `rite.Id` (GUID inadivinable); el endpoint de audio es **anónimo** para que un `<audio>` lo reproduzca. SSRF cerrado por allowlist de hosts + sin redirecciones + URL nunca del cliente.
4. **`Again` = skip neutral** (ni taste ni repulsión cambian), excluido de futuros serves. **C3**: lo desterrado vuelve a los **182 días**.
5. **C1 Last.fm** se entrega como adaptador `IColdStartImport` **con flag apagado**; sin key el endpoint devuelve **503** explícito. No se inventan scrobbles.
6. **`depth_score`** existe como columna pero queda en **0**: no se calcula sin rank (bloqueado por la key de Last.fm).
