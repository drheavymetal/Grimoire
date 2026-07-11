# Movimiento II — C2 (duelo a ciegas) y C27 (adivina la década)

> Estado: **cerrado y verde**. Duelo por pares (Bradley-Terry) que mueve el gusto hacia el ganador y
> lo aleja del perdedor, y el juego de la década con marcador de sesión, ambos como variantes del
> Rito reusando serve/reveal, el vector de gusto, el proxy de audio anti-leak y la firma tipográfica
> por rank. Frontera respetada: **solo `src/web/server/**` y `src/front/**` (+ tests)**. Cero
> migraciones, cero `src/shared/**`, cero `src/console/**`. Fecha: 2026-07-11.

Complementa `rite-engine.md` (motor), `rite-front.md` (UI del Rito) y `e2e.md`.

---

## 1. Qué se construyó

### C2 — Duelo a ciegas (SPEC §5.7, D16)

Dos bandas del pool servible servidas **a ciegas**; el usuario elige una. La preferencia por pares
(Bradley-Terry) enseña el vector más que un like suelto: tira **hacia el ganador** y lo **aleja del
perdedor**, con decay (ganador = señal fuerte, decay 0.25 como un summon; perdedor = empuje suave
0.10). **No re-centra** (embeddings ya centrados, D26 — `DuelMath` solo mezcla vectores centrados).

- **`DuelMath.ApplyDuel(taste, winner, loser, wWin=0.25, wLose=0.10)`** (`src/web/server/Services/DuelMath.cs`),
  función pura: `toward = (1−wWin)·taste + wWin·winner`, luego `result = toward + wLose·(toward − loser)`.
  Con `taste` null arranca del ganador. Valida pesos en (0,1] y dimensiones.
- **`RiteEngine.FindManyAsync(count)`**: refactor que extrae la consulta del anillo a `RingAsync` y
  saca **N bandas distintas** del mismo anillo por el mismo sorteo ponderado por rareza sin
  reemplazo (Gumbel-max, D35). El duelo pide 2. Reusa exclusión de lo riteado, ventana de
  percentiles y resta de repulsión intactas.
- Estados (reusa el enum `RiteState` de shared, sin tocarlo): ambos lados → `Served`; al elegir,
  **ganador → `Summoned`** (entra al grimorio, se revela con C4 + depth score), **perdedor →
  `Again`** (visto, excluido de futuros serves, **no** desterrado: no se rechazó, solo se prefirió;
  no toca la repulsión). El movimiento del gusto lo hace `DuelMath`, no la semántica del estado.
- Purga D39 reusada: `PurgeAbandonedServedAsync` corre **tras** `FindManyAsync`, así el par recién
  abandonado no se re-sirve el mismo turno.

### C27 — Adivina la década (SPEC §5.7)

Variante del Rito con marcador: 45 s a ciegas, el usuario **apuesta año/década, país y subgénero**,
luego se revela y se puntúa contra el dato real (`formed_year`/`country`/`tags`). Entrena el oído.

- **Pool puntuable**: `RiteEngine.FindAsync(..., scorableOnly:true)` estrecha el pool servible a
  bandas con `formed_year`, `country` y `tags` no vacíos — **cada dimensión se juzga contra un dato
  real, nunca inventado**. La muestra de percentiles usa el mismo pool puntuable.
- **`DecadeScore.Score(guess, truth)`** (`src/web/server/Services/DecadeScore.cs`), función pura:
  - Década: exacta = Hit (2), ±1 década = Close (1), resto = Miss (0). `formed_year` null → Miss.
  - País: igualdad case/whitespace-insensitive = Hit (1), resto Miss. Sin apuesta o truth null → Miss.
  - Subgénero: solape de token en cualquier sentido (la apuesta es un tag, o palabra dentro de un
    tag, o el tag es palabra dentro de la apuesta), case-insensitive = Hit (1). Vacío/sin tags → Miss.
  - Total por ronda: máx 4.
