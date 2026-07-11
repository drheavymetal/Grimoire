# Movimiento I — Cimientos (esqueleto)

> Estado: **esqueleto vertical funcionando de punta a punta**. Datos reales de MusicBrainz en Postgres, API real, front real que consume la API. Fecha: 2026-07-10.

Este documento registra qué existe, qué se verificó (con el comando y su salida real), qué huecos quedan y por qué, y las decisiones que tuve que tomar y que **no** están en `DECISIONS.md` (marcadas para promoverlas a decisión).

---

## 1. Qué existe

### Monorepo
```
src/
├── shared/GrimoireLibrary/     net10.0 classlib: Models/, Data/, Services/, Migrations/
├── web/
│   ├── server/                 ASP.NET Core 10 Web API (controllers) + Dockerfile
│   ├── GrimoireTest/           xUnit (unit + integration)
│   ├── Grimoire.slnx           4 proyectos
│   └── global.json             SDK 10.0.109
├── console/server/             worker IHostedService (MusicBrainzSeedJob) + Dockerfile
└── front/                      Vite + React + TS + Tailwind v4 + TanStack + i18next + Dockerfile
build/
├── dev/docker-compose.yml      pgvector/pgvector:pg17, host 5433
└── production/docker-compose.yml   Postgres + API + front tras Traefik (no construido este pase)
global.json                     SDK 10.0.109 (raíz)
.editorconfig                   csharp_prefer_braces = true:warning (+ EnforceCodeStyleInBuild)
```

### Dominio (`GrimoireLibrary/Models/`)
`Artist`, `ArtistEdge`, `Release`, `Label`, `Credit`, `UserTaste`, `Rite`, `GrimoireUser` (Identity, PK `Guid`), y los enums `ArtistKind`, `EdgeKind`, `ReleaseType`, `Rank`, `RiteState`.

- **Tablas creadas en `InitialCreate`**: `artists`, `artist_edges`, `releases`, `labels` + tablas de ASP.NET Identity.
- **Modelos SIN tabla** (nada las escribe todavía, según instrucción): `Credit`, `UserTaste`, `Rite`. Reservadas para movimientos II/III.
- Extensiones `vector` y `pg_trgm` creadas por migración. Índices `GIN (name gin_trgm_ops)` y `HNSW (embedding vector_cosine_ops)` creados.
- `Rank` se deriva de `Listeners` con `RankCalculator.FromListeners` (función pura). Umbrales de SPEC §6: >500k Known, 50k–500k Obscure, 5k–50k Hidden, <5k Forgotten, <500 Nameless. `500 000` cae en Obscure (el `>500k` de Known es estricto).

### Worker de seed (`console/server`)
`MusicBrainzSeedJob : IHostedService` que trae **datos reales** de MusicBrainz WS/2. El corpus es **anclas explícitas ∪ búsqueda por tags acotados** (DECISIONS **D23** / SPEC §2):
- **Tags acotados** (metal + folk que orbita el metal), paginado: `(tag:"black metal" OR tag:"death metal" OR tag:"doom metal" OR tag:"heavy metal" OR tag:"viking folk" OR tag:"nordic folk" OR tag:"neofolk" OR tag:"pagan folk" OR tag:"celtic folk" OR tag:"dark folk" OR tag:"folk metal" OR tag:"ritual folk") AND type:group`. **Nunca `folk` a secas** (arrastraría el canon folclórico entero — D23).
- **Anclas por nombre** (se siembran aunque no tengan tags de metal): `Wardruna`, `Heilung`, `Skáld`, `Gealdýr`, `Einar Selvik`, `Danheim`, `Myrkur`, `Faun`. Cada una se resuelve por búsqueda y **coincidencia exacta de nombre no ambigua** de tipo `group`/`person`. Si no resuelve sin ambigüedad, **se registra, se salta y se anota** — nunca se adivina un MBID ni se sustituye por otro artista. `Tartalo Music` **no** es ancla (MB la devuelve como `Person`; sin confirmar por Pedro — D23).
- Por cada artista: lookup `inc=tags+url-rels` y browse de `release-group?artist=<mbid>&limit=25`.
- **Rate limit 1 req/s estricto**: `SemaphoreSlim(1)` + `PeriodicTimer(1s)` en `MusicBrainzRateLimiter`.
- User-Agent exacto `Grimoire/0.1 ( pmanso@go2chain.es )`. `IHttpClientFactory` + resiliencia (Polly v8 vía `Microsoft.Extensions.Http.Resilience`): reintentos exponenciales con jitter que honran 429/503.
- **Idempotente**: upsert por MBID de artista y de release. Los release-groups compartidos (splits / various-artists) se atribuyen al primer artista que los importa y se saltan en el resto (evita violar el índice único de `releases.mbid`).
- Ejecutable a demanda: `dotnet run --project src/console/server -- seed`; termina solo al acabar. Sin `seed` imprime uso y sale (no siembra en cada arranque).

