# Grimoire — memoria del proyecto

> Documento de **memoria consolidada**: qué es, qué se construyó, cómo, con qué datos, y cómo está desplegado. Se lee junto a `WORKLOG.md` (**el registro exhaustivo y cronológico de todo lo hecho** — 35 commits, cada ola, cada bug, cada operación de datos, el despliegue paso a paso), `DECISIONS.md` (el porqué de cada decisión, append-only), `SPEC.md` (el qué), `DESIGN.md` (la dirección visual) y `progress/*.md` (el detalle por ola). Última actualización: **2026-07-17** (ver **§6g** — biografías en español (D64), la influencia del Bloodline arreglada 69→1 953 (D65), `pnpm test` cableado al gate, y **un bug de doble escape que introdujo el propio agente** el día anterior. Antes, **§6f** — el bug de clase de los tres crawls: `null` confundía «no hay dato» con «no pude preguntar», y con marcador de por medio eso son datos envenenados o bucles infinitos. Contrato de desenlaces D61 + re-embed por huella D62. Antes, **§6d** — sesión larga con Pedro: optimización MA (D53), Atlas usable, **biografías Wikipedia** (D54), **ficha con temas MA reales + todo clicable** (D55), y el **BLOQUE SOCIAL COMPLETO**: perfil con gusto híbrido (D56), amigos + **D28 sesiones revocables saldado** (D57), sidebar lateral (D58), re-seed desde perfil (D59), **notificaciones in-app + regalar rito + rareza + duelo** (D60). §6c es la sesión anterior del mismo día).

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

- **Bloque social + producto (2026-07-15, D54–D60) — desplegado y verificado**: **biografías Wikipedia** (match por MBID→Wikidata, atribución CC BY-SA), **ficha con temática lírica REAL de MA + tags/temas clicables** (dos puertas: rito ciego acotado / browse), **perfil de usuario** (gusto híbrido EMA+anclas, stats, exportar grimorio, re-seed con la rejilla de onboarding), **sidebar lateral**, **amigos** (grafo por handle, tabla de rareza, grimorios cruzados, amigo en Atlas), **D28 saldado** (refresh tokens rotados+revocables, logout/logout-all/sessions), **notificaciones in-app** (buzón sondeado + badge, no push), **regalar rito a ciegas a un amigo**, **rarity-surpassed** y **duelo ligero**. Detalle en §6d.

Los 60+ commits viven en `origin/main` (github.com:drheavymetal/Grimoire), sin firma GPG.

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
5. ✅ **Notificaciones** (D60) — buzón in-app **sondeado** (no push): tabla `notifications`, `NotificationsController` (list/unread-count/read/read-all), campana+badge en sidebar. Eventos: solicitud/aceptada de amistad, **regalo recibido** (`POST /api/friends/{id}/gift` → GiftToken C22 → abre `/gift/{token}` a ciegas). Desplegado y verificado end-to-end. **Extensión desplegada** (Pedro «mételo también»): rarity-surpassed (hook best-effort en `RiteController.Resolve` tras el summon) + duelo ligero (`GET /api/friends/{id}/duel`: Depth Score vs + cruzados C23 + coseno de gusto; `challenge`→notif) — versión ligera, sin migración, verificado en prod. **Bloque social COMPLETO** (D56–D60 + sidebar D58 + re-seed D59). — **in-app, NO push, no instantáneo** (Pedro): tabla `notifications` (user_id, type, actor_id, payload jsonb, created_at, read_at), centro con badge de no-leídas, se refresca al abrir/navegar. NO usa el WebPush/VAPID de movimiento VI. Tipos: solicitud de amistad, aceptada, regalo recibido, «un amigo te superó en rareza», etc.

**Guardarraíles del bloque social**: el ciego se queda (amigos comparten lo *revelado*, regalar es a ciegas — nada de filtrar por lo que le mola al amigo); rareza inversa premia lo oscuro (encaje perfecto); coste cero (D47); opt-in siempre.

