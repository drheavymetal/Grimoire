# Grimoire — worklog exhaustivo

> Registro detallado y cronológico de **todo lo hecho** en la sesión que llevó Grimoire del movimiento I al producto completo, desplegado en `grimoire.drheavymetal.com`. Complementa `MEMORY.md` (la memoria consolidada: qué/cómo/datos/arquitectura/despliegue) con el detalle paso a paso. Método común de todas las olas: **subagentes con frentes de fichero disjuntos**, migraciones con **un único dueño por ola**, `scripts/audit.sh --strict` en verde antes de cada commit, **tests que muerden**, verificación en vivo contra la base. 35 commits (`2f6d701..6d0bad3`), sin firma GPG. Detalle por ola en `docs/progress/*.md`.

---

## Punto de partida — `2f6d701`
Movimiento I terminado: esqueleto vertical, 307 artistas de muestra, búsqueda trigram, ficha, auth Identity+JWT con guarda de arranque, i18n es/en, `audit.sh` verde.

---

## Fase 1 — Movimiento II (El Rito) + cadena de rank

- **`c15eae1`** — Cimientos mov. II (dos agentes disjuntos ETL/Ficha): aristas `member_of` con fechas+instrumentos (2342), previews iTunes→Deezer (D25), embeddings centrados variante C (D26, spread 0.29 sano), proxy de portadas CAA cacheado (404 incluidos), discografía con demo visible.
- **`ccb4200`** — El Rito (motor + UI): tablas `user_taste`/`rites`, motor en anillo por **percentiles** (ventana 0.20, repulsión p20), servido a ciegas, proxy de audio con URL de capacidad (SSRF cerrado), Summon/Banish/Again que mueven el gusto (D33), arranque en frío por 5 bandas, C3/C4/C13, reveal 600 ms.
- **`703d925`** — Promoción de D30–D34 a `DECISIONS.md`, estado en `CLAUDE.md`.
- **`8553e2c`** — Key de Last.fm en user-secrets (nunca commiteada); obtenida y validada contra Last.fm en vivo.
- **`8d9fb07`** — Cadena de rank: `listeners`→`rank` por MBID (D37), término de rareza como sorteo Gumbel-max con **null neutro** (D35), Depth Score (D36). Y **el bug que cazó Pedro con el ojo**: la dirección de corrosión de `Redaction` estaba invertida (D38 — `100` nítido, `10` corroído; `DESIGN.md` tenía razón, `skeleton.md` no). Función `redactionCutForRank`, test y comentarios corregidos.

---

## Fase 2 — Construcción autónoma de los movimientos III–VII

Pedro pidió acabar **todo el producto de forma autónoma**, commit+push incremental, y verificación con Playwright. Se ejecutó por olas de agentes:

