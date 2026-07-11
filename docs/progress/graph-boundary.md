# Error boundary alrededor de los grafos (invariante 5)

> Estado: **hecho y verificado**. Fecha: 2026-07-11. Frontera respetada: solo `src/front/**` — cero
> cambios en backend, console, shared o migraciones (los tocaba otro agente en paralelo). **No
> commit** (por instrucción).

---

## 1. Qué se hizo

Un fallo de render de un grafo (una arista patológica a escala, un blow-up de la simulación
`d3-force`, una coordenada `NaN`) no debe tumbar la ruta entera. Se añade un **React error boundary**
que degrada localmente ese único grafo a un estado vacío diseñado y deja el resto de la página en
pie (invariante 5 / R2).

- **Nuevo componente**: `src/front/src/ui/GraphErrorBoundary.tsx` — `class` component con
  `getDerivedStateFromError` (mapea el throw a `{ hasError: true }`) y `componentDidCatch` (hace
  `console.error` — no se silencia el fallo, permitido por el check 3 del audit). Cuando hay error
  renderiza un fallback privado `GraphErrorFallback` (función interna, no exportada → react-refresh
  contento con un único export), que lee i18n por hook — algo que la clase no puede hacer. Vive en
  `ui/`, nunca en `core/` (invariante 6: toca internals de React).
- **Envuelto alrededor de cada uso de `GraphCanvas`** (los 4 sitios de render, verificado por grep):
  - `ui/lineage/Bloodline.tsx`
  - `ui/pages/GrimoirePage.tsx` (el grafo del grimorio)
  - `ui/pages/ExplorePage.tsx` (la red de splits)
  - `ui/composer/ComposerBody.tsx` (el linaje del compositor)
- **i18n es/en**: nueva clave `graph.renderError` en ambos catálogos.
  - en: `"Could not draw the lineage."`
  - es: `"No se pudo dibujar el linaje."`
  - Voz de la app: directo, sin disculpas (como `"That page has been torn out"`, `"No lineup traced
    yet"`). El estilo del fallback copia el estado vacío existente de `GraphCanvas`
    (`border border-line border-dashed p-6 text-center`, `font-mono text-xs uppercase text-muted`).

## 2. Test que muerde

- Nuevo test en `src/front/e2e/empty-error.spec.ts`: intercepta la respuesta de
  `**/api/lineage/**/bloodline**` y devuelve un payload malformado (`edges` como string, no array),
  de modo que la maquetación headless (`layoutGraph`) lanza en pleno render. Afirma que (a) la ficha
  sobrevive (el `h1` del artista sigue), (b) aparece el fallback `"Could not draw the lineage."`.
  **Comprobado que muerde**: con la barrera quitada el test caería con pantalla rota.

## 3. Verificación

- `pnpm lint` → 0 errores (solo warnings preexistentes en `AuthProvider`/`routes.tsx`, no míos).
- `tsc -b` limpio · `pnpm build` OK · `pnpm test` (unit) 97/97 verde.
- `scripts/audit.sh --fast` → **PASS, 0 violaciones** (marcadores, console.log, invariante 6, i18n,
  cascarones estéticos). No se corrió `--strict` completo porque sus gates de build de dotnet
  ejercerían el backend que el agente de ETL estaba modificando a la vez (migraciones/shared en
  vuelo) — habría medido código ajeno a medio hacer, no el mío. La parte de front del check 7 (lint
  + build) sí se corrió y está verde.
- **Playwright**: 38/42 verde. Los 4 rojos (`artist-extra` pivotal-release, `discovery` labels roster
  y compare shared-members, `lineage` six-degrees) son tests **dependientes de datos** que necesitan
  `artist_edges` / `labels` / embeddings, justo lo que el agente de ETL en paralelo estaba poblando
  (DB en pleno movimiento II: `artist_edges=200063`, `labels=65600`, `embedding IS NOT NULL=175230`
  — todos 0/null en movimiento I). Ninguno de los 4 toca un grafo envuelto ni `GraphErrorBoundary`;
  el `playwright.config.ts` ya advierte que la suite no asume datos estables por el ETL de fondo.
  **El test de la ficha con el grafo bloodline (que ejercita un `GraphCanvas` envuelto) pasó**, igual
  que `edges`, `composer` y `explore-more`.

## 4. Nota de entorno (para quien re-ejecute la suite)

En esta máquina el puerto `:5173` estaba ocupado por el dev server de **CromoWin**, así que Playwright
reusaba la app equivocada (`reuseExistingServer: true`). Además el backend solo permite CORS desde
`:5173`. Para validar se levantó el front de Grimoire en `:5273` con un proxy `/api → :5080` y
`VITE_API_URL=` (mismo origen, sin CORS), y un config de Playwright de usar y tirar con
`baseURL: :5273`. Todo temporal: no se dejó nada de eso en el repo. Para una corrida normal, basta con
que `:5173` sea el front de Grimoire.