**UX pedidas por Pedro a mitad del roadmap (ambas desplegadas):**
- **Sidebar lateral (D58)** — top-bar → rail izquierdo agrupado (El Rito / Explorar / Lo tuyo), activo con barra azufre, perfil como área de usuario abajo (handle + Depth Score). Responsive con drawer móvil. `Sidebar.tsx`.
- **Re-seed desde el perfil (D59)** — «Reelegir tus bandas»: el picker de cold-start del alta (rejilla + Last.fm) en el perfil, con **fresh** (reemplaza) o **add** (une). `POST /api/profile/reseed`. Picker extraído a `useSeedGrid`/`SeedPicker`, compartido con onboarding.

---

## 6e. Sesión 2026-07-15 noche / 2026-07-16 — revisión de crawls: MA cerrado, bios arreglado (batch)

Revisión con Pedro del estado de los tres crawls de enriquecimiento en el server. Commit `8c6b967` en `origin/main`.

### Metal Archives — TERMINADO, contenedor parado
Scope metal-ish (D53) **agotado**: `0 bands pending`. Aportó, dato real y rico para la ficha (D55): **1 958** hits (`metal_archives_id` + `metal_archives_genre` + enlace a Metallum), **1 338** con `lyrical_themes` (array) reales de MA (ej. Pantera→Groove Metal/Violence,Drugs,Suicide; Powerwolf→Werewolves,Dark myths,Horror). Ratio 1 958/61 986 chequeados = 3.2% (normal: la mayoría de MB no es metal ni está en MA). **⚠️ Falsos positivos por match-por-nombre**: mainstream que comparte nombre con banda oscura de MA (ej. "Plan B" rapero→Thrash Metal, "goat"→Death/Black). Genuino en bandas metal reales, ruido en no-metal populares (riesgo D17). Contenedor `grimoire-metalarchives` **eliminado** (churnaba en bucle `Restarting` cada ~60s sin trabajo por `restart: unless-stopped`).

### Last.fm `listeners` — SANO, avanzando
`112 103 / 206 887` = **54%** (salto enorme desde el stall de la letra B de §6b). Contenedor `grimoire-listeners` vivo, 200s fluyendo. Sigue por horas (el underground no está en Last.fm).

> ⚠️ **CORRECCIÓN (2026-07-16, §6f)**: ese «54%» mide contra los 206 887 artistas, pero el job solo persigue **candidatos** (con tags o releases) = 115 845. La cobertura real era ya **97.6%** — el pase estaba **terminado**, no «avanzando»: llevaba horas re-rastreando los mismos 2 833 misses en bucle.

### Biografías Wikipedia (D54) — ARREGLADO: batch SPARQL + no-envenenar el marcador (commit `8c6b967`)
**Dos bugs cazados y corregidos:**
1. **Throughput cráter** — el pase hacía **1 query SPARQL por artista** contra WDQS (Wikidata Query Service público, compartido y throttleado) → `timed out` + `429` → ~**0.4 artistas/s**, 5× por debajo de su propio limitador. **Fix: batch con `VALUES`** — 1 query resuelve ~50 MBIDs (`WikipediaSource.ResolveBatchAsync`, `WikipediaSummary.ParseArticleTitles`, `WikipediaOptions.BatchSize`, env `GRIMOIRE_WIKIPEDIA_BATCH`=50). Verificado en prod: **~8 artistas/s = 20× más rápido**, `0 left for retry`.
2. **🐛 El marcador se envenenaba** — `WikipediaJob` sellaba `AbstractCheckedAt` **pase lo que pase**, y `ResolveAsync` devolvía `null` igual en miss real, timeout y 429. → un fallo transitorio de WDQS quedaba grabado **para siempre** como «esta banda no tiene bio» y nunca se reintentaba. **Fix: tres desenlaces** `BiographyOutcome.{Matched, NoArticle, Unavailable}` — solo respuestas definitivas sellan; transitorio (timeout/429/5xx en cualquiera de los dos endpoints) deja **sin sellar** para reintento. **Limpieza aplicada en prod**: `UPDATE artists SET abstract_checked_at=NULL WHERE abstract IS NULL AND abstract_checked_at IS NOT NULL` → **5 844** falsos negativos re-encolados, **0 bios perdidas** (el filtro `abstract IS NULL` no toca ninguna biografía guardada). Worker `go2chaindev/grimoire-worker:latest` reconstruido + `docker save|gzip|ssh|load` + contenedor `grimoire-biographies` recreado. Estado tras redesplegar: 195 039 pendientes, bios ~11 900 y subiendo. **Pendiente aún**: re-embeber las bandas que ganen `abstract` (cambia el texto del embedding) + refrescar `corpus_stats` cuando acaben listeners+bios.