> **Corrección registrada**: en un momento del trabajo revertí el alcance folk (dejando solo los cuatro tags de metal y quitando las anclas), creyendo que la ampliación no había sido pedida. **Era un requisito real** (D23 en `DECISIONS.md` y §2 en `SPEC.md`, ambos commiteados). El registro commiteado es la fuente de verdad, no mi recuerdo de la conversación. Se restauró tal cual estaba especificado.

### Web API (`web/server`)
- Serilog, Swashbuckle con definición de seguridad **JWT Bearer**, health check `AddDbContextCheck` en `/health`, CORS para el origen de Vite (`http://localhost:5173`).
- `Program.cs` aplica `db.Database.MigrateAsync()` al arrancar.
- ASP.NET Identity (`GrimoireUser : IdentityUser<Guid>`) + JWT Bearer HS256, access 15 min / refresh 16 días. Endpoints `register` / `login` / `refresh` funcionando de punta a punta.
- **Guarda fail-fast de arranque**: fuera de `Development`, la app **se niega a arrancar** si `Jwt:SigningKey` es la clave dev commiteada o mide menos de 32 bytes (256 bits). La clave nunca se loguea. Así una clave de repo no puede llegar a producción.
- `ArtistsController`:
  - `GET /api/artists?q=&limit=` — búsqueda trigram real (`EF.Functions.TrigramsAreSimilar` = operador `%`, orden por `TrigramsSimilarity`), usa el índice GIN.
  - `GET /api/artists/{id}` — artista + releases + edges. Devuelve DTOs, no entidades.

### Front (`front`)
- Vite + React 19 + TS + Tailwind v4 (`@theme` con tokens **OKLCH**) + TanStack Router (code-based) + TanStack Query + i18next (es/en completos).
- Contrato de directorios del invariante 6: `core/` (sin DOM: cliente API como factory, hooks, tipos, `rank()` portable), `platform/` (`storage.web.ts`, `theme.web.ts`), `ui/` (componentes web). El cliente API se inyecta en `core` por contexto; `core` no lee `window` ni `import.meta`.
- Tema claro/oscuro por clase `.dark` en `<html>`, persistido, con script anti-FOUC en `index.html`. Semitono solo en modo claro.
- Tipografías: display **Redaction**, cuerpo **Archivo**, utilidad **Courier Prime** (las tres vía `@fontsource`, empaquetadas en el build).
- **Dos páginas reales alimentadas por la API real**:
  1. Búsqueda: input con debounce (300 ms) → `GET /api/artists?q=` → lista (nombre, país, año). Estados de carga/vacío/error como copia dirigida, no "Loading...".
  2. Ficha: `GET /api/artists/{id}` → nombre, país, años, tags, discografía agrupada por tipo.

