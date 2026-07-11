# Grimoire — memoria del proyecto

> Documento de **memoria consolidada**: qué es, qué se construyó, cómo, con qué datos, y cómo está desplegado. Se lee junto a `WORKLOG.md` (**el registro exhaustivo y cronológico de todo lo hecho** — 35 commits, cada ola, cada bug, cada operación de datos, el despliegue paso a paso), `DECISIONS.md` (el porqué de cada decisión, append-only), `SPEC.md` (el qué), `DESIGN.md` (la dirección visual) y `progress/*.md` (el detalle por ola). Última actualización: **2026-07-11**.

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
- **Catálogo real: 207 622 artistas** destilados del dump completo de MusicBrainz (D5), 668 885 releases, 199 971 aristas `member_of` con fechas e instrumentos, 65 600 sellos.
- **Motor de descubrimiento a escala**: 175 230 embeddings centrados (D26), búsqueda en anillo por percentiles, proyección Atlas (xy) para las 175k.
- **Escucha a ciegas online**: previews resueltos **just-in-time** al servir (iTunes→Deezer), stream por proxy anti-leak, **cero audio local** (D40).
- **Rediseño visual v2** implementado en toda la app (identidad metal atmosférica: logo, corrosión por rareza, el Rito como ritual — ver §5).
- **Feature-complete**: los 7 movimientos, B1–B26 y C1–C27 (solo **C19** queda como hueco declarado por falta de toolchain de audio — §7). Incluye tracklists/duración/temas/versiones/paleta sobre **8 925 364 grabaciones** importadas de MB.
- **Enriquecimiento perezoso** corriendo: `listeners`/`rank` (Last.fm, ~horas), `credits` (casi completo).

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
- **Stack Grimoire** en `~/apps/grimoire/` (proyecto compose `grimoire`): `grimoire-db` (pgvector/pg17, volumen `grimoire-db-data`, red interna `grimoire`), `grimoire-api` y `grimoire-front` (en la red externa `traefik_default`; **sin puertos de host** — Traefik enruta por nombre de contenedor: `http://grimoire-front:80`, `http://grimoire-api:8080`).
- **Router Traefik**: `~/apps/traefik/dynamic/grimoire.yml` (añadido, hot-reload). `Host(grimoire.drheavymetal.com)` → front; `&& PathPrefix(/api)` → api. Front y API **mismo origen** → sin CORS en producción.
- **Imágenes**: `go2chaindev/grimoire-{api,front,worker}:latest`, construidas en el dev box y transferidas por `docker save`/`load` (no hay push al registro privado — necesita credenciales del equipo). El front hornea `VITE_API_URL=https://grimoire.drheavymetal.com/api` en build.
- **Datos**: `pg_dump -Fc` de la base dev → restaurado en `grimoire-db` (índices HNSW/GIN reconstruidos). Patrón D5.
- **Secreto**: `Jwt__SigningKey` (64 chars, generado) en `~/apps/grimoire/.env` — **NUNCA commiteado**. Guarda D28 verificada (se niega a arrancar fuera de Development con clave dev o <32 bytes).

### Cómo redesplegar
1. Rebuild imágenes en el dev box (front con la build-arg de la URL). `docker save … | gzip` → `scp` → `docker load` en el server.
2. Si cambió el esquema/datos: `pg_dump -Fc grimoire` → `scp` → parar api/front, `docker exec -i grimoire-db pg_restore -U grimoire -d grimoire --clean --if-exists < dump`, o recrear el volumen. (Los datos frescos = catálogo + enriquecimiento del momento.)
3. `docker compose -f ~/apps/grimoire/docker-compose.yml up -d`. Verificar `curl --resolve grimoire.drheavymetal.com:443:127.0.0.1 https://grimoire.drheavymetal.com/` desde el server (la LAN no ve el IP público por NAT hairpin).

### Exposición declarada (D28)
Refresh tokens **no revocables** durante 16 días (sin logout server-side ni corte tras cambio de contraseña). Aceptado para amigos; revisar antes de abrir a más gente.

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
