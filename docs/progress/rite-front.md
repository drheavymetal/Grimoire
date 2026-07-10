# Movimiento II — Front del Rito (agente Front del Rito)

> Estado: **terminado y verde**. UI completa del Rito consumiendo el motor real: auth mínima, arranque en frío (picker de 5 + import Last.fm bloqueado con estado digno), servido a ciegas con reproductor por adaptador de audio, slider Comfort↔Abyss con la ventana de percentiles honesta, reveal a 600 ms que respeta `prefers-reduced-motion`, explicabilidad C4, filtros C13, grimorio, y estados vacíos diseñados (204/409). Frontera respetada: **solo se tocó `src/front/**`**. No se tocó `src/shared/**`, `src/web/server/**` ni migraciones. Fecha: 2026-07-11.

Complementa `skeleton.md` (mov. I), `ficha.md` y `rite-engine.md` (motor, cuyo contrato de endpoints consumo sin inventar).

---

## 1. Qué existe (nuevo en este pase)

### core/ (100 % portable, sin DOM — invariante 6)

- **`core/domain/types.ts`** — añadidos los tipos del Rito: `AuthTokens`, `TasteStatus`, `SeedCandidate`, `ServedRite`, `RiteAction`, `RiteState`, `RiteExplanation`, `RiteReveal`, `ResolveResult`, `ServeFilters`, `GrimoireEntry`. Espejo exacto de los DTOs del motor.
- **`core/domain/rite.ts`** (NUEVO) — **el reductor del slider→percentiles**, función pura `comfortToPercentileBand(comfort, widthPct=0.20)` que replica el `RingResolver.Percentiles` del backend (comfort 0 → `[0, 0.20]`, comfort 1 → `[0.80, 1.0]`), más `riskFromComfort`. Así el slider muestra la **ventana de percentiles real** que el motor va a buscar, no una decoración. `RING_WIDTH_PCT = 0.20` = `RiteEngineOptions.RingWidthPct`. (Nota: la variable/tipo se llaman `band`, no `window`, para no disparar el check 4 del audit, que hace grep de la palabra `window`.)
- **`core/domain/reveal.ts`** (NUEVO) — **el gate de `prefers-reduced-motion`**: función pura `shouldAnimateReveal(prefersReducedMotion)` y la constante `REVEAL_DURATION_MS = 600` (DESIGN §3.1). La media query se lee en `platform/`; core solo decide con un booleano, así que el gate se testea sin navegador.
- **`core/audio/types.ts`** (NUEVO) — el **contrato del adaptador de audio** (`AudioAdapter`, `AudioState`): interfaz pura sin DOM. La implementación web vive en `platform/`; un build nativo la sustituiría por expo-av (D12).
- **`core/api/client.ts`** — el `GrimoireClient` se amplía con auth (`register`/`login`/`refresh`) y el Rito (`getTaste`, `seedCandidates`, `seed`, `importLastFm`, `serve`, `resolve`, `grimoire`). El token de acceso se **inyecta por closure** (`getAccessToken`), así que core adjunta el `Authorization: Bearer` sin tocar storage (invariante 6). `serve` devuelve `null` en **204** (anillo vacío), no lanza. La firma pasó de `(baseUrl, fetchImpl?)` a `(baseUrl, options?)`.
- **`core/hooks/`** (NUEVOS) — `useTaste`, `useSeedCandidates`, `useColdStart` (`useSeed`+`useImportLastFm`), `useRite` (`useServe`+`useResolve`), `useGrimoire`. TanStack Query; `useResolve` invalida `taste` y `grimoire` (un summon hace crecer el grimorio y mueve el vector server-side).

### platform/ (adaptadores web; único sitio con DOM)

- **`platform/audio.web.ts`** (NUEVO) — `createWebAudio()`: **el único lugar que toca `HTMLAudioElement`** (invariante 6, D12/invariante). Implementa `AudioAdapter` con play/pause/progress/ended/error por suscripción.
- **`platform/motion.web.ts`** (NUEVO) — `prefersReducedMotion()` lee `matchMedia`.
- **`platform/authStore.web.ts`** (NUEVO) — store singleton de tokens respaldado por `webStorage` (localStorage). El cliente lee el access token de aquí; `AuthProvider` lo sincroniza. **El token no vive en `core/`.**

