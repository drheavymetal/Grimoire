# Movimiento VII — Datos de clásica (agente VII-Datos Clásica)

> Estado: **completado y verde**. Frontera del agente: `src/shared/**`, `src/console/server/**`, tests, y **una** migración aditiva. No se tocó `src/web/server/**` ni `src/front/**`. `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones, 0 skips, 343 tests).

La clásica es otro modelo (D11): no hay formación, hay **obra** (compositor) e **interpretación**, y el linaje que MB documenta mejor es **teacher/student** entre personas. El corpus era metal/folk puro: no había ni un compositor. Este pase los siembra.

---

## 1. Verbo nuevo — `classical`

Un solo verbo que hace las cuatro cosas en secuencia, cada llamada a MusicBrainz por el limitador compartido a **1 req/s** (reusa `MusicBrainzClient`/`MusicBrainzRateLimiter`):

1. Resuelve una lista curada de compositores por **coincidencia exacta de nombre no ambigua** (`kind = Person`).
2. Puebla `works` con sus obras, enlazadas al compositor por `works.composer_id`.
3. Crea aristas `Teacher`/`Student` entre compositores del corpus a partir de las `teacher` artist-rels de MB.

`dotnet run --project src/console/server -- classical` (idempotente, resumible). Límite de obras por compositor: `Classical:WorksPerComposer` / `GRIMOIRE_CLASSICAL_WORKS` (default 100).

La **influencia P737** (tarea 4, opcional) no necesitó verbo nuevo: los compositores traen su QID de Wikidata (vía `GetArtistAsync` con url-rels), así que el verbo existente `influence` los recoge.

---

## 2. Counts reales (contra la base viva)

```
classical  → 23/25 compositores resueltos y upserted
           → 2291 works (2291 con composer_id — 100% enlazadas)
           → 12 aristas teacher/student (6 Teacher + 6 Student = 6 relaciones pedagógicas)