**Tests**: 4 nuevos en `WikipediaSummaryTests` (parser batch, case-insensitive MBID, filas incompletas). `audit.sh --strict` verde.

---

## 6f. Sesión 2026-07-16 — el bug de clase de los crawls (D61) y el re-embed por huella (D62)

Pedro pidió el estado del pase de Last.fm; salieron tres bugs de la misma familia. Pidió **«un sistema de 10»** → se arregló la clase, no los síntomas.

### El diagnóstico: Last.fm estaba TERMINADO, y girando

La memoria decía «listeners 54%». Es cierto contra los 206 887 artistas, pero **engañoso**: el job solo persigue **candidatos** (con tags o releases) = 115 845. Real: **113 012 / 115 845 = 97.6%**. Rank vivo: Nameless 39 855 · Forgotten 35 602 · Hidden 25 249 · Obscure 10 371 · Known 1 935. **El pilar de Ranks ya no está ciego.**

Los 2 833 restantes son **misses reales** (Last.fm no indexa el underground). Pero se re-rastreaban **cada ~20 min, 0 resueltos, 38 reinicios** — ~2 833 requests inútiles por vuelta a una API gratuita de la que depende el pilar entero (R10/D47).

### Los tres bugs, una sola causa (D61)

`null` confundía «la fuente dice que no hay nada» con «la fuente no contestó». Cada pase lo manifestó distinto:

1. **Last.fm** — sin marcador (usaba `listeners is null`, que es la respuesta *normal y permanente* de miles de bandas) → bucle infinito.
2. **Wikipedia** — `WikipediaSource.cs:111` metía el título crudo en el path. Títulos con `/` (**Fliflet/Hamre**, **The Yes/No People**, **Bourne Davis Kane**, **DAF / DOS**, **r.o.r/s**) → segmentos extra → **400** → clasificado como transitorio → nunca sella → **458 reinicios** por 5 filas.
3. **Metal Archives** — `MetalArchivesJob.cs:99` sellaba **antes** del `if (band is not null)`: **envenenamiento silencioso**, cada 5xx/timeout grabado como «no está en Metallum» y hoy indistinguible de un miss real. → **Q10**.

**El arreglo (D61)**: `EnrichmentOutcome {Matched, NoData, Unavailable}` + `EnrichmentResult` + `HttpOutcome.IsTransient` (408/429/5xx transitorio; **404 y demás 4xx definitivos**) en `shared/Enrichment`. `IEnrichmentSource.FetchAsync` devuelve el desenlace. Los cinco sources (Last.fm, iTunes, Deezer, Wikipedia, MA) clasifican; los jobs **solo sellan con respuesta definitiva**. Marcador nuevo `listeners_checked_at` (migración `AddEnrichmentMarkers`, backfill `WHERE listeners IS NOT NULL`). Excepción deliberada: **en WDQS todo no-success sigue siendo transitorio** — un 4xx ahí es veredicto sobre *nuestra* query, y sellaría el catálogo entero por un bug nuestro.

De regalo: `ListenersJob` materializaba **115 845 filas enteras** (con embeddings de 768 dims) para filtrar en LINQ → empujado a SQL.

### Re-embed por huella (D62) — cierra el pendiente arrastrado de §6d/§6e

`EmbeddingJob` filtraba `embedding IS NULL`, así que **tener vector nunca significó que fuera cierto**: los artistas que ganaron tags/bio quedaron congelados en el import D5, sin forma de re-embeberlos salvo borrar el catálogo. Ahora `Artist.EmbeddingFingerprint` (SHA-256/128 del texto) → el pase recorre toda la tabla, reconstruye el texto (CPU puro) y **solo llama a Ollama si la huella cambió**. **Reusa la media persistida, nunca la recalcula** — moverla dejaría todos los `user_taste` (D33) en otro espacio y el motor en anillo se torcería en silencio (D26/D31: la media es referencia fija, no estadístico).