- **`776820f`** — Backend del Gantt: `ArtistEdgeDto` gana el artista **contraparte** (miembro al ver banda, banda al ver persona).
- **`65f32db`** — **Data backbone** (dueño único de migraciones): tablas `credits`/`works`, columnas `death_*`/`xy`. Enriquecimiento gratis: influencia Wikidata P737 (67 aristas), fallecimientos P570 (0 al principio — casi ninguna persona con QID), proyección Atlas por PCA en Python puro (309 xy).
- **`470bb0b`** — **Movimiento III (el Gantt)**: B7 timeline (técnica propia, layout puro en `core/` + SVG, no d3-force), B8 miembros que se iluminan al pasar por un disco, B10 página de miembro. Verificado con Darkthrone (formación de 1991 correcta).
- **`3c42a0a`** — **Movimiento IV (Linaje)**: motor de grafo compartido (`d3-force` headless + SVG, D18), Bloodline (B16), Six Degrees BFS (B19, Megadeth→Slayer vía Kerry King), diáspora (B11), eslabón perdido C5 (Megadeth↔Slayer → Metallica), Rabbit Hole (C8), tu-grimorio-grafo (C17).
- **`f060a36`** — ETL de créditos/sellos/fallecimientos (MB 1 req/s, resumible): 9763→ créditos, 179 labels, 24 deaths (Phil Lynott, Peter Steele…).
- **`d03da72`** — **Ola Q (firma visual)**: cortes graduados de `Redaction` cableados por rank, modo claro híbrido, **El Atlas** (C18, canvas). Se recuperaron tests xUnit huérfanos que los commits por-ruta se habían dejado.
- **`00b14f9`** — **Movimiento V (Escenas)**: escenas ciudad+año+tag (B20/C11), sellos (B21), búsqueda semántica (B2), comparar (B24), splits (C9), muro de portadas (C6), regalo cifrado stateless (C22), grimorios cruzados (C23), un-álbum/hiperprolífico (C24/C25). C7/C10/C21/C26 declarados sin dato.
- **`0dc84bf`** — **Movimiento VI (Espejo)**: Weekly Rite + **WebPush real** (VAPID, service worker, llegó a FCM), trayectoria (C16), el espejo (C20), Dark Twin (B18, bug de colección vacía cazado), anti-rec (B25), gaps (B23). Migración `push_subscriptions`/`taste_snapshots`.
- **`aa65db8`** — Datos de **clásica (mov. VII)**: 23 compositores, 2291 obras, teacher/student (cadena Fauré→Boulanger→Glass) + **features de completado** B9 créditos por disco, C12 In Memoriam, C15 instrumentos raros, B12 disco-pivote.
- **`9cb0c7d`** — Front de **mov. VII**: ficha de compositor (obras + linaje, sin Gantt, D11); `HasWorks` decide banda-vs-compositor.

---

## Fase 3 — V&V (verificación) y un bug de UX

- **`93b8be2`** — Suite Playwright round 1: 19 specs E2E sobre todos los tentpoles, **cero bugs de producto**.
- Revisión adversarial de código (round 2): 0 defectos alto/medio; 2 bajos.
- **`08fb479`** — Arreglo de los 2 hallazgos: **H1 (D39)** un rito `Served` abandonado agotaba el pool servible → ahora se purgan los `Served` sin resolver al servir; **H2** flag `volatile` en `AtlasProjector`. Round 2 E2E: 39 specs.
- **`3554744`** — Las 2 últimas features nombradas: **C2 duelo a ciegas** (Bradley-Terry) y **C27 adivina la década**.
- **`9f99cfa`** — Bug que reportó Pedro probando: **la escucha a ciegas sonaba con todas las bandas a la vez** (el Weekly montaba 7 players con autoplay). Arreglo: coordinación global de audio (reproducir uno pausa el resto) + `autoPlay` solo en el Rito diario.

---

## Fase 4 — Escala: del muestreo de 307 al catálogo real (207k)

Pedro echó en falta Manowar/Gamma Ray/Overkill → se confirmó que el corpus era una muestra capada. Eligió el **dump completo de MusicBrainz (D5)**.

- **`86a48e7`** — **Import del dump completo de MB**: se descargó (~7 GB), se montó un Postgres MB temporal, se cargaron las tablas necesarias, y se destiló el subgrafo metal/rock/folk → **2.5k → 207 622 artistas**, 199 971 member edges, 668 885 releases, 65 600 sellos. Upsert **no destructivo** por MBID (no pisa enriquecimiento). Scripts `scripts/mb-import/`.
- **`2e55396`** — **Escucha online JIT (D40)**: a 207k no se puede pre-resolver preview → el anillo filtra solo por embedding, y el preview se resuelve **al servir** (iTunes→Deezer), se cachea, se saltan insonorizables. Stream por el proxy anti-leak. **Cero audio local.** Verificado en vivo (audio real de iTunes, 1 MB).
- **`9e3b083` + `afe83d8`** — Embeddings del catálogo (175 230): el diseño all-in-memory moría a los 109k por OOM/kill → **reescrito batched, incremental y resumible** (mean del corpus de una muestra por adelantado; keyset-paging; guarda cada 400; sobrevive a kills con marcador de mean). La DB sube en vivo.
- Atlas a escala: sin numpy/pip → script rápido (`atlas_fast.py`) que calcula la base PCA de una muestra por power-iteration y **proyecta las 175k en SQL con el producto interno de pgvector** (segundos). xy poblados para las 175k.

