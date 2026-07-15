# Grimoire — memoria del proyecto

> Documento de **memoria consolidada**: qué es, qué se construyó, cómo, con qué datos, y cómo está desplegado. Se lee junto a `WORKLOG.md` (**el registro exhaustivo y cronológico de todo lo hecho** — 35 commits, cada ola, cada bug, cada operación de datos, el despliegue paso a paso), `DECISIONS.md` (el porqué de cada decisión, append-only), `SPEC.md` (el qué), `DESIGN.md` (la dirección visual) y `progress/*.md` (el detalle por ola). Última actualización: **2026-07-15** (ver **§6c** — MA scraper + Last.fm tags corriendo, clásica eliminada, 3 fixes de producto, Rito por género, enlaces de streaming; **tarde: optimización MA D53** — pool metal-ish + 3 req/s, sin cap).

---

## 1. Qué es

App de descubrimiento musical para **metal, rock y folk** (clásica en un movimiento aparte). Producto independiente, gratuito, **coste operativo cero**, para un grupo de amigos. **No** es una app de qlaios (D1).

La tesis: no dejamos de escuchar lo mismo por falta de recomendaciones, sino porque **filtramos por etiqueta antes que por oído**. Tres pilares:
- **The Rite** — cata a ciegas: 45 s de audio sin nombre, género ni portada; solo se revela si gusta.
- **Ranks** — la rareza es **inversa** a la popularidad: descubrir Metallica no vale nada.
- **Bloodline** — el linaje real: miembros compartidos (MusicBrainz) + influencia (Wikidata P737).

---

## 2. Estado actual (foto)

