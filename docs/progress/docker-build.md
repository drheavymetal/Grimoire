# Production Docker build — verification (agent Docker-Build)

> Estado: **las 3 imágenes de producción construyen y arrancan verde por primera vez**. Frontera respetada: solo `build/**`, los `Dockerfile`, `.dockerignore`. Un único arreglo de app-config declarado (binding de la API, resuelto dentro del Dockerfile, sin tocar `appsettings.json`). No migraciones. `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones, 0 skips). Dev intacto. Fecha: 2026-07-11.

## Qué se hizo

Primer build y ejecución real de `go2chaindev/grimoire-{api,worker,front}`. Antes de hoy los `Dockerfile` y `build/production/docker-compose.yml` validaban con `docker compose config` pero **nunca se habían construido ni ejecutado**.

Verificación en stack **aislado**: red propia `grimoire-prodtest-net`, Postgres efímero `pgvector/pgvector:pg17` sin puerto al host, contenedores `grimoire-prodtest-*` en puertos libres (8091/8092). No se tocó el dev (`grimoire-postgres-dev` :5433, API :5080, front :5174, jobs). Stack de prueba **desmontado** al terminar.

## Imágenes

| Imagen | Tamaño | Runtime base | Build |
|---|---|---|---|
| `go2chaindev/grimoire-api:test`    | 362 MB | `aspnet:10.0`  | OK |
| `go2chaindev/grimoire-worker:test` | 316 MB | `runtime:10.0` | OK |
| `go2chaindev/grimoire-front:test`  | 95.6 MB | `nginx:alpine` | OK |

(Tag `:test` local; el tag real de release lo pone el flujo de push.)

## Arreglos

1. **`.dockerignore` creado (nuevo, en raíz).** No existía. Sin él, el contexto de build enviaba `node_modules`, `bin/obj`, `dist` de la máquina al daemon, y — crítico — el `COPY src/front/ ./` del front sobrescribía el `pnpm install` limpio de la imagen con el `node_modules`/`dist` del host. Excluye artefactos de host, VCS, `docs/`, `scripts/`, compose de dev/demo y proyectos de test.

2. **API se ataba a `localhost:5080` dentro del contenedor — ARREGLADO en el Dockerfile.** `src/web/server/appsettings.json` lleva un `"Urls": "http://localhost:5080"` de base (para dev), y esa clave de configuración **gana** sobre la variable de entorno `ASPNETCORE_URLS` que ponía el Dockerfile. Resultado: el proceso escuchaba en `localhost:5080` — inalcanzable tras Traefik y en el puerto equivocado (las labels esperan 8080). Arreglo **mínimo y dentro de frontera**: el `ENTRYPOINT` pasa `--urls http://0.0.0.0:8080`; el switch de línea de comandos es la única capa que supera a `appsettings.json`, así que **no se tocó `appsettings.json`** y el dev sigue en 5080. Tras el arreglo la API escucha en `0.0.0.0:8080` y `/health` responde `200 Healthy`.

## Arranque (verificado)

- **API**: `/health` → `200 Healthy`. Con `MigrateOnStartup` (default true) las migraciones se aplican solas contra el Postgres efímero y el `AddDbContextCheck` confirma conexión real.
- **Guarda D28 (probada en ambos sentidos)**:
  - Clave dev commiteada, `ASPNETCORE_ENVIRONMENT=Production` → **el proceso se niega a arrancar** con el mensaje "Refusing to start: Jwt:SigningKey is the committed dev default or shorter than 32 bytes…".
  - Clave `<32 bytes` → **idéntico rechazo**.
  - Clave fuerte (64 bytes aleatorios) → arranca normal.
- **Front**: `/` → `200`, sirve `<title>Grimoire</title>` con `<div id="root">`. Fallback SPA verificado: `/rite/anything` → `200` (nginx `try_files … /index.html`).
- **Worker**: sin argumentos imprime el uso y sale `0` (comportamiento D29, no siembra solo).

## Observaciones (no bloqueantes)

- **`Cannot load library libgssapi_krb5.so.2`** en el arranque de la API: la imagen `aspnet:10.0` no trae Kerberos y Npgsql sondea GSSAPI antes de caer a auth por password. **Cosmético**: `/health` sale `Healthy`, la conexión funciona. Si molesta el log, se silencia añadiendo `libgssapi-krb5-2` a la imagen runtime (coste: tamaño), pero no es imprescindible.

## Qué falta para el push a Docker Hub

- **Credenciales del equipo** para el registro privado `go2chaindev/*` (no disponibles aquí; D1 → patrón `desplegar-cromowin`, no `publicar-app-aios`). Sin ellas **no se hizo push** — declarado, no simulado.
- **La API del front se hornea en build.** El front usa `import.meta.env.VITE_API_URL` con fallback `http://localhost:5080`. El `Dockerfile` del front **no** pasa `VITE_API_URL`, así que la imagen sale apuntando a localhost. Para producción hay que construir el front con `VITE_API_URL=https://<GRIMOIRE_HOST>/api` (build-arg + `ARG`/`ENV` en el Dockerfile del front, o build dedicado por host). Pendiente de decidir en el flujo de despliegue.
- **`build/production/docker-compose.yml` no declara `image:` ni el servicio `worker`.** Sólo `api`, `front`, `postgres`, y con `build:` en vez de `image:`. Para el patrón build+push+pull hay que: añadir `image: go2chaindev/grimoire-{api,front,worker}:<tag>` a cada servicio, añadir el servicio `worker` (run-on-demand, `dotnet Grimoire.Worker.dll <verb>`), y decidir cómo se corren los verbos ETL en el server. No lo toqué (fuera del alcance "construir y verificar"; requiere decisión de topología).
- Recordatorio de seguridad antes de abrir la app: refresh tokens no revocables 16 días (D28).

## Comandos de reproducción

```bash
# build (contexto = raíz del repo)
docker build -f src/web/server/Dockerfile     -t go2chaindev/grimoire-api:test .
docker build -f src/console/server/Dockerfile -t go2chaindev/grimoire-worker:test .
docker build -f src/front/Dockerfile          -t go2chaindev/grimoire-front:test .

# guarda D28 (debe negarse)
docker run --rm -e ASPNETCORE_ENVIRONMENT=Production \
  -e Jwt__SigningKey=dev-only-grimoire-signing-key-change-in-production-0123456789 \
  go2chaindev/grimoire-api:test   # -> "Refusing to start…"
```