---

## Fase 5 — Rediseño visual v2

Pedro: «la interfaz me parece fea, no hay logo, quiero algo wow con toque metal, desde cero». Se pitcharon dos **Artifacts** (dirección visual) que aprobó, luego se implementó.

- **`2957e3b`** — Rediseño v2 (reskin, sin tocar la lógica): sistema de tokens (vacío/bone/azufre), hiss de scanline en oscuro, flyer/semitono en claro (híbrido Q2), **logo** (anillo que se deshace en semitono + eje de azufre; wordmark `GR[I]MOIRE`) + favicon, shell/nav, landing, **el Rito como ritual** (señal que pulsa), ficha con nombre corroído + Gantt.
- **`e520815`** — CORS de dev permite puertos 5174/5175 (CromoWin ocupa :5173).
- **`fbc0674`** — Pulido: todas las pantallas secundarias en v2 (Atlas con viñeta, In Memoriam con espina cronológica, `SectionHead`/`PageHeader`). Destapó un bug de grafo a escala.
- **`44a8abb` + `c2e71d4`** — **Bug del grafo**: `/api/splits` devolvía una **arista colgante** (a un nodo ausente) → `d3-force` petaba → sin boundary tumbaba `/explore`. Arreglo: `layoutGraph` filtra aristas colgantes (degrada, no rompe — invariante 5). (El primer commit rompió `pnpm build` por tipos del test; el segundo lo dejó verde.)

---

## Fase 6 — Docker y despliegue

- **`d573896`** — **Imágenes de producción construidas por primera vez** (nunca se habían construido): API 362 MB, worker 316 MB, front 96 MB. Arranque verificado en stack aislado; guarda D28 verificada en ambos sentidos. Arreglos: faltaba `.dockerignore`; la API bindeaba mal en contenedor (`appsettings` pisaba `ASPNETCORE_URLS` → `--urls 0.0.0.0:8080` en el ENTRYPOINT).
- **`56b1ab4`** — El front hornea `VITE_API_URL` en build (mismo origen en prod, sin CORS) + `docs/progress/deploy.md`.
- **Despliegue** a `drheavyserver` (192.168.1.3), tras Traefik v3.2:
  - Acceso SSH **sin 1Password**: se instaló `id_ed25519` en el `authorized_keys` del server (usando la conexión de 1Password mientras estaba desbloqueada).
  - Stack aislado en `~/apps/grimoire/` (compose propio, red `grimoire` + `traefik_default`, sin puertos de host — Traefik enruta por nombre de contenedor).
  - Router `~/apps/traefik/dynamic/grimoire.yml` (aditivo, hot-reload; `Host(...)`→front, `&& PathPrefix(/api)`→api).
  - Datos: `pg_dump -Fc` de la base dev → restaurado (índices HNSW/GIN reconstruidos).
  - Clave JWT fuerte generada en `~/apps/grimoire/.env` (nunca commiteada).
  - **Verificado**: web 200 + cert Let's Encrypt, `/api` sirve datos, http→https 301. **Nada de los otros 12+ servicios del server se tocó.** (Desde la LAN no se ve por NAT hairpin; desde internet, sí.)

---

## Fase 7 — Las 5 features bloqueadas por datos

Pedro: «sigue con esas 5 cosas y los 2 detalles, luego redespliega».