- **Desplegado y vivo en `https://grimoire.drheavymetal.com`** (TLS Let's Encrypt, ver §6).
- **Catálogo real: 206 887 artistas** (tras purgar la clásica, D50 — antes 207 622) destilados del dump completo de MusicBrainz (D5), 668 885 releases, `member_of` con fechas e instrumentos, 65 600 sellos. **Sin música clásica** (§6c).
- **Motor de descubrimiento a escala**: 175 230 embeddings centrados (D26), búsqueda en anillo por percentiles, proyección Atlas (xy) para las 175k.
- **Escucha a ciegas online**: previews resueltos **just-in-time** al servir (iTunes→Deezer), stream por proxy anti-leak, **cero audio local** (D40).
- **Rediseño visual v2** implementado en toda la app (identidad metal atmosférica: logo, corrosión por rareza, el Rito como ritual — ver §5).
- **Feature-complete**: los 7 movimientos, B1–B26 y C1–C27 (solo **C19** queda como hueco declarado por falta de toolchain de audio — §7). Incluye tracklists/duración/temas/versiones/paleta sobre **8 925 364 grabaciones** importadas de MB.
- **Enriquecimiento nocturno (2026-07-12)**: `rank` **2 639 → 14 330**, `credits` **5 153 → 32 929** grupos (casi entero), `influence` **80** (Wikidata bulk lo tumba con 502/429 — facet menor, ROI bajo).
  > ⚠️ **CORRECCIÓN (2026-07-14)**: esta entrada decía que el rank había llegado a un **«plateau»** y que «la cola restante no está en Last.fm ni por nombre». **Es falso.** El pase recorre a los artistas **por orden alfabético** (`ListenersJob.cs:65`, `.OrderBy(a => a.Name)`) y lo mataba el gestor de tareas de fondo: **nunca pasó de la letra B**. De los 14 330 con `listeners`, **13 206 empiezan por A o B**. No es un límite de la fuente, es un rastreo a medias — quedan **88 246 artistas descubribles sin `listeners`** (§6b).

Los 40+ commits viven en `origin/main` (github.com:drheavymetal/Grimoire), sin firma GPG.

---

## 3. Cómo se construyó — por olas, con agentes

La app se construyó de forma **autónoma con oleadas de subagentes** (patrón: frentes de fichero disjuntos, migraciones con un único dueño por ola, `scripts/audit.sh --strict` verde antes de cada commit, tests que muerden, verificación en vivo contra la base). Orden real:

1. **Movimiento I (cimientos, previo)** — esqueleto vertical: 307 artistas de muestra, búsqueda trigram, ficha, auth Identity+JWT con guarda de arranque (D28), i18n es/en.
2. **Movimiento II (El Rito)** — dos olas ETL+Ficha, luego el motor: relaciones `member_of` con fechas/instrumentos, previews iTunes→Deezer (D25), embeddings centrados variante C (D26/D31), motor en anillo por **percentiles** (D26), servido a ciegas + proxy de audio con URL de capacidad (D32), Summon/Banish/Again que mueven el vector de gusto (D33), arranque en frío por 5 bandas (D15).
3. **Cadena de rank** — key de Last.fm (guardada en user-secrets, ver `~/.claude` memoria), `listeners`→`rank` por MBID (D37), término de rareza como sorteo Gumbel-max con null neutro (D35), Depth Score (D36). Y se cazó un bug visual: la **dirección de corrosión de Redaction estaba invertida** (D38, la vio Pedro: `100` es nítido, `10` corroído).
4. **Movimiento III (Sangre y tiempo)** — el **Gantt** (B7/B8/B10): técnica propia (layout puro en `core/` + primitivas SVG, no d3-force), miembros que se iluminan al pasar por un disco. Créditos por disco (B9), In Memoriam (C12), instrumentos raros (C15), disco-pivote (B12).
5. **Movimiento IV (Linaje)** — motor de grafo compartido (`d3-force` headless + SVG, D18), Bloodline (B16), Six Degrees BFS (B19), diáspora (B11), eslabón perdido (C5), Rabbit Hole (C8), tu-grimorio-grafo (C17).
6. **Movimiento V (Escenas)** — escenas ciudad+año+tag (B20/C11), sellos (B21), búsqueda semántica (B2), comparar (B24), splits (C9), muro de portadas (C6), regalo cifrado stateless (C22), grimorios cruzados (C23), un-álbum/hiperprolífico (C24/C25).
7. **Movimiento VI (Espejo)** — Weekly Rite + **WebPush real** (VAPID, service worker), trayectoria (C16), el espejo (C20), Dark Twin (B18), anti-rec (B25), gaps (B23), **El Atlas** (C18, canvas). Migración `push_subscriptions`/`taste_snapshots`.
8. **Movimiento VII (Clásica)** — 23 compositores + 2291 obras + linaje maestro-discípulo (cadena Fauré→Boulanger→Glass), ficha de compositor distinta de la de banda (D11).
9. **Firma visual (Q)** — cortes de corrosión de Redaction cableados por rank, modo claro híbrido, Atlas. C2 duelo + C27 adivina-la-década.
10. **V&V** — 41 specs Playwright (los agentes escribieron y corrieron E2E), revisión adversarial (0 defectos alto/medio; se arreglaron H1 pool-lock D39 y H2 volatile).
11. **Escala (D5)** — **import del dump completo de MusicBrainz**: 2.5k→207k artistas. Embeddings del catálogo (batched, resumible — sobrevive a kills), Atlas rápido (PCA por muestra + proyección SQL pgvector), JIT de previews (D40).
12. **Rediseño v2 + pulido** — toda la capa visual rehecha sobre la dirección aprobada por Pedro.
13. **Despliegue** — a `drheavyserver` tras Traefik (§6).

---

## 4. Datos — el catálogo y su enriquecimiento

**El mirror de MusicBrainz es artefacto de build (D5)**: se importa el dump (~7 GB comprimido, en `/var/tmp/grimoire-mb`), se destila el subgrafo metal/rock/folk (tags ∪ expansión por miembros, 2 saltos — D23) al Postgres de Grimoire, y **solo eso se sirve**. Scripts en `scripts/mb-import/`.

| Dato | Fuente | Estado | Vacío = |
|---|---|---|---|
| artistas, member_of (fechas+instrumentos), releases, labels | dump MB | 207k / 200k / 669k / 66k | — |
| embeddings centrados (768) | Ollama `nomic-embed-text` local | 175 230 | sin señal (D26) |
| xy (Atlas) | PCA local | 175 230 | sin embedding |
| listeners→rank | Last.fm por MBID (D37) | perezoso, corriendo | no está en Last.fm |
| preview_url | iTunes→Deezer JIT al servir (D40) | crece con el uso | insonorizable (~48%, D25) |
| credits | MB release-rels | casi completo | — |
| influence (P737), deaths (P570) | Wikidata SPARQL | 67 / 65 | sin QID |

**Invariante clave (D26/D31)**: los embeddings guardados **ya están centrados**; el vector medio en `corpus_stats` es solo para centrar un vector de consulta externo — **no se resta dos veces**.

---

## 5. Arquitectura y mecanismos

**Stack**: .NET 10 · ASP.NET Core Web API · EF Core 10 + Npgsql · PostgreSQL 17 + pgvector + pg_trgm · Identity+JWT · Serilog · Polly · xUnit. Front: Vite + React + TS + TanStack + Tailwind v4 + shadcn + i18next. Embeddings: Ollama. Monorepo `src/{shared,web,console,front}` + `build/`.

Mecanismos que hay que entender antes de tocar el motor:
- **Búsqueda en anillo por percentiles (D4/D26/D31)**: se muestrea la distribución de distancias del pool servible al vector de gusto, se sacan los dos radios de los percentiles del slider Comfort↔Abyss (ventana 0.20), y se consulta el anillo con HNSW. La repulsión resta (p20). El término de rareza `ln(1e6/listeners)` es un **sorteo Gumbel-max** dentro del anillo; **null = neutro** (D35).
- **Escucha online JIT (D40)**: el anillo filtra solo por `embedding IS NOT NULL`; al servir se resuelve el preview de un candidato al vuelo (iTunes→Deezer), se cachea, se saltan los insonorizables. Stream por proxy de capacidad (D32, SSRF cerrado con allowlist + sin redirects). **Nunca audio local.**
- **La corrosión es el dato (D14/D38)**: el nombre de banda se renderiza en el corte de `Redaction` según su rank (`100` nítido para Known … `10` corroído para Nameless); rank null → corte base nítido, nunca corrosión inventada. El reveal del Rito camina de corroído a su corte en 600 ms.
- **El Gantt es su propia técnica (D18)**: layout puro en `core/` (sin DOM) + SVG. Los grafos de nodos (Bloodline, grimorio, splits) usan `d3-force` headless + SVG. El Atlas es la excepción a canvas/WebGL (D24). Todo con error boundary para degradar, no romper.
- **Aprende del gusto (D33)**: Summon mueve `user_taste.embedding` hacia la banda (EMA decay 0.25), Banish mueve `repulsion`, el duelo (C2) refina por pares. Se versiona en `taste_snapshots` (trayectoria, C16).

---

## 6. Despliegue — grimoire.drheavymetal.com

Detalle en `progress/deploy.md`. Resumen operativo:

- **Servidor**: `drheavyserver` = **192.168.1.3** (LAN), Ubuntu 25.04, Docker 28, **Traefik v3.2** en :80/:443 (config en `~/apps/traefik/`, certResolver `le` Let's Encrypt httpChallenge). Comparte máquina con otros servicios (farmacias, hermes, home-assistant…) — **el despliegue de Grimoire es aditivo y aislado; no toca nada de eso**.
- **Acceso SSH sin 1Password**: la clave `~/.ssh/id_ed25519` del dev box está en `authorized_keys` del server. Conectar: `ssh -o IdentityAgent=none -i ~/.ssh/id_ed25519 drheavymetal@192.168.1.3`.
- **Stack Grimoire** en `~/apps/grimoire/` (proyecto compose `grimoire`): `grimoire-db` (pgvector/pg17, volumen `grimoire-db-data`, red interna `grimoire`), `grimoire-ollama` (embeddings, red **interna**, sin puerto de host — ver abajo), `grimoire-api` y `grimoire-front` (en la red externa `traefik_default`; **sin puertos de host** — Traefik enruta por nombre de contenedor: `http://grimoire-front:80`, `http://grimoire-api:8080`).
- **Ollama en producción (2026-07-14)**: `grimoire-ollama` con `nomic-embed-text` (274 MB, volumen `grimoire-ollama-models`), y `Ollama__BaseUrl=http://ollama:11434/` en la API. **Solo lo necesita la búsqueda semántica (B2)**: los 175 230 vectores del catálogo ya están calculados y **servir un rito no invoca ningún modelo**. Sin esto, B2 devolvía **503 en producción** (el host tenía el binario de Ollama instalado pero el servicio `disabled/dead`, y sin `nomic-embed-text`). Está en la **red interna y sin puerto de host a propósito**: Ollama no tiene autenticación, así que solo `grimoire-api` puede hablarle. Huella real medida: **424 MB de RAM**, CPU 0% en reposo.
- **Router Traefik**: `~/apps/traefik/dynamic/grimoire.yml` (añadido, hot-reload). `Host(grimoire.drheavymetal.com)` → front; `&& PathPrefix(/api)` → api. Front y API **mismo origen** → sin CORS en producción.
- **Imágenes**: `go2chaindev/grimoire-{api,front,worker}:latest`, construidas en el dev box y transferidas por `docker save`/`load` (no hay push al registro privado — necesita credenciales del equipo). El front hornea `VITE_API_URL=https://grimoire.drheavymetal.com` en build — **el ORIGEN, sin `/api`**: el cliente ya prefija `/api` en cada ruta. Hornearlo con `/api` produjo `/api/api/...` → 404 en **todas** las llamadas (bug de 2026-07-14; el cliente ahora normaliza el sufijo, pero la build-arg correcta es el origen).
- **Traefik termina el TLS y reenvía por http** → la API necesita `UseForwardedHeaders` (`X-Forwarded-Proto/Host`). Sin él, `Request.Scheme` es `http` y las URLs de capacidad del audio del Rito (construidas con `Request.Scheme/Host`) salían `http://` en una página `https://` → el navegador las bloquea por contenido mixto y la escucha a ciegas muere. Cableado en `Program.cs` (2026-07-14).
- **Datos**: `pg_dump -Fc` de la base dev → restaurado en `grimoire-db` (índices HNSW/GIN reconstruidos). Patrón D5.
- **Secreto**: `Jwt__SigningKey` (64 chars, generado) en `~/apps/grimoire/.env` — **NUNCA commiteado**. Guarda D28 verificada (se niega a arrancar fuera de Development con clave dev o <32 bytes).

### Cómo redesplegar
1. Rebuild imágenes en el dev box (front con la build-arg de la URL). `docker save … | gzip` → `scp` → `docker load` en el server.
2. Si cambió el esquema/datos: `pg_dump -Fc grimoire` → `scp` → parar api/front, `docker exec -i grimoire-db pg_restore -U grimoire -d grimoire --clean --if-exists < dump`, o recrear el volumen. (Los datos frescos = catálogo + enriquecimiento del momento.)
3. `docker compose -f ~/apps/grimoire/docker-compose.yml up -d`. Verificar `curl --resolve grimoire.drheavymetal.com:443:127.0.0.1 https://grimoire.drheavymetal.com/` desde el server (la LAN no ve el IP público por NAT hairpin).

### Exposición declarada (D28)
Refresh tokens **no revocables** durante 16 días (sin logout server-side ni corte tras cambio de contraseña). Aceptado para amigos; revisar antes de abrir a más gente.

---

## 6b. Sesión 2026-07-14 — qué cambió y qué queda (LEER ANTES DE SEGUIR)

Sesión larga con Pedro. Cuatro bugs de producción, dos decisiones estructurales y un correo enviado.

### Arreglado y desplegado (verificado en prod, no solo en local)

1. **Doble `/api` → 404 en TODAS las llamadas** (commit `0c2a2a4`). El front se horneaba con `VITE_API_URL=https://host/api` y el cliente ya prefija `/api`. No era «el registro roto»: era la API entera. El cliente ahora normaliza el sufijo y el compose pasa el **origen**.
2. **Audio del Rito bloqueado por contenido mixto** (mismo commit). Traefik termina el TLS y reenvía por http → `Request.Scheme` = `http` → las URLs de capacidad del audio salían `http://` en página `https://`. Cableado `UseForwardedHeaders`.
3. **Búsqueda semántica (B2) muerta desde el despliegue** (commit `cf1e53f`). 503: la API no tenía config de Ollama y el host lo tenía `disabled/dead` y sin `nomic-embed-text`. Añadido `grimoire-ollama` en la **red interna sin puerto de host** (Ollama no tiene auth). Huella: 424 MB RAM.
4. **D46 — el Rito servía baterías de sesión como bandas** (commit `0372237`). **El peor de los cuatro.** 49 534 personas sin un solo disco estaban en el pool; **2 de cada 8 ritos** eran una persona, y el preview se emparejaba **por nombre** en iTunes → se servía el **audio de otro artista homónimo** como descubrimiento a ciegas. Filtro: `DiscoverableArtists.Discoverable()` = embedding **+ discografía**. Pool **175 230 → 100 915**.

### Producto

- **Arranque en frío rehecho** (commits `1c520af`, `6f9e4de`). La rejilla ya no ordena por nº de discos (eso enterraba el metal bajo el canon clásico: Bach 5 804 discos, Metallica 1 035) sino por **`listeners`, en round-robin por familias**. Y **crece hacia abajo**: al elegir una banda, sus vecinos se **insertan debajo** de ella; **nada de lo que ya leíste se mueve**. Reordenar la rejilla entera obligaba a releerla desde arriba en cada clic.
- `SeedPool.FamilyOf` clasifica por **el primer tag que nombre una familia**, no escaneando todos: MB ordena los tags por votos, y un «funk metal» enterrado hacía que **Red Hot Chili Peppers ocupara un hueco de metal**.

### Metal Archives — contestaron, y el correo ya salió

**Autorizan el scrape** (no comercial + sin martillear) y sugieren filtros de género. Ver `outreach/metal-archives.md` §1b y §3 (**enviado el 2026-07-14**), y **D42/D43/D44**. Se les ofrecen tres puertas (export / API / que scrapeemos) y se les pregunta por las imágenes (**jamás hotlinkear** — D44). Los filtros de género **se declinan** (D43: son el reflejo que la app combate).

**RESPONDIERON el 2026-07-15** (`outreach/` §4, **D48/D49**). Las tres puertas contestadas:
- **Que scrapeemos** — es lo que menos les cuesta; **no tienen API** (quizá futuro, sin promesa). El scrape entra por `IEnrichmentSource`, en el server en Docker `restart: unless-stopped`, **en paralelo con Last.fm** (hosts y claves de match distintos). **D48**, Q9 cerrada.
- **No tienen MBIDs** → emparejado por **nombre+país+año**, ambiguos sin match, miembros aún más estrictos. **R3 confirmado**, es el trabajo duro.
- **Imágenes**: no son suyas para autorizarlas («I can't tell you to use them or not»). Pedro decide **cachear+servir con retirada a petición** (nunca hotlink). **D49**.
- **No comercial ratificado.** Siguen prefiriendo filtros de género (chiste del «shitty black metal») — no cambia D43, `Banish` ya lo cubre.

### La decisión que lo simplifica todo: D47 — **nunca de pago**

Y lo que la provocó no fue MA: **la puerta ya estaba cerrada** (R10). Los ToS de la API de Last.fm dicen *«solely for non-commercial purposes»*, y Last.fm alimenta el pilar de Ranks. **R9**: los términos de Apple para los previews (badge, atribución, no cachear, nada de «valor de entretenimiento independiente») **chocan de frente con el Rito a ciegas** — riesgo vivo, Pedro decidió ignorarlo mientras sea privado.

### LO SIGUIENTE: el pase de Last.fm (Pedro dijo «sí, pero luego»)

**Es la palanca más grande que queda, con diferencia.** Sobre el pool descubrible (100 915):

| Hueco | Cuántos |
|---|---|
| Sin `listeners` (→ sin rank → **pilar de Ranks ciego**) | **88 246 (87 %)** |
| Sin tags | 34 566 |
| Grupos sin tags | 26 758 (de los cuales solo **6 703** son plausiblemente metal → lo único que MA podría cubrir) |

**La clave: `artist.getInfo` de Last.fm devuelve `listeners` Y `tags` en la MISMA llamada.** Un solo rastreo tapa los dos huecos. Hoy `LastFmArtist` solo deserializa `stats.listeners` — **hay que añadir los tags al DTO y al job**.

**Por qué se quedó parado en la letra B** (y la afirmación de «plateau» en §7 es **falsa**): `ListenersJob.cs:65` recorre `.OrderBy(a => a.Name)` — **orden alfabético**. El gestor de tareas de fondo del agente mata los procesos largos cada 10-15 min, así que el pase nunca pasó de la B. Es resumible (filtra `Listeners is null`), pero **hay que lanzarlo donde no lo maten**: en el server, en un contenedor con `restart: unless-stopped`, no en el dev box.

Plan acordado: (1) añadir tags al DTO/job, (2) lanzarlo en el server en Docker, ~25 h a 1 req/s, (3) **re-embeber** las bandas que ganen tags (el texto del embedding incluye los tags → cambia su sitio en el mapa; ~15 min), (4) refrescar `corpus_stats`.

Alternativa gratuita y complementaria, **para el underground que no está en ninguna API**: **propagación por el grafo de miembros** (199 971 aristas ya en la base) — una banda sin tags cuyos miembros tocaron en bandas de black metal, es black metal. Debe guardarse marcado como **derivado**, nunca como afirmado.

### Deuda operativa detectada

- **El puerto 5173 lo ocupa el dev server de OTRO proyecto** (SkadAI). `playwright.config.ts` usa `reuseExistingServer: true`, así que **los E2E le hablaban a la app equivocada** y salían en rojo sin culpa de Grimoire. Workaround usado: levantar el front en `:5174` y un config temporal. **Pedro debe decidir** si se fija el puerto o se desactiva el reuse.

---

## 6c. Sesión 2026-07-15 — sesión larga y autónoma (LEER)

Sesión con Pedro que empezó con la 2ª respuesta de MA y acabó en un despliegue grande, hecho **de forma autónoma** mientras él dormía. Todo commiteado sin firmar y pusheado a `origin/main` en checkpoints (commits `6d1f5c0`, `686e01d`, `7433362`, `3158053`, `3c22b58`). Estado: **desplegado y verificado en vivo**.

### Metal Archives — 2ª respuesta y el scraper, YA CORRIENDO
- MA respondió (`outreach/` §4, **D48/D49**): **que scrapeemos** (menos esfuerzo para ellos; no tienen API), **no tienen MBIDs** (match por nombre+país+año, R3 confirmado), y **las imágenes no son suyas** para autorizarlas → Pedro decide cachear+servir con retirada a petición (D49). No comercial ratificado. Se les mandó un correo de agradecimiento (`outreach/` §5).
- **Scraper construido y desplegado**: `MetalArchivesParser` (puro, tests), `MetalArchivesSource`, `MetalArchivesJob`, verbo `metalarchives`. Importa **temática lírica + género MA + `metal_archives_id`** (enlace a Metallum, invariante 3). Formación/reviews/imágenes = v2.
- **BUG CLAVE resuelto**: MA está tras un WAF que **403ea HTTP/1.1 y sirve HTTP/2**. .NET usa HTTP/1.1 por defecto → todo 403. Fix: `DefaultRequestVersion=Version20` + `RequestVersionOrHigher` (commit `3c22b58`). **Verificado en vivo: casó Pantera con toda su temática lírica.**
- **Corriendo en el server** (`grimoire-metalarchives`, `restart unless-stopped`), resumible vía `metal_archives_checked_at`.
- **OPTIMIZADO 2026-07-15 tarde (D53)**: la deuda de «ordena por listeners DESC → gasta horas en mainstream» resuelta con **dos cambios** (rebuild `grimoire-worker`, contenedor MA relanzado):
  1. **Pool restringido a metal-ish** (`MetalArchivesJob`): MA es solo-metal, así que se saltan las bandas con tags **claramente no-metal** (`ILIKE` sobre `%metal%`, `%thrash%`, `%doom%`, `%grind%`, `%sludge%`, `%djent%`, `%deathcore%`, `%mathcore%`, `%crust%`, `%powerviolence%`); **sin tags = se queda** (desconocido ≠ no-match). Medido: **53 696 pendientes → 31 954** (se saltan 21 536 = 40% mainstream). Explica el match rate previo del 2.7%: la cabeza por listeners era pop/rock que jamás está en Metallum.
  2. **Cadencia 1 → 3 req/s** (`MetalArchivesSource`, `FixedCadenceRateLimiter` 1s→333ms). Decisión de Pedro (el agente recomendó no, por la palabra dada de «≤1 req/s»); **queda anotado que nuestra conducta real diverge de lo que escribimos a MA** — si importa, reescribirles. **Verificado en vivo: 1964/1964 requests = 200, cero 429/403, latencia plana ~122ms → MA no nos capa a 3 req/s.**
  - Resultado: MA de ~semanas a **~4-6h**. El grueso de la mejora es el filtro, no la cadencia.

### Last.fm — pase relanzado con tags
- `LastFmArtist` ahora deserializa **tags** además de listeners → un solo `artist.getInfo` rellena ambos huecos. El job hace backfill de tags solo donde la banda no tiene (no pisa los de MB).
- **Corriendo en el server** (`grimoire-listeners`, `unless-stopped`), ~101 340 pendientes, ordena por nº de releases DESC (bandas reales primero). Ya llevaba 14 366 con listeners de antes.
- La key de Last.fm va por env `GRIMOIRE_LASTFM_APIKEY` (no user-secrets en el contenedor).

### Música clásica — ELIMINADA (D50, supersede D11/D13)
- Pedro: enturbiaba la app de heavy+rock+folk. **Datos purgados en prod**: 23 compositores + 2291 works + 634 orquestas + 81 coros (**preservadas 3 con tag heavy**) + linaje maestro-discípulo. **207 622 → 206 887 artistas.** Migración `RemoveClassicalAddMetalArchives` dropeó la tabla `works`.
- **Código arrancado entero**: modelo `Work`, ficha de compositor (front), `ComposerController`/servicios, verbo `classical`, `EdgeKind.Teacher/Student`, `SeedFamily.Classical`. Se conservan `CreditResolver` y `CoverVersion` (créditos MB genéricos).

### Tres fixes de producto que Pedro reportó
- **Fuente de bandas ilegible (D51, corrige D38)**: la escala de corrosión de Redaction estaba **invertida**. Verificado renderizando las 6 caras: **cut10=limpio, cut100=corroído** (bloque ilegible). El código y `DESIGN.md §3` decían lo contrario → las Known salían feas y `BASE=100` hacía feo TODO rank desconocido. Corregido: BASE=10, mapa Known 10→Nameless 70 (capado, cut100 solo para el reveal transitorio). Verificado por render.
- **Atlas colgaba el PC (#5)**: `AtlasController` enviaba los **175k stars sin límite** → el canvas pinta un gradiente radial por star → 175k por frame = cuelgue. Capado a **muestra de 8000** server-side. Y la base PCA se reconstruía de los 175k embeddings (~½ GB en la API) → capada a muestra de 12k. Verificado: `/api/atlas` devuelve 8000, no cuelga.
- **Espejo→trayectoria en blanco (#4)**: dependía de la proyección Atlas pesada (null → chart vacío). Reescrita para pintar **depth score en el tiempo** (siempre presente, sin proyección).

### Dos features nuevas que pidió Pedro
- **Rito por género, OPCIONAL (D52, supersede D43)**: puedes invocar dentro de un género (black metal, thrash, folk, viking…) **pero sigues catando a ciegas**. Solo el Rito principal (no duelo/semanal). `RiteGenres` (shared), `GET /api/rite/genres`, `RiteFilters.GenreNeedle` con ILIKE substring en `ServablePool`. **Verificado en vivo: serve con genre=black-metal → 200.** Cobertura crece con el pase de Last.fm.
- **Enlaces «Escuchar en» Spotify/Apple Music/Tidal/YouTube Music** en banda y disco: deep-links de búsqueda, coste cero (Grimoire no reproduce, invariante 4). Front puro.

### Deuda/pendiente detectado esta sesión
- ~~**MA ordena por listeners DESC** → gasta las primeras horas en bandas mainstream no-metal~~ **RESUELTO (D53)**: pool restringido a metal-ish + cadencia a 3 req/s. Ver §6c «MA — 2ª respuesta».
- **Imágenes de MA (D49)**: decidido cachear+servir con retirada a petición, **no implementado** aún (v2).
- **Formación + review score de MA (D44)**: no implementado (v2).
- El error `libgssapi_krb5.so.2` en los workers es **benigno** (Npgsql intenta GSSAPI, cae al fallback; la conexión funciona).
- Los dos crawls sobreviven al apagado del PC de Pedro (corren en el server) y a reboots (`unless-stopped`). Se **auto-supervisan**.

### Cómo reanudar los crawls (desde cualquier máquina con el repo)
SSH al server: `ssh -o IdentityAgent=none -i ~/.ssh/id_ed25519 drheavymetal@192.168.1.3`. Ver vivos + progreso:
```
docker ps --format '{{.Names}}\t{{.Status}}' | grep -E 'listeners|metalarchives'
docker exec grimoire-db psql -U grimoire -d grimoire -tA -c "select count(*) filter (where listeners is not null), count(*) filter (where metal_archives_id is not null) from artists;"
```
Si un crawl murió, **relanzarlo continúa** (marcadores: Last.fm `listeners is null`, MA `metal_archives_checked_at is null`):
```
docker run -d --name grimoire-listeners --network grimoire --restart unless-stopped \
  -e ConnectionStrings__Grimoire="Host=db;Port=5432;Database=grimoire;Username=grimoire;Password=grimoire" \
  -e GRIMOIRE_LASTFM_APIKEY=<key de user-secrets / memoria local, NUNCA aquí> \
  -e GRIMOIRE_LISTENERS_LIMIT=300000 go2chaindev/grimoire-worker:latest listeners
docker run -d --name grimoire-metalarchives --network grimoire --restart unless-stopped \
  -e ConnectionStrings__Grimoire="Host=db;Port=5432;Database=grimoire;Username=grimoire;Password=grimoire" \
  -e GRIMOIRE_METALARCHIVES_LIMIT=300000 go2chaindev/grimoire-worker:latest metalarchives
```
El error `libgssapi_krb5.so.2` al arrancar es benigno. **Cuando Last.fm acabe**: re-embeber las bandas que ganaron tags + refrescar `corpus_stats`.

---

## 6d. Sesión 2026-07-15 (continuación con Pedro) — MA opt, Atlas, biografías, y el ROADMAP social

Sesión con Pedro despierto, iterando rápido. Todo commiteado sin firmar y pusheado a `origin/main`.

### Desplegado y verificado en vivo esta sesión
- **MA scraper optimizado (D53)** — pool restringido a metal-ish (53 696→31 954 pendientes, salta 40% mainstream) + cadencia 1→3 req/s (decisión de Pedro sobre la palabra dada de «≤1 req/s»; sin cap: 1964/1964 = 200). MA de semanas a ~4-6h. Commit `88180f2`.
- **Atlas usable** — hover muestra el nombre del grupo flotante; clic **fija una tarjeta** (nombre+rank+«Ver ficha →») en vez de saltar a la ficha y perder el mapa. Front-only (el nombre ya venía en el payload). Commit `79c7733`.
- **Biografías desde Wikipedia (D54)** — verbo `biographies`, match por MBID→Wikidata→enwiki (nunca por nombre), atribución CC BY-SA en la ficha. Corriendo en el server (`grimoire-biographies`, `unless-stopped`, 206 887 pendientes, listeners DESC). Worker+api+front redesplegados, migración `AddWikipediaBiography` aplicada. Verificado: ficha Coldplay 200 con abstract+url. Commit `887415a`. **Pendiente**: re-embeber las bandas que ganen abstract (cambia el texto del embedding).

### ROADMAP acordado con Pedro — «sin MVPs, todo implementado, cada feature entera»
Orden y estado. Cada ola = feature COMPLETA, desplegada y verificada (olas por linealidad de migración EF + choques en Program.cs/rutas/DTOs/locales, NO por trocear).
1. ✅ **Biografías** (D54).
2. ✅ **Ficha** (D55) — temas REALES de MA en la ficha + tags/temas **clicables con dos puertas** (rito ciego acotado + browse con nombres). Endpoints `/api/browse/tag`+`/theme`, `serve` con `genreNeedle`/`themeNeedle`/`themeKind`, índice trigram en `recordings.title`. Desplegado y verificado. **Caveats**: browse minado ~4.5s (optimizable), orden por listeners DESC (mejora al acabar Last.fm), minado aproximado (D17).
3. ✅ **Perfil de usuario** (D56) — página `/profile`: gusto **híbrido EMA+anclas** (`taste_anchors`, botón reconstruir vía `TasteMath.Seed`), Depth Score de identidad + rank breakdown, corte más profundo, stats década/país/género, enlaces a grimorio/espejo/atlas, ajustes (tema/idioma/exportar JSON/nota D28/logout). `ProfileController` + `ProfileAggregates`. Desplegado, verificado end-to-end. Modelo elegido por Pedro (sobre set-media y EMA-solo-añadir). — gestión de gusto (añadir/quitar bandas → recalcular vector, re-sembrar), grimorio+stats por rank, corte más profundo, hogar para trayectoria(C16)/espejo(C20)/dark-twin(B18)/huecos(B23), Depth Score como identidad, ajustes (idioma/tema/**sesiones D28**/exportar grimorio). Mucho es **aflorar lo ya construido**.
4. ✅ **Amigos + D28** (D57) — grafo `friendships` (handle para añadir, request/accept/decline/remove/block) + tabla de rareza + grimorio de amigo + grimorios cruzados (C23, extraído a `GrimoireCrossService`) + amigo en el Atlas (rombo). **D28 SALDADO**: refresh tokens hasheados+rotados+revocables, `/logout`·`/logout-all`·`/sessions`, reuse-detection matizada (carrera benigna vs replay). Verificado end-to-end. Gift/duelo/feed → van con Notificaciones (se entregan por buzón). Caveat: multi-tab localStorage compartido puede pedir re-login (fix cross-tab = follow-up).
5. ✅ **Notificaciones** (D60) — buzón in-app **sondeado** (no push): tabla `notifications`, `NotificationsController` (list/unread-count/read/read-all), campana+badge en sidebar. Eventos: solicitud/aceptada de amistad, **regalo recibido** (`POST /api/friends/{id}/gift` → GiftToken C22 → abre `/gift/{token}` a ciegas). Desplegado y verificado end-to-end. **Extensión en construcción** (Pedro «mételo también»): rarity-surpassed (hook en summon) + duelo ligero (`GET /api/friends/{id}/duel`: Depth Score vs + cruzados + % alineación; challenge por notificación) — Pedro eligió la versión ligera; sin migración. — **in-app, NO push, no instantáneo** (Pedro): tabla `notifications` (user_id, type, actor_id, payload jsonb, created_at, read_at), centro con badge de no-leídas, se refresca al abrir/navegar. NO usa el WebPush/VAPID de movimiento VI. Tipos: solicitud de amistad, aceptada, regalo recibido, «un amigo te superó en rareza», etc.

**Guardarraíles del bloque social**: el ciego se queda (amigos comparten lo *revelado*, regalar es a ciegas — nada de filtrar por lo que le mola al amigo); rareza inversa premia lo oscuro (encaje perfecto); coste cero (D47); opt-in siempre.

**UX pedidas por Pedro a mitad del roadmap (ambas desplegadas):**
- **Sidebar lateral (D58)** — top-bar → rail izquierdo agrupado (El Rito / Explorar / Lo tuyo), activo con barra azufre, perfil como área de usuario abajo (handle + Depth Score). Responsive con drawer móvil. `Sidebar.tsx`.
- **Re-seed desde el perfil (D59)** — «Reelegir tus bandas»: el picker de cold-start del alta (rejilla + Last.fm) en el perfil, con **fresh** (reemplaza) o **add** (une). `POST /api/profile/reseed`. Picker extraído a `useSeedGrid`/`SeedPicker`, compartido con onboarding.

---

## 7. Huecos y pendientes

**Features de grabaciones — RESUELTAS** (import de `recording` de MB: 8 925 364 grabaciones, 99.9% de releases, títulos 100%, duración 91%; 21 418 versiones. Migración `AddRecordingsAndCoverVersions`, scripts `scripts/mb-import/recordings/`):
- **B5** tracklist en la ficha (título + duración), **C7** eje de duración (media excluyendo null), **C21** minería de títulos (léxico cerrado es/en, marcado como aproximación — D17), **C10** grafo de versiones (cover_versions cross-artist), **C26** deriva cromática (el proxy de portadas añade CORS → paleta en cliente sin taint). Todas **desplegadas y verificadas en producción**.
- **C19** eje tímbrico — **hueco declarado**: sin toolchain de audio (numpy/scipy/librosa/pip ausentes; ffmpeg sí, pero sin FFT solo saldría loudness/crest, un stub). D25 ya lo degradó a opcional (rescata 7%). No se finge.

**Tech-debt menor**: el verbo `atlas` de consola es lento a escala (los xy de producción se poblaron con un script rápido de muestra+proyección SQL; el verbo debería adoptarlo para futuros refrescos).

**Enriquecimiento perezoso** (no es código, corre solo): `listeners` recorre Last.fm por horas (la mayoría del underground no está allí); `preview_url` crece al usar el Rito.

**Decisiones pendientes de Pedro**: respuesta de Metal Archives (Q4, temática lírica); revocabilidad de refresh tokens (D28) antes de abrir a más gente; push de las imágenes al registro `go2chaindev/*` (necesita credenciales).

**Operativo**: el gestor de tareas de fondo del agente mata procesos de fondo cada ~10-15 min → los servidores/jobs se relanzan (los jobs de datos son resumibles). En producción esto no aplica (Docker con `restart: unless-stopped`).

---

## 8. Decisiones

El porqué de todo está en `DECISIONS.md` (**append-only**, D1–D40). Las que más condicionan el sistema: D1 (independiente), D4/D26/D31 (el motor en anillo centrado), D5 (mirror = artefacto de build), D6 (coste cero), D9 (fuentes opcionales), D13 (siete movimientos), D18 (grafos), D23 (corpus por expansión), D25 (cobertura de previews 52%), D28 (auth), D33 (aprender del gusto), D38 (dirección de corrosión), D40 (escucha online JIT).