### ui/ (solo web)

- **`ui/auth/AuthProvider.tsx`** + **`AuthPanel.tsx`** (NUEVOS) — estado de sesión (login/register/logout) y el formulario. Al cargar, si hay refresh token persistido, lo **rota** (`/refresh`) para obtener un access fresco; si falla, cierra sesión. Copia dirigida, errores mapeados (401/409/400).
- **`ui/rite/RitePlayer.tsx`** (NUEVO) — reproductor a ciegas vía el adaptador de audio; el `<audio>` apunta **solo** a la capability URL proxiada. Sin nombre/portada/país/género.
- **`ui/rite/RevealName.tsx`** (NUEVO) — el reveal: el nombre "se revela" (blur+opacidad→nítido) en 600 ms dirigido por estado de React (no animación solo-CSS acoplada desde core). Aterriza en el **corte BASE de Redaction**, nunca un corte por rank (rank es null → sería una mentira; prohibición literal de CLAUDE.md). `prefers-reduced-motion` muestra el nombre resuelto al instante.
- **`ui/rite/ColdStart.tsx`** (NUEVO) — picker de bandas (mín. 5, D15) + import Last.fm que ante **503** muestra "aún no disponible" digno (no error roto), y distingue 404 (sin match) de otros errores.
- **`ui/rite/RiteConsole.tsx`** (NUEVO) — slider Comfort↔Abyss con la etiqueta de percentiles real, filtros C13 (país + rango de años; **formato y rank NO**), botón invocar, reproductor, Summon/Banish/Again, reveal con C4, y estados vacíos 204/blindResolved.
- **`ui/pages/RitePage.tsx`** (NUEVO) — orquesta los tres gates: anónimo → AuthPanel; con sesión y sin taste → ColdStart; con taste → RiteConsole.
- **`ui/pages/GrimoirePage.tsx`** (NUEVO) — lo invocado (`GET /grimoire`), estado vacío diseñado. No muestra rank (es null).
- **`ui/routes.tsx`** — rutas `/rite` y `/grimoire`. **`ui/Layout.tsx`** — nav a Rito/Grimorio + logout. **`main.tsx`** — inyecta `getAccessToken` y envuelve en `AuthProvider`.
- **`locales/en.json` + `es.json`** — claves nuevas en **ambos** catálogos: `nav.*`, `auth.*`, `coldStart.*`, `rite.*`, `grimoire.*`.

### Tests (Vitest, entorno node — corren sin navegador, D12)

- `core/domain/rite.test.ts` (el reductor del slider), `core/domain/reveal.test.ts` (el gate de reduced-motion), `core/api/riteClient.test.ts` (contrato de red: Bearer inyectado, 204→null en serve, 503 de Last.fm como ApiError, cuerpos de seed/resolve/serve). Se actualizó `client.test.ts` a la nueva firma.

---

## 2. Verificación (comando → salida real)

### Gate

```
bash scripts/audit.sh --strict   → RESULT: PASS (Violations 0, Skipped 0)
```
Gates: `dotnet-build`, `dotnet-test`, `pnpm-lint`, `pnpm-build` en verde. `pnpm test` → **23 passed (5 files)**.

### Tests que muerden (mutación → fallo → revert)

- Invertir `shouldAnimateReveal` (`return prefersReducedMotion`) → 2 fallos en `reveal.test.ts`.
- Colapsar la banda (`high = low`) → 4 fallos en `rite.test.ts`.
- Revertido → **23 passed**.

### De punta a punta contra el motor y la base vivos (API :5080, Postgres :5433, front dev :5173)

Con un usuario fresco, ejecutando exactamente las llamadas HTTP que hacen los hooks:

```
1.  register                         → accessToken (373 chars)
2.  GET /taste                       → {"hasTaste":false,...}
3.  seed-candidates?limit=60         → [Absu, Accept, AC/DC, Agathocles, Alice in Chains] (pick 5)
4.  POST /seed                       → {"hasTaste":true,...}
5.  GET /taste                       → {"hasTaste":true,...}
6.  POST /serve {comfort:0.5}        → {token, riskPercentile:0.5, audioUrl}; leak-check name/country/tags → NONE
7.  GET {token}/audio (proxy)        → HTTP 200, Content-Type audio/x-m4p, 1136756 bytes (preview real, origen oculto)
8.  POST resolve summon              → reveal name "Arch Enemy", distance 0.4601
9.  GET /grimoire                    → 1 entrada (creció 0→1)
10. serve + resolve banish           → {"state":"Banished","reveal":null} (sigue a ciegas)
11. serve {decadeFrom:2200}          → HTTP 204 (anillo vacío → estado vacío diseñado)
12. serve con usuario sin taste      → HTTP 409 (→ lleva a arranque en frío)
13. import-lastfm                    → HTTP 503 (→ "no disponible aún" digno)
14. REACTIVIDAD: delete rites en PG  → GET /grimoire pasa de 1 a 0
```

El punto 14 es la prueba anti-cascarón del REVIEW: cambiar un dato en Postgres y re-consultar cambia la UI (la vista lee la base viva, no una constante). El front dev server compila y arranca (`dev-root: 200`), lo que valida el grafo de módulos completo.

Base dejada limpia: `0 usuarios, 0 rites, 0 user_taste`. Corpus intacto (2478 artistas, 80 servibles, 2342 aristas de linaje).

---

## 3. Huecos declarados (y su porqué)

- **Sin captura E2E de navegador headless.** Igual que `skeleton.md`/`ficha.md`: **no hay herramienta de navegador en este entorno**. Se verificó (a) el grafo de módulos vía `pnpm build` + el dev server arrancando, (b) la lógica pura vía tests que muerden, y (c) **toda la ruta de datos que la UI consume** ejecutando las mismas llamadas HTTP contra el motor vivo, incluida la prueba de reactividad contra Postgres. No se renderizó el DOM en un navegador real.
- **C1 Import Last.fm: bloqueado, no roto.** El backend responde 503 (sin key, Q5). La UI lo trata como "aún no disponible" con copia dirigida. La ruta viva (con key) no se puede probar aquí; el camino 503 sí está ejercitado y testeado.
- **Sin degradación tipográfica por rank.** Deliberado: rank es null en todo el corpus y Q1 sigue sin ratificar. `redactionCutForRank` existe y está testeada (de `ficha.md`) pero **ningún componente la llama**; el reveal aterriza en el corte BASE. Los seis paquetes `@fontsource/redaction-N` graduados **no** se cablean.
- **El reveal usa filtro (blur/contraste) dirigido por estado, no los cortes graduados de Redaction.** DESIGN §3.1 describe "empieza en Redaction 100 y se revela hasta su corte". Como el corte final por rank está prohibido (rank null) y aterriza en BASE de todos modos, se implementó el "revelado fotográfico" como transición de filtro dirigida por estado (permitido por D12: "transiciones dirigidas por estado"). Cuando Q1 se ratifique y exista rank, se podrá cambiar a los cortes graduados sin tocar core (el estado y el timing ya viven en `core/reveal.ts`).
- **C2 duelo a ciegas, C27 marcador de década: no construidos** este pase (fuera del alcance del brief; el motor los deja preparados). El reveal ya trae `formedYear`/`country`/`tags` que C27 necesitaría.
- **Refresh de token en reload, no rotación por 401 en caliente.** `AuthProvider` rota el refresh token al cargar; dentro de una sesión el access vive 15 min. No hay reintento automático tras un 401 a mitad de sesión (el usuario re-entra). Aceptable para un puñado de amigos (D28); es un hueco consciente, no un accidente.

---

## 4. Notas para el coordinador

- **Frontera respetada al 100 %**: cero cambios en `src/shared/**`, `src/web/server/**` o migraciones. Todo el contrato del motor se consumió tal cual `rite-engine.md §4` lo documenta; no hizo falta ningún cambio de backend.
- **Discrepancia de dirección de Redaction** (ya anotada en `ficha.md`): DESIGN §3.1 dice "10 casi ilegible … 100 nítida", pero el paquete real es al revés. No afecta a este pase porque el reveal aterriza en el corte BASE, no en uno por rank.
- **`createGrimoireClient` cambió de firma** (`(baseUrl, options)`); el único llamador externo era `main.tsx` (actualizado) y `client.test.ts` (actualizado).