- **`f54e1aa`** — Detalle 1: **error boundary de grafos** (los 4 usos de `GraphCanvas`, degrada local) + `docs/MEMORY.md` (memoria consolidada, incluido el despliegue).
- **`3990e8f`** — **Import de grabaciones de MB**: Postgres MB temporal con `recording`/`track`/`medium`+covers, subset a nuestros releases → **8 925 364 recordings** (99.9% de releases, títulos 100%, duración 91%), **21 418 versiones**. Migración `AddRecordingsAndCoverVersions`. Desbloquea C7/C21/C10.
- **`a1e479d`** — Las UIs: **B5** tracklist (título+duración), **C7** eje de duración (excluye null), **C21** minería de títulos (léxico cerrado es/en, marcado aproximación), **C10** grafo de versiones (cross-artist), **C26** deriva cromática (proxy con CORS → paleta en cliente sin taint). **C19** (timbre) → **hueco declarado** (sin numpy/scipy/librosa/pip; ffmpeg sí, pero sin FFT solo saldría un stub; D25 ya lo degradó a opcional).
- **`1a32a6b`** — Memoria refleja las features + redespliegue.
- **Redespliegue**: rebuild de imágenes (API+front cambiaron), dump fresco (1.3 GB, con los 8.9M recordings), transfer, **recreación limpia de la base** (hubo que usar `dropdb --force`/`createdb` — `DROP DATABASE` no va en transacción), restore, up. Verificado en vivo: tracklist (Darkthrone → 22 temas), temas (Death 32, Fire 28, Winter 23), TLS. Los demás servicios intactos.

---

## Fase 8 — Arreglo de cobertura de rank (en curso)

Pedro preguntó cómo iba el enriquecimiento perezoso → se diagnosticó que **el rank solo cubría el 3,3%** (2 639 de 79 729 con tags), **no por Last.fm ni por tiempo**, sino porque D37 emparejaba **solo por MBID**, que a escala falla masivamente (Last.fm indexa cada banda bajo su propio MBID, distinto del de MB; incluso había dos «Iron Maiden», solo uno matcheó).

- **`6d0bad3`** — **D41**: emparejado híbrido — MBID primero, **fallback por nombre** (`ResolveByName`, verifica nombre, acepta MBID distinto). Sube la cobertura de 3% → previsión 40-60%. Enciende Depth Score, degradación tipográfica y término de rareza para gran parte del catálogo. Tests que muerden. **Corriendo detached** (~8h); cuando madure → re-dump + restore a producción.

---

## Bugs cazados y arreglados

1. **Dirección de corrosión de Redaction invertida** (D38) — la vio Pedro mirando el Artifact. `100` es nítido, `10` corroído; el código lo tenía al revés (latente, sin cablear). Arreglado antes de cablearlo.
2. **Escucha a ciegas con todas las bandas a la vez** (`9f99cfa`) — el Weekly montaba 7 players con autoplay sin coordinación. Coordinación global de audio.
3. **Rito servido-y-abandonado agotaba el pool** (H1/D39) — se purgan los `Served` sin resolver al servir.
4. **Arista colgante rompe la ruta del grafo** (`44a8abb`) — filtro de aristas colgantes + error boundary.
5. **Embeddings mueren a los 109k sin guardar** (`9e3b083`) — reescrito batched/resumible.
6. **Cobertura de rank al 3%** (D41) — fallback por nombre.
7. **`DROP DATABASE` en transacción** (redespliegue) — `dropdb --force`/`createdb`.
8. **Auto-kill con `pkill -f`** (operativo) — el patrón coincidía con la propia línea del shell → matar por pid numérico.

---

## Operaciones de datos ejecutadas
- Import del dump completo de MB → 207k artistas + subgrafo destilado.
- Import de 8.9M recordings + tracklists + versiones.
- Embeddings del catálogo (175k, batched/resumible).
- Proyección Atlas (175k xy, PCA muestra + SQL pgvector).
- Enriquecimiento Wikidata (influencia, fallecimientos), créditos/sellos (MB), listeners (Last.fm, en curso con D41).

---

## Estado final y pendiente

**Hecho**: producto feature-complete (solo C19 declarado), catálogo real 207k + 8.9M recordings, motor a escala, escucha online JIT, rediseño v2, **desplegado y vivo en grimoire.drheavymetal.com** con TLS, memoria en `MEMORY.md`. 35 commits en `origin/main`.

**Pendiente por mi parte**: cuando el job `listeners` D41 madure (horas), **re-dump + restore a producción** con los ranks buenos (queda a la espera por decisión de Pedro). Vigilar el loop. Tech-debt menor: el verbo `atlas` de consola lento a escala.

**No es mío**: respuesta de Metal Archives (Q4), revocabilidad de refresh tokens (D28), push al registro `go2chaindev/*` (credenciales del equipo).