### Tests (`web/GrimoireTest`)
- `RankCalculatorTests`: 10 casos de frontera (499, 500, 4999, 5000, 49_999, 50_000, 499_999, 500_000, 500_001, 0) + null.
- `LineupIntervalResolverTests`: 7 casos (rangos abiertos por inicio y por fin, fronteras inclusivas exactas, miembro que se fue antes, filtrado de edges no-membership).
- `ArtistsSearchIntegrationTests`: integración real con `WebApplicationFactory<Program>` contra Postgres, pero **sobre una base efímera `grimoire_test` que crea y dropea el propio fixture** — nunca escribe en la base de desarrollo (ver §5). Inserta un fixture sintético (`ZZ Test Artist <guid>`), valida la búsqueda trigram con un typo, y se limpia pase lo que pase. Si Postgres no está accesible (p. ej. si `dotnet test` corre antes de `docker compose up`, como en el orden del gate), se **omite limpio** con `[SkippableFact]` y razón documentada, no se falsea.
- `JwtStartupGuardTests`: tres casos con `WebApplicationFactory<Program>` en `Production` — clave dev por defecto → rechaza arranque; clave corta → rechaza arranque; clave fuerte (≥32 bytes) → arranca. No requieren base de datos (se desactiva la migración de arranque). **Aviso de configuración**: la guarda corre en el código top-level *antes* de `builder.Build()`, así que ni `UseSetting` (config de host, por debajo de `appsettings.json`) ni un `ConfigureAppConfiguration` de `WithWebHostBuilder` (se aplica en el `Build`, demasiado tarde) llegan a la guarda; la única fuente que sí llega es la **variable de entorno** (`Jwt__SigningKey`), que `CreateBuilder` añade al construir. Los tests desactivan el paralelismo del assembly y restauran las variables tras cada caso. El test de clave fuerte **verifica que el override llegó** (compara el valor bindeado) antes de dar por bueno el arranque.

---

## 2. Verificación (comando → salida real)

Toolchain: .NET SDK **10.0.109**, Node **24.13.0**, pnpm **11.5.1**, Docker **29.5.2**, psql 18.4, Postgres del contenedor **17.10**.

**`dotnet build src/web/Grimoire.slnx -warnaserror`**
```
Compilación correcta.
    0 Advertencia(s)
    0 Errores
```

**`dotnet test src/web/Grimoire.slnx`**
```
Correctas! - Con error: 0, Superado: 22, Omitido: 0, Total: 22
```
(La prueba de integración se ejecutó contra el Postgres vivo — 0 omitidas. Incluye los 3 tests de la guarda JWT.)

**`docker compose -f build/dev/docker-compose.yml up -d`** → contenedor `grimoire-postgres-dev` (pg17) escuchando en host `5433`. Ambos compose validan con `docker compose config`.

**`dotnet run --project src/console/server -- seed`**
```
Gathered 301 artists to seed (6/8 anchors resolved, 295 from tag search). Fetching detail...
Seed complete: 301 inserted, 0 updated, 5233 releases upserted.
```
0 fallos de fetch de detalle. **Anclas resueltas: 6/8** — resueltas: Wardruna, Heilung, Skáld, Gealdýr, Einar Selvik, Danheim. **No resueltas (ambiguas → registradas y saltadas, sin adivinar)**: `Myrkur` (3 coincidencias exactas distintas), `Faun` (7 coincidencias exactas distintas). Requieren un desempate manual (o confirmar el MBID con Pedro) antes de sembrarlas.

**Idempotencia**: se re-ejecutó `seed` **sin limpiar la base** (no hubo `down -v` ni truncado; el `MigrateAsync` del job es no-op si no hay migraciones pendientes, y no las había), así que la corrida midió idempotencia real contra las 301 filas ya sembradas. Resultado clave: **cero MBIDs de artista duplicados** (`select count(*) from (select mbid from artists group by mbid having count(*)>1)` = 0). El upsert por MBID actualiza en su sitio al artista/release ya presente, no lo re-inserta. **Matiz honesto**: la búsqueda por tags de MusicBrainz es un servicio vivo y **no devuelve exactamente el mismo conjunto candidato entre corridas** (una banda recién etiquetada puede entrar), así que el total puede variar en unas pocas filas entre ejecuciones — se observó 301 → 302. Eso es deriva del corpus de origen, **no** duplicación. Cifras exactas de la re-ejecución final, verificadas por el coordinador contra la base viva: **6 inserted, 294 updated, 5217 releases upserted**. Los 6 insertados son deriva del corpus (bandas recién etiquetadas en MusicBrainz), no duplicados: 294 actualizaciones en sitio y `dup_artist_mbid = 0`. Estado final: **307 artistas, 40 países, 5320 releases** — 307 y no 308 porque se borró la fila de fixture con MBID fabricado (ver §5).

