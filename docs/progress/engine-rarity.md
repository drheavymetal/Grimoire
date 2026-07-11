# Movimiento II — Motor de rareza + Depth Score (agente Motor-Rareza/Depth)

> Estado: **terminado y verde**. Se encendió el término de rareza en el ordenado del anillo (SPEC §6) y se implementó el Depth Score (B15). Verificado contra la base viva (que el agente ETL ya pobló con `listeners`/`rank`). Frontera del agente: **solo `src/web/server/**` + sus tests**. No se tocó `src/shared/**`, `src/console/**`, `src/front/**` ni se crearon migraciones. Fecha: 2026-07-11.

Este documento registra qué se construyó, la fórmula del Depth Score, el tratamiento de null en el término de rareza (con su porqué), la verificación (comando → salida real), los huecos, y las decisiones a promover a `DECISIONS.md`.

---

## 1. Qué se construyó

### Término de rareza en el ordenado del anillo (SPEC §6, supersede la cláusula de D31)

`src/web/server/Services/RaritySelector.cs` (clase pura, testeada, en la frontera del agente — no en `shared`).

- **`RarityTerm(int? listeners, double weight)`** = `ln(1e6 / GREATEST(listeners,1)) * weight` (la fórmula literal de §6). Rareza inversa a la popularidad: menos oyentes → término mayor → más probable de salir. Con `listeners` poblado es exactamente el término del SQL de §6.
- **`SelectIndex(rarityTerms, nextUnit)`** — elige **uno** del anillo con **muestreo Gumbel-max**: `argmax_i (rarityTerm_i + g_i)` con `g_i = -ln(-ln(u_i))`, `u_i` uniforme en (0,1). Esto selecciona la banda `i` con probabilidad `∝ exp(rarityTerm_i)`.

**El término reordena DENTRO del anillo, no lo sustituye** (elección documentada, exigida por la tarea). El anillo de percentiles (D26/D31) sigue mandando: fija la banda de distancia. El término de rareza solo pondera la elección dentro de esa banda, como un **sorteo ponderado**, no como un `ORDER BY rareza DESC LIMIT 1`. Motivo: en una franja fina de percentiles ordenar determinísticamente por rareza colapsaría la variedad (serviría siempre la misma banda, la más rara del anillo) y mataría la exploración aleatoria-dentro-del-anillo que era el diseño de D26/D31. El Gumbel-max preserva esa exploración mientras sesga hacia lo raro. Con `RarityWeight = 0` todos los términos son 0 → sorteo uniforme → se recupera exactamente el comportamiento pre-D31.

**Peso ajustable**: `RiteEngineOptions.RarityWeight` (default `RaritySelector.DefaultRarityWeight = 0.15`), enlazable desde la sección `Rite` de configuración como el resto de tunables del motor.

**Integración en `RiteEngine.FindAsync`**: el paso final ya no es `ORDER BY random() LIMIT 1` en SQL. Ahora la consulta del anillo (con filtros duros, ventana de percentiles y resta de repulsión, todo server-side) proyecta `{ Id, Distance, Listeners }` y se materializa; el `RaritySelector` hace el sorteo ponderado en memoria con una fuente de aleatoriedad inyectada (`Random.Shared`, acotada a (0,1) para que el Gumbel no explote). El anillo es un subconjunto acotado del pool servible (una franja de percentiles), así que traerlo a memoria es barato y hace el sorteo testeable con una función pura.

### Tratamiento de null en el término de rareza — el punto crítico

**Un `listeners` null devuelve término `0.0` (NEUTRO), nunca uno enorme.** Porqué, literal:

- Muchísimas bandas tendrán `listeners` null (no están en Last.fm — justo la cola oscura). `GREATEST(NULL,1)` y `ln(NULL)` convertirían a esas bandas en «infinitamente raras» que **ganarían siempre** el sorteo — lo contrario de lo que se quiere, porque serían las bandas de las que menos sabemos las que dominarían la selección.
- Un null **no es «rarísimo»; es «desconocido»**. Un término neutro `0` hace que una banda sin dato pese **igual que una banda justo en 1e6 oyentes** (`ln(1e6/1e6)=0`): compite solo en el sorteo aleatorio y **nunca** se apodera de la selección.
- Ordenación resultante del término: banda genuinamente rara (positivo) **>** desconocida (0) **>** banda mega-popular >1e6 (negativo). El null queda en el medio neutro, ni lo más raro ni lo menos.

Esto está cubierto por tests que **muerden**: invertir el `return 0.0` a un valor grande rompe 5 tests (comprobado).

### Depth Score (B15) — `user_taste.depth_score`

`src/web/server/Services/DepthScore.cs` (clase pura, testeada).

- **`Points(Rank? rank)`**: `Nameless=5, Forgotten=4, Hidden=3, Obscure=2, Known=1`, y **`null → 0`**. Mide cuán lejos ha llegado el usuario (rareza), no cuánto escucha. Un rank null no puntúa: no se inventa un rank ausente (D33).
- **`Compute(IEnumerable<Rank?>)`** = suma de `Points` sobre las bandas invocadas.

**Recálculo en cada summon** (endpoint `POST /api/rite/{token}/resolve`): al invocar, se recalcula el Depth Score sobre **todas** las bandas en `state='summoned'` del usuario (incluida la recién invocada) y se persiste en `user_taste.depth_score` en el mismo `SaveChanges` que el cambio de estado del rito. Se recalcula desde el conjunto autoritativo (no incremental) para no depender de invariantes de no-doble-conteo.

**Expuesto** en:
- `GET /api/rite/taste` → `TasteStatusDto.DepthScore` (también en las respuestas de `seed`/`import-lastfm`/`taste`, que comparten el DTO).
- El reveal del summon → `RiteRevealDto.DepthScore` (el Depth Score del usuario tras esa invocación, para que la pantalla de reveal muestre cuán lejos ha llegado).

Ambos son campos nuevos añadidos a DTOs existentes (aditivo, compatible con el front). Ninguna migración: la columna `depth_score` ya existía (D30).

---

## 2. Verificación (comando → salida real)

### Build + tests

```
dotnet build src/web/Grimoire.slnx -warnaserror   → 0 Advertencias, 0 Errores
dotnet test  src/web/Grimoire.slnx                → Superado: 132, Con error: 0, Omitido: 0
```

22 tests nuevos: `RaritySelectorTests` (11) y `DepthScoreTests` (7 métodos, uno con Theory de 5 casos → cuenta como varios). Cubren:
- Término de rareza: null → 0 neutro; null == 1e6 oyentes; rara > desconocida > mega-popular; `GREATEST(listeners,1)` con 0 oyentes; peso 0 desactiva.
- Sorteo Gumbel-max: con ruido igual elige el término mayor; null no vence a una banda rara; términos iguales → uniforme (recupera random-within-ring); frecuencia con RNG **sembrado** (determinista, no flaky): Nameless se elige mucho más que null, y null cae cerca de una mega-popular, no de la rara.
- Depth Score: orden de tiers monótono; null → 0; suma; nulls no suman pero las reales sí; todo-null → 0 (degrada con dignidad).

**Muerden**: invertir `RarityTerm` null `return 0.0 → 999.0` → **5 tests en rojo** (comprobado, revertido).

### Motor de punta a punta contra la base viva

La base viva ya tiene `listeners`/`rank` poblados por el agente ETL: **80 servibles, los 80 con listeners** (rango 55 … 4 789 021). Flujo register → seed → serve → summon:

```
seed → {"hasTaste":true,...,"depthScore":0}
serve x5 (comfort 0.6) → 5 tokens distintos, HTTP 200  (la consulta del anillo con la proyección
                                                          {Id,Distance,Listeners} traduce y sirve)
summon x3 (comfort 0.8, extremo Abyss):
  Antestor       rank=Hidden    → revealDepthScore=3    (3)
  Askemåne       rank=Nameless  → revealDepthScore=8    (3+5)
  Cryptic Tales  rank=Forgotten → revealDepthScore=12   (8+4)
GET taste → {"summonedCount":3,...,"depthScore":7|12}   (coincide con la suma de Points por rank)
```

El Depth Score acumula **exactamente** la fórmula (`Hidden 3 → Nameless 5 → Forgotten 4`), el reveal lo lleva, y con `comfort=0.8` (Abyss) salieron bandas raras (Nameless/Forgotten), coherente con el sesgo de rareza operando **dentro** del anillo lejano.

Base dejada limpia: los 3 usuarios de verificación borrados (cascade eliminó sus rites/taste) → `rites=0`. Corpus intacto: **2478 artistas**. Queda un `user_taste` que **no es mío** (usuario `lastfm-live-…@grimoire.test` del agente paralelo, no huérfano) — no se toca.

### Gate

```
bash scripts/audit.sh --strict   → RESULT: PASS (Violations 0, Skipped 0)
```
Verdes: `dotnet-build`, `dotnet-test`, `pnpm-lint`, `pnpm-build`.

---

## 3. Huecos declarados (y su porqué)

- **Caso null en el pool servible: no ejercitado en vivo**, solo en tests. Ahora mismo los 80 servibles tienen `listeners` (el ETL los pobló), así que en la base viva el término de rareza engancha para todos y no hay null servible que probar en vivo. El tratamiento neutro de null está cubierto por la función pura y sus tests. Cuando el pool servible crezca hacia la cola oscura, aparecerán servibles con `listeners` null y el término neutro los tratará como «desconocidos», no como «rarísimos». La integración final a esa escala la valida el coordinador.
- **`w_novel` (novelty por tags) de §6: NO implementado.** La query de §6 tiene tres términos: distancia, rareza y `tag_novelty`. Este pase enciende solo el de rareza (el que la tarea pedía). El de novelty queda para un pase posterior; no afecta al motor actual.
- **El sorteo materializa el anillo en memoria.** A escala grande (decenas de miles de servibles) una franja de percentiles puede traer algunos miles de filas `{Guid, double, int?}` por serve — barato para un endpoint interactivo, pero anotado por si el pool crece mucho: se podría empujar el Gumbel-max a SQL (`ORDER BY rarityTerm + (-ln(-ln(random()))) DESC LIMIT 1`) si el traslado a memoria pesara.

---

## 4. Decisiones a promover a `DECISIONS.md`

> Ninguna contradice un invariante. El coordinador las ratifica como `D<n>`.

1. **El término de rareza de §6 se enciende y supersede la cláusula de D31** («sin término de rareza mientras `listeners` sea null»). Ahora `listeners` está poblado. El término `ln(1e6/GREATEST(listeners,1)) * w_rare` **reordena dentro del anillo**, no lo sustituye: se aplica como **sorteo ponderado (Gumbel-max)**, no como orden determinista, para preservar la exploración aleatoria-dentro-del-anillo de D26/D31. Peso `RarityWeight` (default 0.15), ajustable.
2. **Tratamiento neutro de null en el término de rareza.** `listeners` null → término `0` (neutro), nunca «infinitamente raro». Un null es «desconocido», no «rarísimo»; pesa igual que una banda en 1e6 oyentes y nunca domina el sorteo. Es la salvaguarda que impide que la cola oscura sin dato de Last.fm gane siempre.
3. **Depth Score (B15) definido.** `depth_score = Σ Points(rank)` sobre `rites WHERE state='summoned'`, con `Nameless=5, Forgotten=4, Hidden=3, Obscure=2, Known=1`, y **`rank null → 0`** (no se inventa). Se recalcula en cada summon y se persiste en `user_taste.depth_score`. Expuesto en `GET /api/rite/taste` y en el reveal del summon.