### Operativo

Un job batch que acaba debe **quedarse** acabado: `restart: unless-stopped` sobre un job que sale con 0 es lo que convirtió «pase seco» en «contenedor girando». Los contenedores de enriquecimiento pasan a **`restart: on-failure:3`**. Verificado: `grimoire-biographies` acabó → `Exited (0)`, `Restarts=0`.

**Tests**: `HttpOutcomeTests` (transitorio vs definitivo), `WikipediaSummary.SummaryPath` (extraída a shared para que el bug quede bajo test — los 5 títulos reales), huella en `EmbeddingTextBuilderTests`. `audit.sh --strict` verde, 0 violaciones. 525 tests en verde.

### 🔥 INCIDENTE durante el despliegue — la migración tumbó la API (leer antes de escribir otra)

La primera versión de `AddEnrichmentMarkers` traía un backfill `UPDATE artists SET listeners_checked_at = now() WHERE listeners IS NOT NULL` (113k filas). **Tumbó la API en producción**, y el modo de fallo merece recordarse porque no es obvio:

1. Esas filas llevan un **embedding de 768 dims** y viven en un **índice HNSW** → el UPDATE reescribe cientos de MB y churnea el índice. Tardó **minutos**, no segundos.
2. Superó el `CommandTimeout` de 30 s → EF abortó y **revirtió** la migración… **pero Postgres siguió ejecutando el UPDATE** (el cliente se rinde, el servidor no).
3. Ese backend huérfano retenía el **lock exclusivo de `__EFMigrationsHistory`** (EF 9+ lo toma durante toda la migración) → cada arranque de la API se bloqueaba **leyendo el historial**, timeout a los 30 s, crash, reinicio, y **otro UPDATE a la cola**. Cascada. `front:200`, `api:502`.

**Diagnóstico**: `select pid, state, wait_event_type, now()-query_start, query from pg_stat_activity` — mostró el UPDATE huérfano a 3m15s y dos SELECT del historial en `Lock: relation`. **Remedio**: parar la API (deja de reencolar) → `pg_terminate_backend` del huérfano → quitar el backfill de la migración → rebuild + redeploy → **200 al primer intento**.

**Lecciones**: (a) **las migraciones mueven esquema; el movimiento masivo de datos es un paso operativo**, fuera de banda; (b) un `UPDATE` sobre filas con vector+HNSW no es «un update más»; (c) el `ALTER TABLE ... ADD COLUMN NULL` sí es instantáneo — el DDL nunca fue el problema.

**Y el backfill no hacía falta**: «chequeado» se lee como `listeners IS NOT NULL OR listeners_checked_at IS NOT NULL` — tener contador **es** la prueba de que preguntamos. El marcador solo debe desambiguar el caso null. Cero filas selladas, misma información.

### El verbo `stats` era O(n²) — D63

Al ir a verificar D62 salió otra de la misma familia: `StatsJob` recorría cada artista contra **todos** (176 014² = 31 000 millones de distancias, monohilo). Medido: **25 min con un core al 104 %, sin escribir una línea**, camino de ~5-8 h. Reescrito con muestreo (1 500 probes × 15 000 vecinos, semilla fija, exacto por debajo de 2 000 vectores, progreso cada 100): **11 s**. Ver D63.

### Estado al cierre (2026-07-16, todo TERMINADO y verificado)

