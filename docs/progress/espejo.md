# Movimiento VI — Espejo (agente Espejo)

> Estado: **terminado y verde**. Weekly Rite + WebPush real (B17), Tu trayectoria (C16), El espejo
> (C20), Dark Twin (B18), Anti-recomendación (B25) y Gaps (B23). Frontera respetada: `src/web/server/**`,
> `src/front/**` y **una** migración EF. Cero `src/console/**`, cero poblado de ETL. `scripts/audit.sh
> --strict` → **RESULT: PASS** (0 violaciones, 0 skips). Verificado en vivo contra la base 5433 y la
> API 5080. Fecha: 2026-07-11.

Complementa `rite-engine.md` (motor y proxy de audio que reuso), `rite-front.md` (RitePlayer/RevealName
que reuso), `visual.md` (Atlas/`AtlasProjector` y `RankedName`/D38 que reuso).

---

## 1. Migración creada (dueño único este pase)

`20260711022545_AddPushSubscriptionsAndTasteSnapshots` — dos tablas, aplicada a la base viva y verificada:

- **`push_subscriptions`** (`id` PK, `user_id` FK cascade, `endpoint` único, `p256dh`, `auth`, `created_at`).
  El handle de capacidad del navegador para WebPush (B17). El servidor **no** lo fabrica — lo hace
  `PushManager.subscribe` — solo lo guarda. Índice único por `endpoint` (upsert), índice por `user_id`.
- **`taste_snapshots`** (`id` PK, `user_id` FK cascade, `embedding vector(768)`, `depth_score`, `created_at`).
  El histórico versionado del vector de gusto (C16). Índice `(user_id, created_at)` para leer la
  trayectoria en orden. Vector **ya centrado** (D26), nunca re-centrado.

Modelos nuevos en `src/shared` (aditivos, exigidos por la migración): `Models/PushSubscription.cs`,
`Models/TasteSnapshot.cs`; DbSets + config en `GrimoireDbContext`. Nada más de shared cambió.

---

## 2. Qué se construyó

### Servicios puros (shared, testeados que muerden)

- **`WeeklyRiteSelector`** (B17): `IsoWeekKey(instant)` (alineado a lunes ISO) + `Select(pool, weekKey, 7)`
  — selección determinista por semana. Ordena el pool por id (el orden de entrada no perturba), siembra
  un PRNG SplitMix64 desde un FNV-1a del weekKey, y hace Fisher-Yates parcial. **Misma semana + mismo pool
  → mismas siete, byte a byte.**
- **`MirrorMath`** (C20): `Compute(summonedTags, banishedTags)` → tag favorito (el más frecuente entre lo
  invocado, desempate alfabético) + fracción de bandas desterradas que lo llevan. `HasData=false` cuando
  no hay favorito o nada desterrado.
- **`TrajectoryMath`** (C16): `DriftSeries` (distancia coseno par a par) + `TotalDrift` (primer→último).
- **`DarkTwinMath`** (B18): `Best(myTaste, mySummoned, candidates)` con score `tasteSimilarity ×
  disjointness`. **Salta candidatos con colección vacía** (nada que ofrecer → su disjointness trivial de
  1.0 no debe ganar); desempate por el menor `user_id`.

### Backend (`src/web/server`)

- **`Services/WebPushSender.cs`** + `WebPushOptions`: envío real vía la librería `WebPush` (encriptación
  RFC 8291 + firma VAPID RFC 8292). `Enabled` solo con clave privada VAPID presente. `SendAsync` devuelve
  `Delivered|Gone|Failed`; 404/410 → `Gone` (se purga); cualquier excepción (incl. cripto por subscription
  malformada) → `Failed`, nunca 500, nunca aborta el lote.
- **`Controllers/PushController.cs`** (`/api/push`): `vapid-public-key` (anónimo, 503 si no hay clave),
  `subscribe` [auth] (upsert por endpoint), `unsubscribe` [auth].
- **`Controllers/WeeklyController.cs`** (`/api/weekly`): `GET` [auth] materializa las 7 como ritos ciegos
  del usuario (idempotente por semana: reusa los ya servidos, solo crea los que faltan; 409 sin taste),
  cada uno con URL de audio proxiada y `risk` = rango dentro de las 7 por distancia al gusto. `POST notify`
  [auth] dispara el push (503 sin VAPID); payload de datos (`{type,count,url}`) que el SW localiza.