influence  → 13 aristas influenced_by nuevas entre compositores (67 → 80 en total)
```

Tras el pase: `artist_edges` = 2342 MemberOf · 80 InfluencedBy · 6 Teacher · 6 Student. `works` = 2291 (antes 0). 412 works con `kind`, 1879 con `kind` null (MB no da tipo → null, no se inventa).

### Las 6 relaciones maestro-discípulo (reales, de MB)

```
Joseph Haydn      → Ludwig van Beethoven
Gabriel Fauré     → Maurice Ravel
Gabriel Fauré     → Nadia Boulanger
Nadia Boulanger   → Philip Glass
Arnold Schönberg  → Anton Webern
Arnold Schönberg  → Alban Berg
```

Cada relación se materializa como **dos aristas dirigidas**: `Teacher` (maestro→discípulo, "enseñó a") y `Student` (discípulo→maestro, "estudió con"). Así la ficha lista tanto "enseñó a" (`From = self, Kind = Teacher`) como "estudió con" (`From = self, Kind = Student`) con un único índice. Es una cadena real: Fauré→Boulanger→Glass es un linaje de tres generaciones.

### Idempotencia comprobada

Re-ejecución del verbo → `0 works inserted, 0 teacher/student edges inserted`, 23 compositores upserted (update in-place). Los índices únicos `ix_artists_mbid`, `ix_works_mbid` y `ix_artist_edges_from_id_to_id_kind` hacen imposible un duplicado.

---

## 3. Cómo se asoció work ↔ compositor — `works.composer_id` (una migración aditiva)

La tabla `works(id, mbid, title, kind)` que dejó la ola de data-backbone **no tenía ninguna referencia a un artista**, así que no había forma de que la ficha de un compositor listara sus obras. La frontera del brief decía "NO migraciones" bajo el supuesto (explícito en el paréntesis) de que `works` ya existía y bastaba; el texto de la tarea preveía justo este caso y autorizaba la excepción: *"o añade la columna aditiva mínima — sin migración destructiva, y decláralo"*.

**Declarado**: se añadió **una** migración estrictamente aditiva y no destructiva, `20260711030149_AddWorkComposer`, que solo hace:
- `works.composer_id uuid null` — FK a `artists(id)` `ON DELETE SET NULL` (una obra sobrevive si su compositor desaparece);
- índice `ix_works_composer_id` (para que la ficha liste las obras de un compositor barato).

Nada más (el modelo estaba en sync, verificado con `has-pending-model-changes` antes y con el diff de la migración después). Un compositor es un `Artist` con `kind = Person`. Una obra co-acreditada a varios compositores se atribuye **al primero que la importa** (`works.mbid` es único global) — declarado en `Work.cs` y en el job.

> Nota para el dueño de migraciones: esta es la única migración de esta ola VII; el agente de la ficha trabajó `src/web`/`src/front` sin migraciones, así que no hubo dos creadores concurrentes.

---

## 4. Cambios en shared (aditivos)

- **`Models/Enums.cs`** — `EdgeKind`: añadido **`Student`** (`Teacher` ya existía). Son texto en la BD → sin migración por el enum.
- **`Models/Work.cs`** — añadido `Guid? ComposerId` + nav `Artist? Composer`.
- **`Data/GrimoireDbContext.cs`** — mapeo del FK `Work.Composer` + índice.
- **`Services/TeacherStudentResolver.cs`** (puro, testeado) — traduce una `teacher` artist-rel de MB a un par canónico (maestro, discípulo). Semántica de dirección **verificada en vivo**: querying Beethoven, Haydn aparece como `teacher` con `direction=backward` (Haydn le enseñó). `forward` ⇒ el consultado es el maestro; `backward` ⇒ es el discípulo.
- **`Services/ComposerResolver.cs`** (puro, testeado) — elige un único MBID de Person por coincidencia exacta (case-fold + sin diacríticos, vía `NameMatch.Normalize`) contra nombre, sort-name **o alias**; 1 Person → Resolved, 0 → NotFound, 2+ → Ambiguous. Nunca adivina (disciplina de las anclas D23).
- **`Services/WorkMapper.cs`** (puro, testeado) — MB work → `Work`; rechaza MBID no-GUID o título vacío; `type` ausente → `kind` null (no se inventa).

En console: `MusicBrainzClient.GetWorksForArtistAsync` (browse `work?artist=`), `WorkBrowseResponse`/`MbWork`/`MbAlias` en los modelos, `Classical/ClassicalJob.cs`, `Classical/ClassicalOptions.cs`, y el verbo en `Program.cs`.

**Tests que muerden** (30 nuevos): `TeacherStudentResolverTests` (10), `ComposerResolverTests` (8), `WorkMapperTests` (12). Comprobado que muerden: al invertir la semántica de dirección en producción (`forward`↔`backward`), fallan 3 tests de dirección; revertido, verde.

---

## 5. Compositores ambiguos / saltados (no adivinados)

De 25 curados, **23 resueltos, 2 saltados** honestamente:

- **Richard Wagner** — `Ambiguous`: MB tiene 2+ Persons con ese nombre exacto. Se salta, no se elige uno.
- **György Ligeti** — `Ambiguous`: idem.

### El emparejado y las grafías nativas de MB

MB guarda a varios compositores bajo su **grafía/orden nativo**, con la forma común solo como alias, y el `artist:"…"` de la búsqueda casa contra el **campo nombre**, no los alias. Por eso la lista curada usa la forma que MB indexa como nombre primario:

- **Chopin** → `Fryderyk Chopin` (polaco), no "Frédéric".
- **Schönberg** → `Arnold Schönberg` (ö, no "oe" — el folding de diacríticos no cruza ö↔oe).
- **Bartók** → `Bartók Béla` (orden húngaro apellido-nombre).
- **Stravinsky** / **Shostakovich** → nombre primario **en cirílico** (`Игорь Фёдорович Стравинский` / `Дмитрий Дмитриевич Шостакович`): no tienen alias latino exacto ("Strawinsky"/"Stravinskij" ≠ "Stravinsky"). Es coincidencia exacta contra el nombre canónico de MB, honesta, no adivinación.

El `ComposerResolver` casa además contra alias y sort-name (más robusto), aunque en la práctica la búsqueda `artist:` obliga a dar la forma-nombre para que el candidato vuelva.

---

## 6. Huecos declarados (y su porqué)

- **`works.kind` null en 1879/2291.** MB no asigna tipo a muchas obras (movimientos sueltos, WoO, catálogos Hess). Null = ausente en MB, nunca inventado.
- **Una sola página de obras por compositor** (`WorksPerComposer=100`). Los canónicos tienen miles (Bach 8400, Mozart 5513). 100 por compositor da sustancia a la ficha sin traer el catálogo entero. Subir el límite (o paginar) es cambiar un número; el job no cambia.
- **El rank sigue mintiendo para clásica** (D11): `listeners` de Last.fm por MBID no se corrió para estos compositores; aunque se corriera, los tiers de clásica son ruido. No se usó para nada aquí.
- **No hay `member_of` para compositores** — correcto: la clásica no tiene formación. El Gantt no aplica (D11). Su linaje es teacher/student + influencia.
- **Embeddings de los compositores**: no se corrió `embeddings` sobre ellos en este pase. Traen nombre/tags/país/links; el `EmbeddingTextBuilder` los acepta sin cambio. Correr `embeddings` los indexaría (The Rite y Bloodline funcionan igual o mejor en clásica — D11).

---

## 7. Qué necesita la ficha de compositor (otro agente, `src/web` + `src/front`)

- **Obras de un compositor**: `works WHERE composer_id = @id` (índice `ix_works_composer_id`). Agrupar por `kind`; tratar `kind = null` como "sin clasificar", no ocultar.
- **Linaje maestro-discípulo**:
  - "Enseñó a": `artist_edges WHERE from_id = @id AND kind = 'Teacher'` → `to_id`.
  - "Estudió con": `artist_edges WHERE from_id = @id AND kind = 'Student'` → `to_id`.
  - Ambas direcciones existen por diseño; una sola consulta indexada por cada una.
- **Influencia** (P737): `artist_edges WHERE from_id = @id AND kind = 'InfluencedBy'` (ya poblado, 80 aristas, 13 entre compositores).
- **La ficha de compositor NO es la de banda** (D11): sin Gantt, sin miembros, sin rank. El héroe es la lista de obras + los dos linajes (teacher/student e influencia).