| | |
|---|---|
| **API / front** | 200/200. Migración `AddEnrichmentMarkers` aplicada, sin queries colgadas |
| **Biografías** | **TERMINADO**: 34 581 bios, **0 pendientes**. Los **5 atascados eran biografías reales** — el escape los rescató (Fliflet/Hamre, DAF/DOS, r.o.r/s, The Yes/No People, Bourne Davis Kane). `Exited (0)`, sin reinicios |
| **Last.fm** | **CERRADO**: `0/2833 resolved, 0 deferred` → los 2 833 eran misses reales, ahora **sellados**; el pase no volverá a preguntar. Cobertura final **113 012 / 115 845 = 97.6 %** de candidatos. `Exited (0)` |
| **Re-embed (D62)** | **TERMINADO**: 3h07 (12:00:34 → 15:08:01), exit 0. **176 014 vectores / 176 014 huellas** = cobertura total. Subió desde 174 495: **+1 519** bandas que ganaron tags/bio ahora tienen señal donde antes no la tenían. `Resuming ... with the persisted mean` ✅ — los `user_taste` intactos |
| **`stats` (D63)** | **HEALTHY**: p10 **0.8298** · p50 **1.0015** · p90 **1.1430** · **spread 0.3133**. Los tres divergen y el spread **mejoró** sobre el 0.29 histórico (que era de 309 vectores). El motor en anillo sano tras el re-embed |

**Los tres contenedores paran solos** (`on-failure:3`) — se acabaron los 458 y 38 reinicios. Régimen estacionario: re-correr cualquiera de los pases cuesta un scan y cero llamadas externas.

**Lo que queda**: **Q10** (¿re-rastrear los misses envenenados de MA?, decisión de Pedro) y el verbo **`atlas`**, que sigue con la misma enfermedad O(n²) que se le curó a `stats` (§7).

---

## 6g. Sesión 2026-07-17 — biografías en español (D64), la influencia del Bloodline (D65), y un bug mío

Sesión con dos subagentes en paralelo, fronteras disjuntas (uno dueño único de migraciones). Ambos entregaron con `audit.sh --strict` verde; el agente principal verificó todo antes de commitear. Commits `12aaff6`, `b4944a7`, `8f3f8ef`.

### Lo desplegado y verificado en vivo

| | Antes | Ahora |
|---|---|---|
| Aristas de influencia (Bloodline) | **69** | **1 953** (28×, D65) |
| Biografías en español | 0 | **13 461** (206 887 chequeados, D64) |
| Gates de `audit.sh` | 4 | **5** (`pnpm test`) |

Ficha verificada en prod: Coldplay devuelve `en` **y** `es`, cada uno con **su URL de atribución** (`es.wikipedia.org`, no la inglesa — la CC BY-SA exige acreditar el texto que realmente se muestra).

### 🐛 El bug del doble escape lo introduje YO el 2026-07-16 (leer: es la lección)

El agente de biografías encontró que `SummaryPath` rompía los títulos con caracteres pre-escapados. **Lo metí yo el día antes**, en `d222b09`, al arreglar los títulos con `/`:

MediaWiki codifica sus URLs canónicas **de forma inconsistente**: `AC/DC` vuelve con la barra cruda y los acentos crudos (`Héroes_del_Silencio`), pero un ampersand vuelve **ya escapado** (`Earth%2C_Wind_%26_Fire`). El código original interpolaba el título **crudo** y por eso funcionaba con los pre-escapados. Mi `Uri.EscapeDataString(title)` arregló los 5 con `/` y **rompió los 1 681 con `%`**:

```
Earth%2C_Wind_%26_Fire      -> 200   (interpolación cruda: correcta)
Earth%252C_Wind_%2526_Fire  -> 403   (mi EscapeDataString: rota)
```

**No dañó datos** (el pase inglés ya había terminado), pero **habría perdido en silencio las ~1 055 bios en español con `%` en la URL** al correr hoy. El agente lo cazó antes de desplegar. Fix correcto: `Uri.EscapeDataString(Uri.UnescapeDataString(title))` — una forma conocida, codificada exactamente una vez, idempotente.

Es exactamente la clase de fallo de D61 —**un miss que fabricamos nosotros, indistinguible de uno legítimo**— cometido por quien acababa de documentarla.

### ⚠️ CORRECCIÓN: las «2 487 bandas envenenadas» NO existían

Durante la sesión el agente principal afirmó que 2 487 bandas con `&` en el nombre (Elvis Costello & The Attractions, Captain Beefheart & His Magic Band…) estaban envenenadas por ese bug, y **se lanzó una limpieza en prod que recuperó 0** y costó ~2 500 peticiones inútiles.