- **El juego NO mueve el gusto** (una apuesta no es una preferencia): al puntuar, la banda → `Again`
  (vista, excluida, revelada), sin cambio de `taste`/`repulsion` ni snapshot. Verificado en vivo.
- **Marcador acumulado en sesión** por el front (reductor puro `addRound`, sin persistencia, sin
  migración). El backend es stateless por ronda.

### DTOs (`src/web/server/Dtos/DuelDecadeDtos.cs`)

`DuelRequest`, `DuelSideDto`, `DuelServedDto`, `DuelResolveRequest`, `DuelResultDto` (reusa
`RiteRevealDto`), `DecadeServeRequest`, `DecadeServedDto`, `DecadeGuessRequest`, `DecadeDimensionDto`,
`DecadeScoreDto`.

### Front (`src/front/**`)

- `core/domain/types.ts`: `DuelSide/DuelServed/DuelResult`, `DecadeServed/DecadeGuess/DecadeDimension/DecadeScoreResult`, `GuessOutcome`.
- `core/domain/decade.ts` (NUEVO, puro): `addRound` (reductor de marcador), `decadeOptions`, `EMPTY_SCOREBOARD`.
- `core/api/client.ts`: `duel`, `resolveDuel`, `serveDecade`, `guessDecade` (204 → null en los serve).
- `core/hooks/useDuel.ts`, `useDecade.ts`: TanStack Query; el duelo invalida `taste`+`grimoire`, la década no invalida nada (marcador local).
- `ui/rite/RevealCard.tsx` (NUEVO): tarjeta de reveal compartida (nombre que se revela con `RevealName`, corte base de Redaction, origen, tags, C4, link a ficha). `RiteConsole` refactorizado para usarla; `DuelConsole` la reusa para el ganador.
- `ui/rite/RiteGate.tsx` (NUEVO): el guard de tres puertas (anónimo→AuthPanel, sin gusto→ColdStart, con gusto→children) extraído de `RitePage` y reusado por las tres superficies del Rito.
- `ui/rite/DuelConsole.tsx`, `ui/rite/DecadeConsole.tsx` (NUEVOS): pantallas de duelo y década, dos reproductores a ciegas / apuesta + marcador, estados vacíos 204 diseñados.
- `ui/pages/DuelPage.tsx`, `DecadePage.tsx` (NUEVOS); `RitePage.tsx` refactorizado a `RiteGate`.
- `ui/routes.tsx` (`/duel`, `/decade`), `ui/Layout.tsx` (nav Duel/Decade), links desde `RiteConsole`.
- `locales/en.json`+`es.json`: `nav.duel/decade`, `rite.toDuel/toDecade`, y catálogos completos `duel.*` y `decade.*` en **ambos** idiomas.

**Anti-leak**: en ambos, nada de nombre/país/portada/género antes de elegir/apostar; el audio siempre
por la capability URL proxiada (`/api/rite/{token}/audio`). El país/subgénero de C27 solo se revela
tras apostar. Reproductores a ciegas via el `AudioAdapter` de `platform/` (invariante 6).

---

## 2. Endpoints (todos bajo `/api/rite`, `[Authorize]`)

| Método | Ruta | Body | Respuesta |
|---|---|---|---|
| POST | `/duel` | `{comfort, country?, decadeFrom?, decadeTo?}` | `DuelServedDto {left, right}` a ciegas · **204** anillo <2 · **409** sin gusto |
| POST | `/duel/resolve` | `{winnerToken, loserToken}` | `DuelResultDto {reveal}` · **400** tokens iguales · **404** no existe · **409** ya resuelto |
| POST | `/decade` | `{comfort}` | `DecadeServedDto {token, audioUrl}` a ciegas · **204** sin puntuable · **409** sin gusto |
| POST | `/{token}/guess` | `{decade, country?, subgenre?}` | `DecadeScoreDto {artist, decade, country, subgenre, totalPoints, maxPoints}` · **404** / **409** |