- **`Controllers/MirrorController.cs`** (`/api/mirror`): `reflection` (C20), `trajectory` (C16, proyecta
  cada snapshot al plano del Atlas con `AtlasProjector` + deriva), `anti-rec` (B25, la banda no juzgada
  más cercana a la repulsión, con distancias y tags rechazados compartidos), `dark-twin` (B18, anónimo),
  `gaps` (B23, décadas/países/subgéneros no invocados, mayores primero).
- **`RiteController`**: escribe un `TasteSnapshot` en cada cambio de gusto — seed, import-lastfm y cada
  summon (C16). Vector clonado para no compartir array con la fila viva.
- DTOs: `PushDtos`, `WeeklyDtos`, `MirrorDtos`. `Program.cs` registra `WebPushOptions` + `WebPushSender`.
  `appsettings.json` lleva la clave pública VAPID + subject; la **privada** vive en user-secrets.

### Frontend (`src/front`)

- **core/** (sin DOM, invariante 6): tipos nuevos en `types.ts`; métodos de cliente
  (`vapidPublicKey`/`subscribePush`/`unsubscribePush`/`weekly`/`notifyWeekly`/`reflection`/`trajectory`/
  `antiRec`/`darkTwin`/`gaps`); hooks `useWeekly`/`useNotifyWeekly`/`useMirror.*`; y `domain/trajectory.ts`
  (geometría pura del SVG de la trayectoria, testeada sin navegador).
- **platform/push.web.ts**: el ÚNICO sitio que toca Service Worker / PushManager / Notification. Registra
  `/sw.js`, suscribe con la clave VAPID, aplana la subscription. **public/sw.js**: muestra la notificación
  del OS y navega al hacer clic; localiza el texto es/en desde `navigator.language` (sin i18next en el worker).
- **ui/**: `pages/WeeklyPage.tsx` (suscripción push + las 7 jugables), `weekly/WeeklyItem.tsx` (reusa
  RitePlayer + resolve), `push/PushSubscribe.tsx`, `pages/MirrorPage.tsx` (las 5 secciones con estados
  vacíos diseñados), `mirror/TrajectoryChart.tsx`. Rutas `/weekly` y `/mirror` + nav en `Layout`.
  Firma tipográfica por rank (`RankedName`, D38) reusada en la anti-rec.
- i18n: claves `nav.weekly/mirror`, `weekly.*`, `mirror.*` en **es y en** ambos.

---

## 3. Verificación en vivo (comando → salida real)

API `:5080` (Development) + Postgres `:5433`, 2478 artistas / 80 servibles / 309 xy. Flujo con usuarios
frescos vía HTTP (las mismas llamadas que hacen los hooks):

```
weekly GET → 7 items, week 2026-W28; refetch → MISMOS 7 tokens (estable)
audio del primer token → HTTP 200 audio/x-m4p 1101849 bytes (preview real proxiado)
seed 5 → taste_snapshots = 1 (origen de la trayectoria)
summon x2, banish x3 → taste_snapshots = 3 (seed + 2 summons)   [C16 escribe de verdad]
weekly refetch → flags [(Summoned,true)(Summoned,true)(Banished,true)x3 (Served,false)x2]
mirror/trajectory → points 3, drifts [0, 0.176, 0.189], totalDrift 0.3142, xy proyectado en los 3
mirror/reflection → {hasData:true, favouriteTag:'black metal', 2/3, fraction 0.667}   [C20]
mirror/anti-rec → 'Toto' toRepulsion 0.6822, toTaste 1.016, sharedRejected [pop rock, rock, ...]
  ↳ verificado por SQL que 0.6822 ES el argmin real de distancia a la repulsión (no invertido)
mirror/gaps → decades [(1990s,100)(1980s,88)(2010s,36)], countries [(US,78)(DE,33)(GB,32)], subgenres...
dark-twin (1 usuario) → {hasData:false} (estado vacío honesto)
dark-twin (2 usuarios, colecciones disjuntas) → hasData:true, theirsOnly 3 bandas reales
push/subscribe → 204 (fila persiste); push/unsubscribe → 204
weekly/notify (subscription con keypair P-256 válido a un endpoint FCM inexistente) →
  200 {sent:0, pruned:1, failed:0}   ← alcanzó FCM real, 404/410, purgó el endpoint muerto
```

**Prueba anti-cascarón**: cambiar rites/taste en la base cambia lo que devuelven mirror/trajectory/weekly
(leen la base viva). Migración aplicada y `\d` de ambas tablas comprobado. Base dejada limpia: 0 usuarios,
0 rites/taste/snapshots/subscriptions; corpus intacto (2478 / 80 / 309).

### Tests que muerden (xUnit 272 verde total; Vitest 79 verde)

Nuevos: `WeeklyRiteSelectorTests` (misma semana→mismas 7, independiente del orden, semanas distintas
difieren, ISO lunes-alineado), `MirrorMathTests` (el 0.667, empate alfabético, case-insensitive, vacíos),
`TrajectoryMathTests` (deriva par a par, total≠suma de pasos), `DarkTwinMathTests` (cercano+disjunto gana,
colección vacía se salta), front `trajectory.test.ts` (mapeo al viewbox, no inventa el origen).
**Comprobado que muerden**: seed que ignora el weekKey → `Select_DifferentWeeks_Differ` falla; numerador
del espejo invertido → `Compute_Fraction...` falla; deriva a cero → `TrajectoryMath` falla. Revertidos → verde.

### Gate

```
bash scripts/audit.sh --strict → RESULT: PASS (0 violaciones, 0 skips)
dotnet test → 272 passed        pnpm test → 79 passed        pnpm build → ✓ (sw.js en dist)
front dev → root 200, /sw.js 200 text/javascript, MirrorPage/WeeklyPage/push.web transforman 200
```
Puertos liberados por pid al terminar (5080 y 5173 libres, sin procesos colgados).

---

## 4. Exposiciones declaradas (estilo D28)

- **Clave privada VAPID = secreto.** Vive solo en user-secrets (dev) / variable de entorno (prod), **nunca
  commiteada**. La pública va en `appsettings.json` (es material público por diseño). Sin la privada,
  `notify` → **503** honesto; `subscribe`/`vapid-public-key` siguen funcionando (solo hacen falta la pública).
- **`push_subscriptions.endpoint` es una capacidad bearer.** Quien lo tenga podría hacer push a ese navegador
  *solo si además tuviera la clave privada VAPID*. Se borra en cascada con la cuenta; los muertos se purgan
  perezosamente al recibir 404/410 en el envío.
- **`taste_snapshots` retiene el histórico completo de embeddings de gusto, indefinidamente.** Es **más
  revelador que `user_taste`** (que guarda solo el último vector): expone cómo se movió el gusto en el tiempo.
  Borrado en cascada con la cuenta. **No hay TTL ni poda** — una política de retención es una decisión
  posterior deliberada, no un accidente.
- **La anti-rec (B25) revela los géneros que rechazaste** (`sharedRejectedTags`). Es intencional: un tag no
  es la identidad de una banda, y C20 ya expone estadística de géneros. No revela **qué bandas** desterraste
  (siguen a ciegas — C3/C20).

---

## 5. Huecos declarados (y su porqué)

- **Sin navegador headless → no se puede fotografiar la notificación del OS.** Pero lo verificado es fuerte:
  el envío real **alcanzó FCM de Google**, que rechazó el registro falso con 404/410 y **purgamos el endpoint**
  (`pruned:1`). Es decir, la encriptación RFC 8291, la firma VAPID y el POST HTTPS al servicio de push real
  **corren de verdad**. Lo único no observado es el pop de la notificación en el SO y el clic — que exige un
  navegador con permiso concedido y una subscription real, inexistentes aquí.
- **El Weekly Rite exige taste** (409 → arranque en frío). Consistente con `serve`. La UI enruta a cold start.
- **«Las mismas 7» se garantiza dado el pool servible estable entre corridas de ETL** (D5). Si el ETL
  re-siembra a mitad de semana, las 7 podrían cambiar; en producción el pool es estático entre refrescos.
- **Dark Twin: heurística `tasteSim × disjointness`**, anónima; con pocos usuarios elige el mejor disponible
  *con colección real*. Con un solo usuario → estado vacío honesto (verificado).
- **Anti-rec/Dark-twin/Gaps operan sobre el corpus embebido pequeño (309 con embedding).** Resultados
  deterministas y correctos (verificado por SQL que la anti-rec es el argmin real), pero la «intuición» de un
  resultado (p. ej. Toto más cerca de una repulsión metalera) es un artefacto de la geometría de embeddings
  centrados a esta escala, no un bug del motor.
- **La trayectoria se dibuja con proyección lineal (PCA) heredada del Atlas.** Si algún día hay UMAP, cambia
  el pase offline y habría que reproyectar (mismo caveat que `visual.md`).