**Era una inferencia de un proxy (`name LIKE '%&%'`) presentada como medición.** Comprobado después contra WDQS: esas bandas **no tienen item de Wikidata con su MBID** (P434 → 0 artículos). Wikidata tiene «Elvis Costello» la persona y «The Attractions» el grupo, pero el artista de MusicBrainz «X & The Y» es una entidad aparte sin item propio. **Eran misses genuinos; el sello era correcto.**

Lección: el radio de daño del doble escape es la intersección de (a) tener item de Wikidata **y** (b) título con carácter pre-escapado. Un nombre con `&` no implica ninguna de las dos.

### `audit.sh` no corría los tests del front (arreglado)

`"test": "vitest run"` existía en `package.json` desde siempre y **el gate nunca lo llamaba** — solo `lint` y `build`. Por eso un test caduco (`riteClient.test.ts`, esperaba el body del Rito sin `genre`, anterior a D52) llevaba días en rojo en `main` mientras el gate obligatorio decía PASS. El test estaba caduco, el código bien. Cableado como quinto gate.

### Juego entre dos: «¿Lo invocaste o lo desterraste?» (D66) — desplegado, apagado

Pedro pidió juego entre dos. Se descartó «adivina la banda» genérico (premia el canon → invierte el pilar de Ranks) a favor de **adivinar el veredicto del amigo sobre una banda de su grimorio, a ciegas**. Asíncrono vía el buzón de D60.

**Lo que destapó**: los destierros **no los veía nadie** hoy (verificado: solo el Espejo y el motor, ambos solo-para-uno-mismo; ningún endpoint de amigos los toca; ni el vector `repulsion` sale de su dueño). El juego es exposición nueva → **opt-in explícito, apagado por defecto** (decisión de Pedro sobre la variante de destierro ciego). Verificado en prod: **0 activado, 3 nunca preguntado** → se desplegó muerto. Rutas vivas y protegidas (401; una ruta inventada da 404, como control).

`VerdictGameOptIn` **nullable**: «nunca preguntado» ≠ «dijo que no» — D61 aplicado a un consentimiento.

**Arranca vacío**: 10 Summoned / 3 Banished en toda la prod. La munición la genera jugar al Rito.

### ✅ Previews múltiples + «adivina la banda» (D67) — DESPLEGADO (`8762ac5`)

Dos agentes en paralelo, fronteras disjuntas (previews = dueño único de migración; el juego, prohibido migrar), programando contra un contrato (`IGuessPreviewSource`) para no esperarse. **El diseño del agente del juego corrigió el del otro**: este pasaba un `Random`, y como el audio va por URL de capacidad que **re-resuelve en cada replay**, habría dado una canción distinta cada vez que el jugador pulsara repetir.

**El hallazgo**: no estábamos limitados a un audio por banda — **tirábamos el resto**. iTunes daba 25 temas y guardábamos 1; Deezer pedía exactamente 1. Ahora se cosechan en `artist_previews`.

| | Antes | Ahora |
|---|---|---|
| Clips | 144 | **1 104** |
| Bandas con audio | 144 | **246** |
| **Bandas con alternativa real** | **0** | **226** |

`preview_url` **intacto** (verificado: el harvest nunca lo escribe). Los +102 son propina de la fase resolve: **100 bandas más que el Rito puede servir**. `0 left unmarked` — ninguna fuente falló.

**El empalme** (`RiteClipSource`, hecho por el agente principal): `Previews` es navegación perezosa y **una colección sin cargar está vacía, no ausente** — sin el `LoadAsync` cada ronda repite el clip ya oído, sin error ni build roto, **la ola anulada a oscuras**. Test verificado rompiendo el código a propósito.

⚠️ **Deuda**: `GameRound.Answer` queda **null** en este juego (un id de 36 chars no cabe en `varchar(16)`) → al revisar una ronda no ves **lo que escribiste**. Necesita columna nueva.

