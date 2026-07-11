# Movimiento II — Listeners / Rank (agente Listeners)

> Estado: **terminado y verde**. `artists.listeners` poblado desde Last.fm y `artists.rank` derivado. Verificado contra la base viva. Frontera del agente: `src/console/server/**` y `src/shared/**` (+ tests en `src/web/GrimoireTest/`). **No** se tocó `src/web/server/**`, `src/front/**` ni se creó ninguna migración (las columnas ya existían). Fecha: 2026-07-11.

Desbloquea B15 (Ranks) y, con él, el término de rareza del motor (SPEC §6) que hasta ahora era null. El `LastFmColdStart` de `src/web/server` (C1) es de otro agente y no se tocó.

---

## 1. Qué se construyó

### Verbo nuevo `listeners` (`Program.cs`)

Dispatcher extendido a `seed|edges|previews|listeners|embeddings|stats`. `ConfigureListeners` lee la key de `LastFm:ApiKey` (user-secrets, con fallback a `GRIMOIRE_LASTFM_APIKEY`), registra un cliente HTTP resiliente (`AddPoliteHttpClient`, retry en 429/503) y la fuente. Sin key → fuente `Enabled = false` → el job no hace nada (Invariante 5 / D9 / bloqueador Q5).

### `LastFmEnrichmentSource` (console, detrás de `IEnrichmentSource`)

- **Emparejado por mbid, no por nombre.** Toda banda sembrada tiene mbid (es la clave del seed), así que la llamada es `artist.getInfo?mbid=<mbid>`: Last.fm devuelve **exactamente nuestra entidad** y las colisiones de nombre (el problema "Toto"/"Death" de D22) **no pueden ocurrir**. Si Last.fm no indexa ese mbid, el conteo queda **null** en vez de tomar prestados los oyentes de otra banda con el mismo nombre. Solo un artista sin mbid caería al fallback por nombre (`Resolve` + `NameMatch` + guarda de mbid contradictorio) — no ocurre en este corpus.
- **Rate limit ~5 req/s** vía `FixedCadenceRateLimiter(200 ms)` (mismo patrón que `MusicBrainzRateLimiter`, cadencia distinta).
- Errores tratados como huecos: 404 / error 6 de Last.fm ("artist not found") → null silencioso; 5xx/red → warning + null. Nunca se enmascara ni se inventa.

### `LastFmListeners` (shared, puro y testeado)

Parsing y verificación fuera de la capa HTTP para poder testear sin red:
- `ParseListeners(response)` — camino by-mbid: la identidad la garantiza la query, así que solo parsea `stats.listeners` (string→int), tratando error/nulo/inparseable como null.
- `Resolve(response, name, mbid)` — fallback por nombre: exige `NameMatch` y rechaza si el mbid devuelto contradice el nuestro.
- DTOs (`LastFmArtistInfoResponse`/`Artist`/`Stats`) con `JsonPropertyName`. Autocontenido, sin tocar el `LastFmColdStart` del web.

### `ListenersJob` (console, `WorkerJob`)

- **Candidatos = bandas sembradas** (`Tags <> {} OR tiene releases`), no las filas mínimas de miembro (personas que la búsqueda de banda de Last.fm no resolvería — D25). Mismo criterio que `PreviewJob`.
- **Resumable e idempotente**: procesa solo candidatos con `listeners IS NULL`, hasta `Listeners:Limit` (default 500, override `GRIMOIRE_LISTENERS_LIMIT`). Re-ejecutar continúa; una columna es upsert, nunca duplica.
- Por cada match: `listeners = valor` y `rank = RankCalculator.FromListeners(valor)` en la misma escritura, así rank y listeners nunca discrepan. `RankCalculator` ya existía y estaba testeado (umbrales SPEC §6). Donde `listeners` queda null, `rank` queda null (no se fuerza).

### Extensión de contrato

`ArtistEnrichment` gana `int? Listeners` (aditivo; `PreviewJob` no lo lee, no le afecta).

---

## 2. Verificación (comando → salida real)

### Build + tests

```
dotnet build src/web/Grimoire.slnx -warnaserror   → 0 Advertencias, 0 Errores
dotnet test  src/web/Grimoire.slnx                → Correctas: 138, Con error: 0, Omitido: 0
```
16 tests nuevos (`LastFmListenersTests`): parseo (válido, error 6, null, inparseable, cero como valor real), emparejado por nombre (match con/ sin diacríticos, banda equivocada "Toto" rechazada, mbid contradictorio rechazado), y las fronteras de rank ya cubiertas por `RankCalculatorTests`. **Muerden**: al invertir la guarda `!NameMatch.Matches` → **5 con error**; revertido → verde.