**`psql -h localhost -p 5433 -U grimoire -d grimoire -c "select count(*), count(distinct country) from artists;"`**
```
 count | count
-------+-------
   301 |    39
```
Releases: **5233**. Extensiones presentes: `vector`, `pg_trgm`. Índices en `artists`: `ix_artists_name` (gin_trgm_ops), `ix_artists_embedding` (hnsw). Las anclas folk sin tags de metal están presentes (Wardruna, Heilung, Skáld, Gealdýr como `Group`; Einar Selvik, Danheim como `Person`).

**`curl -s "http://localhost:5080/api/artists?q=darkthrone"`**
```json
[{"id":"e1d59c3d-e185-4d54-8b7c-8682a650a6e6","name":"Darkthrone","country":"NO","formedYear":1986,"rank":null}]
```
Búsqueda difusa (`q=morbid`) → `Morbid Angel (US)`. Ficha de Darkthrone: 25 releases (23 Album + 2 Compilation), tags `[black metal, death metal, heavy metal]`. (El `id` es un `Guid` que generamos nosotros; cambia si se recrea la base.)

**Auth de punta a punta** (`http://localhost:5080`): `register` → 200 con par de tokens; `login` → tokens (access ~371 chars, expira a 15 min); `refresh` → nuevos tokens; registro duplicado → 409; login inválido → 401; validación (email/clave débil) → 400; usar el access como refresh → 401 (guarda `token_type`). La política real de Identity exige clave con carácter no alfanumérico (400 legítimo). Usuario persistido en `AspNetUsers`.

**Guarda de arranque JWT** — `dotnet test --filter "FullyQualifiedName~JwtStartupGuardTests"`:
```
Correctas! - Con error: 0, Superado: 3, Omitido: 0, Total: 3
```
Los tres casos (en `Production`, sin base de datos porque se desactiva la migración de arranque): `Production_WithDevDefaultKey_RefusesToBoot` (clave dev commiteada → no arranca), `Production_WithShortKey_RefusesToBoot` (clave < 32 bytes → no arranca), `Production_WithStrongKey_Boots` (clave fuerte → arranca, y el test **verifica que el override llegó** comparando el valor bindeado).

**CORS**: preflight desde `Origin: http://localhost:5173` → `Access-Control-Allow-Origin: http://localhost:5173`. **Swagger**: `/swagger/v1/swagger.json` con esquema de seguridad `Bearer` y las 5 rutas.

**`cd src/front && pnpm install && pnpm lint && pnpm build`**
```
pnpm lint  → ✖ 1 problem (0 errors, 1 warning)
pnpm build → ✓ built  (dist con las 3 tipografías: redaction/archivo/courier-prime)
```
El único warning es `react-refresh/only-export-components` en `routes.tsx` (exporta el `router` además de componentes) — no bloquea, es una advertencia de fast-refresh en desarrollo.

---

## 3. Huecos que quedan (y por qué)

