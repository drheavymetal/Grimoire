# Verificación E2E con Playwright (round 2 — profundo + adversarial)

> Estado: **suite en verde**. 39 specs en total (los 19 de round 1 + **20 nuevos**), un Chromium real
> contra el front Vite (:5173) y la API ASP.NET real (:5080) sobre Postgres vivo (:5433). Estable en
> pasada con `--retries=0`. Frontera respetada: solo `src/front/**` (specs nuevos + 8 constantes de
> fixture en `e2e/helpers.ts`). **No se rompió ningún spec de round 1.** **No se encontró ningún bug de
> producto** — los 4 fallos iniciales fueron selectores/flujos míos, corregidos en los specs, no
> cableado roto. Fecha: 2026-07-11. Sin commit.

---

## 1. Specs nuevos (20 tests en 11 ficheros)

Amplían a features y caminos que round 1 no ejercitó a fondo. Ninguno afirma conteos exactos salvo
donde el dato es estructural (los 7 de la Weekly, B17). Fixtures fijados verificados contra la base
viva antes de escribirlos.

| Fichero | Cubre | Ángulo |
|---|---|---|
| `lineage-tools.spec.ts` | **C5 el eslabón perdido**: interpola `(A+B)/2` (AC/DC↔Accept), lista vecinos reales con distancia y click-through a la ficha | happy |
| `artist-extra.spec.ts` | **B12 disco-pivote** (Black Sabbath → «The Eternal Idol», badge «Turning point»); **C8 Rabbit Hole** (Cradle of Filth: opt-in, cadena >1 paso **no repetida**, click-through); **degradación tipográfica** (AC/DC Known → Redaction 100 nítida; Chained and Desperate Nameless → Redaction 10 corroída — el corte **cambia** con el rank) | happy + invariante visual |
| `explore-more.spec.ts` | **C6 muro de portadas** (arte real del CAA, click-through); **C15 instrumentos raros** (click-through a la banda del intérprete); **C9 splits** (grafo real o vacío digno) | happy + vacío |
| `mirror-full.spec.ts` | **Espejo completo con auth real**: las 5 secciones montan (C20, C16, B25, B18, B23), estados vacíos diseñados, **sin banner de error**, enlace al Atlas | auth real + vacío |
| `gift.spec.ts` | **C22 regalo** extremo a extremo: envolver (AC/DC con preview) → abrir a ciegas (nombre oculto, audio por el proxy anti-fuga) → revelar → abrir ficha; **token manipulado → «Not a real gift»** (API 404) | happy + adversarial |
| `crossed.spec.ts` | **C23 grimorios cruzados** con **dos cuentas reales**: A pega el código real de B → tres columnas; **código falso → «No grimoire answers»** (API 400) | auth real + adversarial |
| `weekly-push.spec.ts` | **Weekly Rite**: los **7** estables (Band 1…Band 7), servidos a ciegas; **la fontanería de WebPush monta** (control «Notification alerts») | auth real |
| `auth-gate.spec.ts` | **Rutas protegidas sin login** (`/weekly`, `/mirror`, `/grimoire`) → panel de auth, **contenido no filtrado** | adversarial auth |
| `rite-filters.spec.ts` | **Filtros del Rito C13**: con gusto sembrado, abrir filtros, estrechar por país+década (anillo deliberadamente estrecho) → sirve a ciegas **o** vacío digno, nunca error | auth real + borde |
| `i18n-routes.spec.ts` | **i18n es/en en varias rutas** (`/`, `/explore`, `/scenes`, `/lineage`): titulares traducidos, **sin fuga del otro idioma** en la nav (persistencia por `localStorage grimoire-lang`) | invariante 7 |
| `edges.spec.ts` | **Sello inexistente → «No such label»** (API 404), con afordancia de vuelta | adversarial/borde |

### Auth real, como en round 1
Los flujos auth-gated registran por API y `injectAuth` inyecta el par de tokens en `localStorage`
(email único por test → aislamiento en paralelo). El flujo de registro/login por UI ya lo cubre
`rite.spec.ts` de round 1. Para **C22** y **C23** se usa auth real de dos partes: el token del regalo
y el código de grimorio de un **segundo usuario** se obtienen por API y se pegan en la UI del primero.