**Dos meteduras de pata del agente principal, para que consten**: le pasó `GRIMOIRE_PREVIEWS_LIMIT` cuando la variable es **singular** (`GRIMOIRE_PREVIEW_LIMIT`), y al corregirlo puso el límite a 100 000 → **arrancó un resolve sobre 100k bandas** (días martilleando iTunes). Parado en 30 s y acotado a 200. El `Limit` gobierna **las dos fases**, así que no se puede cosechar sin resolver algo.

### Deuda detectada, no atacada

- **`DeathsJob` tiene la misma enfermedad de escala**: materializa **98 250 entidades `Artist` completas** (66 554 con vector de 768 dims, cientos de MB) para usar 21 773. Misma familia que `ListenersJob` (D61) e `InfluenceJob` (D65).
- ⚠️ **Riesgo latente de repetir el incidente de §6f**: correr `MigrateAsync` contra una BD atrasada dispara un `CREATE INDEX CONCURRENTLY` sobre 8.9M recordings que **revienta el timeout de 30 s y deja un índice inválido**. Peor: `IF NOT EXISTS` **casa por nombre sin mirar validez**, así que una migración posterior lo marca como aplicado sin construir nada. Le pasó a un subagente en la BD de dev (reparado, dev intacta y verificada: 0 índices inválidos).

---

## 7. Huecos y pendientes

**Features de grabaciones — RESUELTAS** (import de `recording` de MB: 8 925 364 grabaciones, 99.9% de releases, títulos 100%, duración 91%; 21 418 versiones. Migración `AddRecordingsAndCoverVersions`, scripts `scripts/mb-import/recordings/`):
- **B5** tracklist en la ficha (título + duración), **C7** eje de duración (media excluyendo null), **C21** minería de títulos (léxico cerrado es/en, marcado como aproximación — D17), **C10** grafo de versiones (cover_versions cross-artist), **C26** deriva cromática (el proxy de portadas añade CORS → paleta en cliente sin taint). Todas **desplegadas y verificadas en producción**.
- **C19** eje tímbrico — **hueco declarado**: sin toolchain de audio (numpy/scipy/librosa/pip ausentes; ffmpeg sí, pero sin FFT solo saldría loudness/crest, un stub). D25 ya lo degradó a opcional (rescata 7%). No se finge.

**Tech-debt — verbos O(n²) a escala**: el verbo `atlas` de consola sigue siendo lento (los xy de producción se poblaron con un script rápido de muestra+proyección SQL; el verbo debería adoptarlo para futuros refrescos). El verbo `stats` tenía la **misma enfermedad** y ya está curado por muestreo (**D63**, §6f): es el patrón a copiar para `atlas`.

**Enriquecimiento — ya NO es perezoso, está CERRADO (§6f)**: `listeners` **terminado** (97.6 % de los 115 845 candidatos; el resto no está en Last.fm y ya está **sellado**, no se re-pregunta), biografías **terminado** (0 pendientes), MA **terminado** (scope metal-ish agotado), embeddings **al día** (huella por artista, D62 — re-correr cuesta un scan y cero inferencias). Lo único que sigue creciendo solo es `preview_url`, al usar el Rito (JIT, D40).

**Decisiones pendientes de Pedro**: respuesta de Metal Archives (Q4, temática lírica); revocabilidad de refresh tokens (D28) antes de abrir a más gente; push de las imágenes al registro `go2chaindev/*` (necesita credenciales).

**Operativo**: el gestor de tareas de fondo del agente mata procesos de fondo cada ~10-15 min → los servidores/jobs se relanzan (los jobs de datos son resumibles). En producción esto no aplica (Docker con `restart: unless-stopped`).

---

## 8. Decisiones

El porqué de todo está en `DECISIONS.md` (**append-only**, D1–D40). Las que más condicionan el sistema: D1 (independiente), D4/D26/D31 (el motor en anillo centrado), D5 (mirror = artefacto de build), D6 (coste cero), D9 (fuentes opcionales), D13 (siete movimientos), D18 (grafos), D23 (corpus por expansión), D25 (cobertura de previews 52%), D28 (auth), D33 (aprender del gusto), D38 (dirección de corrosión), D40 (escucha online JIT).
