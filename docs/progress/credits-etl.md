# Movimiento II — ETL Créditos / Sellos / Deaths (agente Credits-ETL)

> Estado: **en curso, verde**. Frontera del agente: SOLO `src/console/server/**` y `src/shared/**` (+ tests). **No se creó ninguna migración** (las tablas `credits`, `works`, `labels` ya existían de la ola Data-backbone). **No se tocaron los modelos de `src/shared/Models`** ni `src/web/server/**` ni `src/front/**`. `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones, 0 skips).

Tres tareas: (1) `credits`, (2) `labels`, (3) deaths-enrichment (url-rels sobre personas → re-run `deaths`). Todo ejecutado de verdad contra la base viva; counts reales abajo. MusicBrainz a **1 req/s estricto** (reusa `MusicBrainzRateLimiter` + `MusicBrainzClient`).

---

## 1. Verbos nuevos

Tres verbos añadidos al dispatcher de `Program.cs`, con el patrón de los existentes (`WorkerJob` one-shot). Todos **batched, resumibles, idempotentes** y **parciales-declarados**.

- **`credits`** — puebla `credits`. Por cada release-group nuestro pide **una** release concreta a MB con `inc=artist-credits+labels+recordings+artist-rels+recording-level-rels` (una sola llamada captura intérpretes, instrumentos, producción **y** el sello). Elige la mejor edición (Official → fecha más temprana → más relaciones). De las relaciones a nivel de grabación saca performers e instrumentos; de las release-level, producción.
- **`labels`** — puebla `labels` (`mbid, name`) y `releases.label_id` del label-info de la misma release. El JSON de release se **comparte por disco** con `credits`, así que sobre los release-groups ya cacheados **no hace ni una llamada a MB**. El `country` (que el label-info no trae) lo rellena una segunda pasada acotada de lookups `label/{mbid}`.
- **`personlinks`** — desbloquea C12. Hace `inc=url-rels` sobre las filas de miembro (`Person`) para que ganen su link `wikidata` en `artists.links`; luego se **re-ejecuta el verbo `deaths`** (ya existente, sin cambios).

### Lógica pura, testeada, en `src/shared`

- **`CreditResolver`** (shared, puro): mapea `(tipo-de-relación MB, atributos)` → facetas de crédito `(role, instrument, is_guest)`. `instrument`→performer+instrumento; `vocal`→performer+"vocals"/descriptor; `producer|engineer|mix|mastering`→su rol. **Miembro oficial ≠ invitado**: el atributo `guest` marca `is_guest=true` (no descarta el crédito — el crédito de invitado es real y va marcado). Atributos cualificadores (`original`, `additional`, `solo`, `minor`, `guest`) no se leen como instrumento. **Empareja por MBID**: `Resolve(...)` descarta al artista que no está en el corpus (nunca lo inventa).
- **`LabelResolver`** (shared, puro): valida `(mbid, name, country)` → `ResolvedLabel`, descartando mbid mal formado / nombre vacío; `First(...)` elige el primer label válido de un co-lanzamiento (mantiene `label_id` monovaluado).

### Infra de resumibilidad (worker)

- **`EtlCache`** — cache en disco del JSON de la release elegida, indexado por release-group MBID, **fuera del repo** (temp por defecto, override `GRIMOIRE_CACHE_DIR`). Compartido por `credits` y `labels` → una sola descarga sirve a los dos, y es de buena educación con MB.
- **`ProgressLedger`** — ledger append-only de MBIDs completados **por verbo** (`credits.done`, `labels.done`, `personlinks.done`). Distinto de la presencia en cache (que solo significa "JSON en disco"), porque un release-group puede tener 0 créditos legítimamente. Marcar **después** de confirmar la escritura en DB.

Créditos **directos de MB** → `source='musicbrainz'`, `confidence=1` (D9). No se generó ningún crédito inferido en esta pasada (no había intersección de intervalos que calcular aquí); el camino inferido queda para cuando lo pida el Gantt (iría con `source='inferred'`, `confidence<1`).

---

## 2. Verificación (comando → salida real)

```
dotnet build src/web/Grimoire.slnx -warnaserror   → 0 Advertencias, 0 Errores
dotnet test  src/web/Grimoire.slnx                 → Superado: 209, Con error: 0, Omitido: 0
bash scripts/audit.sh --strict                     → RESULT: PASS (0 violaciones, 0 skips)
```

### Tarea 1 — credits

```
GRIMOIRE_CREDITS_LIMIT=300 dotnet run --project src/console/server -- credits
  → Credits: 5320 release-groups, 10 already done, 5310 pending. Processing 300 ...
  → Credits complete: processed 300 release-groups (300 fetched, rest from cache),
    9511 credits written (57 as guests). 5010 release-groups still pending.
```
(Se corrieron dos smoke-batches de 5 antes → **310 release-groups procesados en total**.)

Estado en la base: `credits` = **9763 filas** sobre **310 release-groups**, **65 invitados** (roles: performer 8233, producer 1101, engineer 294, mix 131, master 4). Muestra real (Metallica en vivo, instrumento **por grabación**, `is_guest` correcto):
```
 Cliff Burton   | performer | electric bass guitar | is_guest=f
 James Hetfield | performer | electric guitar      | is_guest=f
 James Hetfield | performer | lead vocals          | is_guest=f