### Prueba anti-fuga del proxy (C22)
`gift.spec.ts` escucha `page.on('request')` y afirma que sale una petición a `/api/gift/{token}/audio`
(la capability proxiada, D32); la URL de origen de iTunes/Deezer nunca llega al cliente. El nombre de
la banda **no** aparece antes del reveal (a ciegas de verdad).

## 2. Bugs reales encontrados y arreglados

**Ninguno de producto.** Los tentpoles de los movimientos IV–VI funcionan de extremo a extremo:
el eslabón perdido interpola vecinos reales, el Rabbit Hole camina una cadena real no repetida, el
disco-pivote nombra el release con más rotación, el regalo se envuelve/abre/revela y rechaza tokens
manipulados, los grimorios cruzados resuelven contra dos cuentas y rechazan códigos falsos, el Espejo
monta sus cinco secciones, la Weekly sirve sus siete, y las rutas protegidas gatean al anónimo.

Los 4 fallos de la primera pasada fueron **de mis specs**, no del producto:

1. `artist-extra.spec.ts` (Rabbit Hole) — scopeé al `<div>` de la cabecera (`heading.locator('..')`),
   que contiene el `<h2>` y el botón pero **no** el `<ol>` de pasos (hermano del div). Arreglado
   scopeando a la `<section>` contenedora con `.filter({ has: heading })`.
2. `artist-extra.spec.ts` (degradación) — el navegador **normaliza** el `style` inline a comillas
   dobles (`"Redaction 10"`), así que mi regex con comilla simple no casaba. Arreglado a `/Redaction 10"/`
   (la comilla de cierre distingue el corte 10 del 100).
3. `gift.spec.ts` — `GiftButton` tiene **dos pasos**: el primer clic abre el formulario de nota, el
   segundo envuelve. Mi test clicaba una vez. Arreglado esperando el input y clicando de nuevo.
4. `mirror-full.spec.ts` — `getByRole('link', { name: /Atlas/ })` casaba dos enlaces (el de la nav
   «Atlas» y el de la sección «See the Atlas →»). Arreglado a `/See the Atlas/`.

## 3. Cómo correrla

```bash
# con Postgres arriba (docker compose dev) y Ollama
cd src/front && pnpm e2e            # reusa API:5080 y front:5173 si están arriba; si no, los levanta
```

## 4. Límites honestos

- **WebPush / pop del SO**: se verifica que el control de suscripción **monta** su estado diseñado
  («Notification alerts»); **no** se pulsa Suscribir ni se afirma el pop de permiso del navegador/SO —
  imposible de comprobar de forma fiable en headless sin el permiso real. No se finge (igual que round 1).
- **Semántica con Ollama caído → 503**: no se fuerza. Tumbar Ollama exige tocar infraestructura fuera
  de `src/front/**`; round 1 ya cubre la semántica en verde. El manejo 503 digno del cliente existe en
  código (`SemanticController`/`useSemantic`) pero no se ejerció en esta corrida — declarado, no fingido.
- **Datos vivos**: los conteos crecen con el ETL; las aserciones usan «existe»/«≥1»/«sirve-o-vacío»
  salvo los 7 estables de la Weekly (B17 rellena a siete). Los fixtures fijos (AC/DC, Accept, Black
  Sabbath, Cradle of Filth, Chained and Desperate) son MBIDs reales del corpus base, con rank/preview/
  edges verificados por SQL y por API antes de fijarlos.
- **C2 (duelo a ciegas) y C27 (adivina la década)**: **no tienen UI** en el front actual (grep vacío),
  así que no se testean — la misión los pedía «si existe / si tiene UI».
- **Navegador**: un solo proyecto `chromium` (caché 1228), como round 1. Sin Firefox/WebKit.
- **Puertos**: API y front se levantaron para esta verificación y se liberan por **pid** al terminar
  (nunca `pkill -f Grimoire.Server`, que mataría la propia shell).

## 5. Gate final

`bash scripts/audit.sh --strict` → **PASS**, 0 violaciones (incluidos `pnpm-lint` y `pnpm-build`, que
ven los 11 ficheros nuevos de `e2e/`, y `dotnet-build`/`dotnet-test`). Los artefactos de Playwright ya
estaban en `.gitignore` desde round 1.