El audio reusa `GET /{token}/audio` (anónimo, capability URL) sin cambios.

---

## 3. Verificación (comando → salida real)

### Gates y suites

```
dotnet test src/web/Grimoire.slnx        → Superado: 383, Con error: 0   (+26: DuelMath 8, DecadeScore 18)
cd src/front && pnpm test                → 96 passed (13 files)          (+10: decade.test 4, duelDecadeClient 6)
cd src/front && pnpm e2e                 → 41 passed                     (+2: duel.spec, decade.spec; los 39 previos verdes)
bash scripts/audit.sh --strict           → RESULT: PASS (Violations 0)
```

**Muerden** (mutación → fallo → revert):
- `DuelMath`: quitar el empuje contra el perdedor (`result[i]=toward[i]`) → falla `ApplyDuel_SeparatesWinnerFromLoser_MoreThanSummoningTheWinnerAlone`.
- `DecadeScore`: cambiar `gap==10` por otro valor → fallan `Decade_OneDecadeOff_IsClose`, `Decade_DecadeBoundary…`, `Total_CloseDecadePlusCountry…`.
- Revertido → 383 verde.

### En vivo contra la base viva (API :5080, Postgres :5433, 80 servibles, 79 puntuables)

**Duelo — el gusto se mueve hacia el ganador y se aleja del perdedor** (distancia coseno del gusto
antes vía snapshot de seed, después vía `user_taste`):

```
duel → left/right tokens; winner=Aurora Borealis, depth=3
WINNER  before=1.01699 after=0.49627  -> TOWARD (ok)
LOSER   before=1.01771 after=1.23171  -> AWAY   (ok)
winner state=Summoned | loser state=Again
snapshots 1 → 2 (el duelo movió el vector: snapshot C16)
```

**Década — puntúa un acierto y un fallo reales**:

```
HIT  round: truth 1988 US 'death metal'  → decade hit 2 | country hit 1 | subgenre hit 1 | 4/4  (Cannibal Corpse)
MISS round: truth 1991 DE (apuesta 4 décadas off, país ZZ, sub inexistente) → 0/4
re-guess del token resuelto → HTTP 409
snapshots = 1 (el juego NO mueve el gusto: entrenar el oído no es preferencia)
band consumida → state=Again
```

Base dejada limpia: `0 usuarios, 0 rites, 0 user_taste, 0 taste_snapshots`. Corpus intacto (2501
artistas, 80 servibles). Puertos liberados por pid.

---

## 4. Huecos declarados (y su porqué)

- **Reveal del ganador aterriza en el corte BASE de Redaction**, no en un corte por rank — misma
  prohibición literal que el Rito (rank existe hoy, pero se reusa `RevealName` que ya mapea el corte
  por rank cuando lo hay; cuando el rank es null cae a BASE, nunca a una mentira). Sin novedad.
- **Emparejado laxo de tokens del duelo**: `duel/resolve` valida que ambos tokens sean rites
  `Served` del propio usuario y distintos, pero **no** liga los dos como un par formal (no hay
  columna de grupo → exigiría migración, fuera de frontera). Para un puñado de amigos es aceptable;
  la purga D39 evita fugas. Anotado por si se endurece más adelante.
- **El juego de la década consume del pool del Rito**: puntuar marca la banda `Again`, así que sale
  del anillo del Rito principal también. Es correcto (ya la oíste revelada) y consistente con jugar
  el Rito; con el pool puntuable pequeño (79) el juego puede agotarlo → 204 (estado vacío diseñado).
- **Marcador de la década solo en sesión** (front). Persistirlo exigiría tabla/migración (fuera de
  frontera) y el brief lo permite así.
- **Sin degradación tipográfica nueva**: se reusa la firma por rank existente (D38) en el reveal; no
  se cablea nada nuevo.