```
Invitados reales detectados (p.ej. Chrigel Glanzmann, flute/whistle en un disco ajeno) → `is_guest=t`.

**Idempotencia** comprobada: re-run del batch de 5 → "5 already done" y avanzó a los siguientes 5 (el ledger salta lo hecho). La escritura por release-group es `DELETE credits WHERE release_id=@rid` + insert fresco → re-procesar un grupo no duplica. El instrumento repetido por persona es real: una fila por `recording_id` distinto (una por pista).

### Tarea 2 — labels

```
GRIMOIRE_CREDITS_LIMIT=1 GRIMOIRE_LABEL_COUNTRY_LIMIT=200 dotnet run ... -- labels
  → Labels complete: processed 311 release-groups (1 fetched), 179 distinct labels,
    299 releases got a label_id, 134 label countries filled this pass
    (45 still without country). 5009 release-groups still pending.
```
Estado: `labels` = **179**, **134 con country**; **299 releases con `label_id`**. Solo 1 fetch a MB (el resto de los 311 grupos salió del cache de `credits`). Muestra real:
```
 Nuclear Blast | DE | 16 releases
 Century Media | DE | 12
 Peaceville    | GB |  8
 [no label]    | XW | 23   (entidad real de MB para autoeditados; no inventada)
```

### Tarea 3 — deaths-enrichment (desbloquea C12)

```
GRIMOIRE_PERSONLINKS_LIMIT=400 dotnet run ... -- personlinks
  → Person links complete: 400 people processed, 369 gained links,
    208 newly carry a Wikidata QID. 1768 people still pending.
dotnet run ... -- deaths
  → 210 people carry a Wikidata QID. Querying P570/P20 ...
  → Deaths complete: 5 batches, 24 death dates and 14 places written.
```
Antes de esta pasada `deaths` escribía **0** (solo 2 personas tenían QID). Ahora **371 personas con links**, **210 con QID**, y **24 fallecimientos escritos** (14 con lugar). Muestra real: Phil Lynott (1986, Salisbury), Eric Carr (1991, New York City), Peter Steele (2010, Scranton), Joe Strummer, Gar Samuelson… — datos verificables, ninguno inventado.

---

## 3. Counts finales en la base

| tabla / campo | antes | después |
|---|---|---|
| `credits` | 0 | **9763** (65 invitados, 310 release-groups) |
| `labels` | 0 | **179** (134 con country) |
| `releases.label_id` no-null | 0 | **299** |
| `Person` con `links` | 2 | **371** (210 con QID Wikidata) |
| `artists.death_date` no-null | 0 | **24** |

---

## 4. Pendiente declarado (con cuántos y por qué)

A 1 req/s estricto, una pasada completa son horas; se corrió un batch sustancial de cada uno y **se declara el resto** (los verbos son resumibles — re-ejecutar continúa donde quedó, saltando lo hecho vía ledger + cache en disco):

- **`credits`: 5010 release-groups pendientes** (de 5320; 310 hechos). `GRIMOIRE_CREDITS_LIMIT=N` procesa el siguiente lote. ~90 min más para el resto a 1 req/s.
- **`labels`: 5009 release-groups pendientes** para `label_id`. Se poblarán gratis a medida que `credits` cachee más grupos (labels reusa el cache); o `labels` con `GRIMOIRE_CREDITS_LIMIT` alto los baja él mismo.
  - **45 labels sin country**: MB no afirma país para esas (imprints/holdings, y `[no label]`). No es límite de presupuesto (se miraron los 179 ≤ 200) — es un hueco real de la fuente, no se inventa.
- **`personlinks`: 1768 personas pendientes** (de 2168; 400 hechas). `GRIMOIRE_PERSONLINKS_LIMIT=N` sigue. Cada tanda desbloquea más `deaths` — **conviene re-ejecutar `deaths` tras cada tanda de `personlinks`** (es barato, Wikidata).
- **`recording_id` en `credits`** — se guarda el UUID de grabación de MB como columna suelta (sin tabla Recording, sin FK — como dejó la migración de Data-backbone). Correcto según ese esquema.
- **Créditos solo release-level para producción / recording-level para intérpretes** — se toma **una** edición por release-group (la mejor). Otras ediciones podrían tener créditos distintos; bounded a propósito.

---

## 5. Ficheros tocados (todos dentro de frontera)

Nuevos (shared): `Services/CreditResolver.cs`, `Services/LabelResolver.cs`.
Nuevos (worker): `Credits/{EtlCache,CreditsOptions,CreditsJob,LabelsJob}.cs`, `PersonLinks/{PersonLinksOptions,PersonLinksJob}.cs`.
Nuevos (tests): `CreditResolverTests.cs` (12), `LabelResolverTests.cs` (7) — muerden (comprobado invirtiendo `is_guest`: falla; revertido, verde).
Modificados (worker, aditivo): `MusicBrainz/MusicBrainzModels.cs` (DTOs de release/label/recording), `MusicBrainz/MusicBrainzClient.cs` (`GetReleasesForCreditsAsync`, `GetLabelAsync`, `GetArtistLinksAsync`), `MusicBrainz/MbMapping.cs` (`MapLinks`), `Program.cs` (wiring de los 3 verbos).

**Modelos compartidos: NO tocados.** Ninguna migración creada.