- **Bloodline / `artist_edges` vacío.** El worker trae `tags+url-rels` y release-groups, no `artist-rels` (miembros con fechas). La tabla, el modelo y `LineupIntervalResolver` (con tests) están listos para el movimiento III; no se pobló porque no estaba en el alcance de este pase y multiplicaría las llamadas a 1 req/s.
- **`labels` vacío, `releases.label_id` null.** El browse de release-groups no trae sello; requiere bajar a nivel de `release`, fuera de alcance este pase.
- **`Listeners`, `Rank`, `Embedding`, `ImageUrl`, `Release.CoverUrl` = null.** Deliberado: Last.fm necesita key que no tenemos (D6/Q5), el pase de embeddings no se ha corrido, y no se tocó Cover Art Archive. **No se inventó ningún valor.** `Rank` queda null mientras `Listeners` sea null.
- **`Split` como `ReleaseType` nunca se puebla**: MusicBrainz no tiene secondary-type "Split". El enum existe para futuro; hoy se mapean Album/EP/Demo/Live/Compilation.
- **Fechas parciales de release** (solo año o año-mes en MB) se guardan como primer día del periodo. Es una aproximación consciente; la UI solo muestra el año.
- **`Credit`, `UserTaste`, `Rite` sin tabla**: modelos definidos, nada las escribe aún (según instrucción explícita). Se crearán cuando el motor de descubrimiento (mov. II) las use.
- **Producción no construida.** `build/production/docker-compose.yml` + Dockerfiles (server, worker, front/nginx) están escritos y el compose valida con `docker compose config`, pero **no se construyeron ni se corrieron las imágenes** en este pase (fuera del gate de verificación).
- **Front sin captura E2E de navegador.** Verificado por `lint` + `build` y contra la API viva que consume (endpoints, CORS y contrato de datos comprobados). No se levantó un navegador headless para una captura de render.
- **Redaction SÍ está en fontsource (responde Q6).** Al contrario de lo que anticipaba el brief, `@fontsource/redaction` existe en npm (versión **5.2.5**) y se usa como tipo display. Además existen los **cortes de corrosión graduados** que pide D14 para la degradación por rank, cada uno como su propio paquete a la misma versión 5.2.5:
  - `@fontsource/redaction-10`, `@fontsource/redaction-20`, `@fontsource/redaction-35`, `@fontsource/redaction-50`, `@fontsource/redaction-70`, `@fontsource/redaction-100`. **⚠️ Corrección (D38)**: la dirección de esta línea estaba al revés. Lo correcto es **10 = corroído … 100 = nítido** (verificado empíricamente); el mapeo bueno es `Known→100 … Nameless→10`. Ver D38.
  - Este pase **solo cablea** `@fontsource/redaction` (base). Los seis cortes graduados **no** se cablean todavía porque el rank es null; quedan listos para wire en D14 / mov. II (con la dirección corregida de D38).

---

## 4. Decisiones tomadas que NO están en `DECISIONS.md` (promover)

> Marcadas para que se conviertan en entradas `D<n>` si se aceptan. Ninguna contradice un invariante; varias resuelven ambigüedades entre el brief y la SPEC.

1. **Postgres 17, no 16.** El brief y `build/dev` piden `pgvector/pgvector:pg17`; `CLAUDE.md`/SPEC dicen "PostgreSQL 16". Se siguió el brief (pg17). *Conviene fijar la versión canónica.*
2. **Enums almacenados como texto** (`HasConversion<string>`), no `smallint`. La SPEC escribe `rank smallint`. Se eligió texto por legibilidad y por el enum "abierto" de `artist_edges.kind` (D11). Impacto nulo este pase (rank null). *Decisión de esquema a ratificar.*
3. **Convención de nombres snake_case** vía `EFCore.NamingConventions`, para que las columnas coincidan con la SPEC (`sort_name`, `formed_year`…) y para que la query del gate (`count(distinct country)`) funcione sin comillas. Las tablas de Identity conservan su nombre PascalCase estándar (`AspNetUsers`).
4. **Refresh tokens sin estado — exposición de seguridad a registrar.** El refresh es un JWT firmado con claim `token_type=refresh` y 16 días de vida, validado en `/refresh`. No hay tabla de refresh tokens. **Consecuencia**: al no persistirse, **no se pueden revocar ni rotar** — un refresh token robado es válido durante los 16 días completos y no hay forma de invalidarlo (ni logout server-side, ni "cerrar todas las sesiones", ni corte tras cambio de contraseña). Aceptable para el esqueleto; antes de producción real habrá que persistirlos (tabla `refresh_tokens` con rotación y lista de revocación) o acortar mucho la ventana.
5. **Clave de firma JWT en `appsettings.json`** (marcada dev-only). Producción debe inyectarla como secreto (el compose de producción ya la toma de `${JWT_SIGNING_KEY}`). Reforzado con una **guarda de arranque** que impide bootear fuera de `Development` con la clave dev commiteada o una clave < 32 bytes (con tests).
6. **Atribución de release-groups compartidos** (split / various-artists) al primer artista que los importa, saltándolos en el resto. Necesario porque `releases.mbid` es único y el browse por artista los devuelve a ambos lados.