### Job contra la base viva

```
BEFORE: candidates 307 | with_listeners 0 | with_rank 0
DOTNET_ENVIRONMENT=Development dotnet run --project src/console/server -- listeners
  → 307 artists pending listener resolution (of 307 candidates).
  → Listeners batch complete: 290/307 resolved a listener count. Re-run to continue.
AFTER:  with_listeners 290 | with_rank 290 | candidate_nulls 17 | mismatched (rank≠listeners) 0
```

Distribución de rank (los cinco tiers poblados, fronteras de SPEC §6 correctas):
```
   rank    | count |  min   |   max
-----------+-------+--------+---------
 Nameless  |    15 |      2 |     432     (<500)
 Forgotten |    28 |    564 |    4961     (500–5k)
 Hidden    |    67 |   5101 |   46353     (5k–50k)
 Obscure   |   104 |  50394 |  499023     (50k–500k)
 Known     |    76 | 503490 | 7353789     (>500k)
```
Muestra: `Red Hot Chili Peppers` 7.35M → Known; `Indra's Arrow` 2, `Beorn's Hall` 10 → Nameless.

**Resumable / idempotente**: re-ejecutar → "17 artists pending" (solo los nulls, no los 307), 0/17 resueltos, counts intactos (290/290). Sin duplicados, sin caídas.

### Nota de diseño: by-mbid > by-name (medido)

Una primera pasada por **nombre** resolvió 296/307, pero incluía riesgo de banda-equivocada: p. ej. `Avenger`, `Castle`, `Stillborn` — nombres comunes cuyo resultado más popular en Last.fm **no es** nuestra entidad. Al cambiar a **by-mbid** (query `?mbid=`), Last.fm devuelve exactamente nuestra banda: `Avenger` (CZ) → 27 929, `Castle` → 21 782, `Stillborn` → 12 922, todos correctos por construcción. El precio: 290 en vez de 296, porque 6 bandas famosas (`KISS`, `LOUDNESS`) tienen en Last.fm un mbid distinto del nuestro y by-mbid las deja null antes que atribuir los oyentes de otra entidad. Es el trade correcto para una app cuyo corazón es el underground (D25: "mejor null que la banda equivocada"; y la rareza es inversa a la popularidad — perder un `Known` cuesta poco).

### Gate

```
bash scripts/audit.sh --strict   → RESULT: PASS (Violations 0, Skipped 0)
```
Gates verdes: `dotnet-build`, `dotnet-test`, `pnpm-lint`, `pnpm-build`.

### Seguridad de la key

La `LastFm:ApiKey` (32 chars) se copió del user-secrets del web al del console (`dotnet user-secrets init/set`). `git grep` de su valor sobre los ficheros trackeados → **no aparece**. El único cambio en el csproj es el `UserSecretsId` (un GUID, no la key). El job requiere `DOTNET_ENVIRONMENT=Development` para que el host cargue user-secrets.

---

## 3. Huecos declarados (y su porqué)

- **17 candidatos quedan null** (`Bølzer, ChthoniC, Horns of Domination, KAT, King Gizzard & the Lizard Wizard, KISS, LOUDNESS, Murmur Mori, NothingNew, Osi and the Jupiter, Phillip Boa and the Voodooclub, SKÁLD, Today Is the Day, Ultra Vomit, Vidres a la sang, VoidKeeper, VolsungaSaga`). Motivo: Last.fm no indexa nuestro mbid exacto para esas entidades (a menudo su mbid difiere del canónico de MusicBrainz, dato notoriamente rancio en Last.fm). Bajo by-mbid + D25 se prefiere el null honesto a tomar el conteo de una banda del mismo nombre. `SKÁLD` (ancla folk, D23) es una pérdida real pero honesta. Un fallback por nombre rescataría algunas, pero reintroduce el riesgo de banda-equivocada para nombres comunes; no se hace.
- **2168 filas de tipo persona (miembros)** no son candidatas (sin tags ni releases) — se saltan limpio, como en `PreviewJob`. Last.fm es de bandas, no de músicos individuales en este flujo.
- **Marcador de "intentado" = `listeners IS NULL`.** No hay columna para distinguir "no intentado" de "intentado sin match" (no se crean migraciones). Consecuencia: una re-ejecución reintenta los 17 nulls (barato: ~4 s a 5/s). Idempotente igualmente (upsert de columna). Si algún día molesta, un marcador exigiría migración.
- **`depth_score` no se calcula aquí** — es del lado web (otro agente). Este pase solo puebla `listeners` y `rank`, que es lo que ese cálculo necesitaba.
