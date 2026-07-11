# Verificación E2E con Playwright (round 1)

> Estado: **suite en verde**. 19 specs, un navegador Chromium real contra el front Vite (:5173) y la
> API ASP.NET real (:5080) sobre Postgres vivo (:5433). Corrida headless, estable en 3 pasadas
> consecutivas (incluidas 2 con `--retries=0`). Frontera respetada: solo se tocó `src/front/**`
> (config + specs + `package.json` + `.gitignore`). **No se encontró ningún bug de producto** —
> los dos fallos iniciales fueron selectores míos, no cableado roto. Fecha: 2026-07-11.

---

## 1. Montaje de Playwright

- `@playwright/test@1.61.1` como devDependency (`pnpm add -D`).
- **Navegador**: no hizo falta descargar. La caché `~/.cache/ms-playwright` ya tenía `chromium-1228`,
  que es justo el que resuelve la 1.61.1 (`chromium.executablePath()` →
  `chromium-1228/chrome-linux64/chrome`). Verificado con un launch headless de humo antes de escribir
  nada. También hay `google-chrome-stable` en el sistema como respaldo, no fue necesario.
- **Config** (`src/front/playwright.config.ts`): `testDir: ./e2e`, baseURL `http://localhost:5173`,
  Chromium (`Desktop Chrome`), `fullyParallel`, 3 workers, `retries: 1`, trace en fallo.
  `webServer` es un array de dos entradas (API dotnet + front `pnpm dev`) **con
  `reuseExistingServer: true`**: si ya están arriba los reusa, si no los levanta (timeout API 240 s por
  el build frío). Así `pnpm e2e` funciona tanto con los servidores ya en marcha como en frío.
- **Script**: `pnpm e2e` → `playwright test`.
- No colisiona con Vitest: los E2E son `e2e/*.spec.ts`; Vitest solo mira `src/**/*.test.ts`.

## 2. Specs (19 tests en 8 ficheros)

Todos ejercen la UI real contra la API real. Nunca se afirman conteos exactos (el ETL sigue
poblando en segundo plano): se usa «existe» / «≥ 1» y selectores por rol/texto estable de i18next.

| Fichero | Cubre |
|---|---|
| `search.spec.ts` | Trigram tolerante a errata (`darkthron`→Darkthrone), estado vacío, búsqueda semántica (by meaning) con distancias |
| `rite.spec.ts` | **The Rite completo por UI**: anónimo→registro→cold start (elegir 5 bandas)→consola→servir a ciegas (preview vía proxy, sin nombre visible)→Summon→reveal→el grimorio crece. + Last.fm degradado con dignidad |
| `gantt.spec.ts` | Gantt de Darkthrone: barras de miembros (role=link), marcas de release (role=button), hover atenúa la formación inactiva (opacity 0.18) |
| `lineage.spec.ts` | Bloodline (grafo ego con nodos reales en la ficha) + Six Degrees Deep Purple↔Rainbow (camino real vía Don Airey, grado 1) |
| `discovery.spec.ts` | Escenas, Sellos (+click-through a la ficha del sello), Atlas (star field real), In Memoriam, Comparar dos bandas (miembros compartidos + distancia) e Instrumentos raros |
| `composer.spec.ts` | Ficha de compositor Beethoven: obras + maestro Joseph Haydn, y **sin** Gantt de banda |
| `i18n.spec.ts` | Toggle es↔en cambia nav y titulares (The Rite↔El Rito, Search↔Busca) |
| `empty-error.spec.ts` | Id inexistente → estado 404 diseñado; banda sin edges (Gealdýr, D23) → Gantt vacío diseñado, no revienta |
| `auth-pages.spec.ts` | Weekly y Mirror (auth-gated) renderizan su contenido real con sesión + gusto sembrado |

**Cómo se maneja la auth**: el flujo de registro/login se ejercita de verdad por la UI en el spec de
The Rite (registra una cuenta nueva cada corrida). Para las páginas que solo *requieren* estar
logueado pero no son sobre el login (Weekly, Mirror), un helper registra por API e inyecta el par de
tokens en `localStorage` (`grimoire-access-token`/`grimoire-refresh-token`, tal como los guarda
`platform/authStore.web.ts`) vía `addInitScript`. Cada test usa un email único → aislamiento por
usuario, seguro en paralelo.

**Prueba del proxy de preview**: el spec de The Rite escucha `page.on('request')` y afirma que se
disparó una petición a `/api/rite/{token}/audio` (la capability URL proxiada). La URL de origen de
iTunes/Deezer nunca llega al cliente (invariante 4/D10). Comprobado a mano contra la API: algunas
bandas devuelven `200 audio/x-m4p` (~1 MB) y otras `404` porque su `preview_url` aún no está resuelto
— datos vivos; el player degrada sin romper.

## 3. Bugs reales encontrados y corregidos

**Ninguno de producto.** La suite recorre los tentpoles y todos funcionan de extremo a extremo:
la búsqueda abre fichas, The Rite sirve a ciegas y revela al invocar, el grimorio crece, el Gantt
pinta formaciones reales, Six Degrees traza un camino real por miembros compartidos, la ficha de
compositor separa obras y maestro, y las páginas auth degradan bien. Los dos fallos de la primera
pasada fueron **de mis selectores**, no del producto, y se corrigieron en los specs:

1. `discovery.spec.ts` — `getByText('Distance in sound')` (substring, case-insensitive) capturaba
   además el párrafo de ayuda «Tag overlap, distance in sound…». Arreglado con `{ exact: true }`.
2. `auth-pages.spec.ts` — `getByRole('heading', { name: 'The Mirror' })` casaba tanto el h1 «The
   Mirror» como el h2 «The mirror» (case-insensitive). Arreglado con `{ exact: true }`.

## 4. Cómo correrla

```bash
# con Postgres arriba (docker compose dev) y Ollama
cd src/front && pnpm e2e            # reusa API:5080 y front:5173 si están arriba; si no, los levanta
```

## 5. Límites honestos

- **Navegador**: Chromium headless de la caché de Playwright (1228). No se probó Firefox/WebKit — un
  solo proyecto `chromium`. No hay descarga de navegador en esta corrida (ya estaba cacheado); si en
  otro entorno falta, `npx playwright install chromium` lo baja (~150 MB).
- **WebPush**: no se verifica el pop de notificación del SO (`PushController`/`weekly notify`). Es
  imposible de comprobar de forma fiable en headless sin el permiso real del navegador/SO; no se
  finge.
- **Preview real**: se afirma que la petición al proxy sale; no se afirma que suene audio (algunas
  bandas aún no tienen `preview_url` con el ETL en curso, y el autoplay headless puede quedar en
  pausa). Es la aserción honesta posible.
- **Datos vivos**: los conteos crecen mientras corre el ETL; por eso ninguna aserción fija números.
  El spec de The Rite reintenta el anillo hasta 4 veces por si cae vacío (D25). Los pares fijos
  (Darkthrone, Beethoven→Haydn, Deep Purple↔Rainbow vía Don Airey) son MBIDs reales del corpus base y
  se verificaron por API antes de fijarlos.
- **Puertos**: la API y el front se levantaron para esta verificación; se liberan por pid al terminar
  (no se usó `pkill -f Grimoire.Server`, que mataría la propia shell).

## 6. Gate final

`bash scripts/audit.sh --strict` → **PASS**, 0 violaciones (incluidos `pnpm-lint` y `pnpm-build`, que
ven los nuevos ficheros de `e2e/` y la config). Artefactos generados (`test-results/`,
`playwright-report/`) añadidos a `.gitignore`.