---

## 5. Aislamiento de la base de datos en los tests (corrección)

**Bug corregido** (lo encontró el coordinador en su pasada de verificación). La versión anterior de `ArtistsSearchIntegrationTests` se conectaba a la **base de desarrollo** (`Host=localhost;Port=5433;Database=grimoire`) e insertaba un artista de fixture llamado `Darkthrone` con un MBID fabricado `11111111-1111-1111-1111-111111111111`, **sin borrarlo**. Dos consecuencias graves:

1. **Contaminaba la base que sirve la app.** Cada `dotnet test` escribía en `grimoire`, dejando un `Darkthrone` falso junto al real que importó el seed.
2. **Hacía circular la prueba.** El `curl "/api/artists?q=darkthrone"` que ofrecí como evidencia de que el slice lee datos reales sembrados podía estar satisfecho por mi propio fixture, no por MusicBrainz. (El coordinador re-confirmó el slice de forma independiente contra Wardruna y SKÁLD, así que la conclusión se sostiene; la evidencia que di, no.)

**Un test que escribe en la base que valida se ve verde para siempre.** Arreglo aplicado:

- **No toca la base de desarrollo.** El fixture (`IAsyncLifetime`) **crea una base efímera `grimoire_test`** (vía conexión de mantenimiento a `grimoire`, sin escribir en sus tablas), apunta la app a ella con `ConfigureTestServices` (se sustituye el `DbContextOptions<GrimoireDbContext>` registrado — no se abre nunca la base de desarrollo), y **la borra al final** con `DROP DATABASE ... WITH (FORCE)` (para que conexiones del pool no bloqueen el drop). Se limpia el pool con `NpgsqlConnection.ClearAllPools()` antes de dropear.
- **Se limpia pase lo que pase.** El fixture borra su fila por MBID en un `finally` (aunque falle el assert), y la base entera se dropea en `DisposeAsync`. No se trunca nada.
- **Nombre sintético e imposible de colisionar**: `ZZ Test Artist <guid>` (no una banda real del corpus). La búsqueda se prueba dropeando el último carácter del nombre (typo deliberado) para ejercitar el trigram sobre un nombre único.
- **Skip limpio** si Postgres no está accesible o no se puede crear la base (razón documentada), en vez de escribir en `grimoire`.

Verificado: tras `dotnet test` (y tras `audit.sh --strict`, que corre los gates), la base de desarrollo queda sin filas `ZZ Test%`, sin la fila `1111…`, y `grimoire_test` no existe (dropeada). La fila espuria previa se eliminó a mano:
```
delete from artists where mbid='11111111-1111-1111-1111-111111111111';   -- DELETE 1
select count(*) from artists where mbid='11111111-1111-1111-1111-111111111111';   -- 0
select name, mbid from artists where name='Darkthrone';
--   Darkthrone | af8fd97c-db72-4e30-b2aa-30ebd0c4f1a0   (una sola fila, MBID genuino)
```

**`bash scripts/audit.sh --strict`** → `RESULT: PASS` (7/7, incluidos los gates `dotnet-build`, `dotnet-test`, `pnpm-lint`, `pnpm-build`; en `--strict` los skips cuentan como fallo y hubo 0).

---

## 6. Cómo reproducir el gate

```bash
dotnet build src/web/Grimoire.slnx -warnaserror
dotnet test  src/web/Grimoire.slnx                 # 22/22 (integración omite limpio si no hay DB)
docker compose -f build/dev/docker-compose.yml up -d
dotnet run --project src/console/server -- seed     # ~13 min a 1 req/s; anclas + tags → ~301 artistas reales
psql -h localhost -p 5433 -U grimoire -d grimoire -c "select count(*), count(distinct country) from artists;"
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/web/server   # escucha en :5080
curl -s "http://localhost:5080/api/artists?q=darkthrone"
cd src/front && pnpm install && pnpm lint && pnpm build
```
