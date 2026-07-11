# Pulido del rediseño v2 — llevar el lenguaje bespoke al resto de pantallas

> Estado: **terminado y verde**. `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones,
> 0 skips; dotnet build/test + pnpm lint/build en verde). Reskin puro: **solo `src/front/src/ui/**`**
> + los catálogos i18n (`src/locales/{en,es}.json`). Cero cambios en `core/**`, `platform/**`,
> `src/web/server/**`, `src/shared/**`, `src/console/**` ni migraciones. Fecha: 2026-07-11.

Continúa `docs/progress/redesign.md`: aquel pase fijó tokens, marca, shell y los tentpoles (landing,
El Rito, ficha). Este toma su §6 pendiente —"reskin por tokens, no por rediseño una-por-una"— y le da
a cada pantalla secundaria un tratamiento propio para que **ninguna se sienta de plantilla**. No se
tocó ningún tentpole (Logo, Layout, RiteConsole, ficha, coordinación de audio, cableado de
`redactionCutForRank`).

---

## 1. Dos primitivas de presentación nuevas (`ui/`)

- **`ui/PageHeader.tsx`** — la cabecera de página en la voz de la app: un **eyebrow** en Courier
  (mayúsculas, tracking 0.28em, azufre) que da a cada pantalla su propio "capítulo", luego el título
  display, luego el lead. Sobre la superficie `flyer` el grano de semitono asoma en claro (el flyer
  fotocopiado de D14); en oscuro queda el vacío limpio. El `<h1>` es un heading llano con el string
  exacto —el eyebrow nunca se cuela en su nombre accesible, que es como los 41 specs localizan cada
  página.
- **`ui/SectionHead.tsx`** — un kicker de sección para las páginas-hub: un **tick de azufre**
  (regla corta) marca cada sección como capítulo autoral en vez de un montón de `h2` idénticos.
  Renderiza como fragmento **a propósito**: el `h2` sigue siendo hijo directo de su contenedor, así
  que las queries de los specs (`getByRole('heading', …).locator('..')`) siguen resolviendo a la
  sección y no a un wrapper.

## 2. Pantallas pulidas (antes → después)

| Pantalla | Antes | Después |
|---|---|---|
| **Atlas** (C18) | canvas + `h1` llano | masthead con eyebrow "The periphery, mapped"; **marco v2**: viñeta radial que hunde los bordes del campo estelar en el vacío + **leyenda Courier** (bone = catálogo, azufre = cerca de tu gusto). Canvas intacto (excepción de invariante 6). |
| **In Memoriam** (C12) | lista en grid | masthead limpio (sin flyer: es lectura, D14) + **espina cronológica**: regla vertical con un nodo de azufre por año. Tono cuidado, sin morbo. |
| **Tu grimorio** (C17) | `h1` + link | masthead "What you summoned" con el link a El Rito como `aside`; `SectionHead` en grimorios cruzados (Dark Twin real) y en el grafo. |
| **El Espejo** (C20/C16/B25/B18/B23) | 5× `h2` idénticos | masthead "Your rite, turned back" + `SectionHead` en las cinco secciones (reflejo, trayectoria, anti-rec, Dark Twin, gaps). |
| **Weekly Rite** (B17) | `h1` llano | masthead "Seven, blind". |
| **Linaje** (B19/C5) | 2× `h2` | masthead "Trace the bloodline" + `SectionHead` en Six Degrees y el eslabón perdido. |
| **Explorar** (C6/B24/C15/C24/C25/C9) | flyer + 5× `h2` | masthead vía `PageHeader` + `SectionHead` en las cinco secciones. |
| **Escenas** (B20) · **Sellos** (B21) · **Sello** · **Regalo** (C22) | flyer ad-hoc | unificados a `PageHeader` con eyebrow propio; Sello y Regalo conservan su bleed. |
| **Cold start** (D15) | `h1` llano | masthead "Before the first rite". |
| **Duelo** (C2) · **Década** (C27) | header calmo (familia de El Rito) | eyebrow kicker manteniendo el header restraint del Rito (sin flyer, para no divergir del padre). |
| **Auth/login** | formulario llano | la marca (`Mark`) + eyebrow "Cross the threshold"; el `h1` sigue siendo "The Rite" (lo exige `rite.spec`). |
| **Ficha de compositor** (D11) | `h2` de secciones | tick de azufre sobre Obras y Linaje. |

Cadenas nuevas: **15 `*.eyebrow`** + **`atlas.legendField`/`atlas.legendTaste`**, en **es y en**
(paridad de claves verificada, 0 huérfanas). Sin `console.log`, sin mocks, sin datos inventados: todo
sale de los hooks de `core/` ya existentes.

## 3. Verificación

- **Gate**: `bash scripts/audit.sh --strict` → **PASS** (0/0; dotnet build+test, pnpm lint+build verdes).
- **tsc + eslint** de los ficheros tocados: limpio.
- **E2E (41 specs contra `:5174`)**: los specs sensibles a copia/selector de **todas** las pantallas
  pulidas pasan (i18n, i18n-routes, discovery [escenas/sellos/atlas/memoriam], auth-pages, mirror-full,
  weekly-push, lineage-tools, gift, decade, duel, composer, crossed, search, empty-error, rite): **26/26**
  en la corrida dirigida.
- **Captura en vivo** (en `scratchpad/polish-*.png`): Atlas (marco+viñeta+leyenda), In Memoriam
  (espina), Linaje (ticks de sección), Auth (marca+eyebrow), Escenas en **claro** (grano de flyer +
  eyebrow en oro profundo legible sobre papel, D27). Todo en el lenguaje v2.

## 4. Huecos / declarado

- **Fallos e2e que NO son de este pase** (pre-existentes, verificados): son los data-dependientes ya
  listados en `redesign.md` §6 más un hallazgo nuevo:
  - `discovery` comparar y `lineage` seis-grados — *Deep Purple / Rainbow / Don Airey* devuelven **0
    hits** en este corpus dev.
  - `artist-extra` pivotal — depende de datos de Black Sabbath aún no sembrados.
  - `explore-more` **split network** — `/api/splits` ahora devuelve 2999 nodos con una **arista
    colgante** (nodo inexistente); `d3-force` lanza `node not found` y, **sin error boundary**,
    TanStack Router reemplaza toda la ruta `/explore` por su UI de error. Esto **tumba de rebote** a
    `cover-wall` y `rare-instruments` en la misma página (pasan en aislamiento; fallan en paralelo
    cuando el crash de splits gana la carrera). **Probado en árbol limpio con `git stash`: el crash es
    idéntico sin mis cambios.** El origen (`core/domain/graph.ts` + `d3-force` + falta de boundary +
    datos del ETL) está **fuera de la frontera de este pase** (reskin de `ui/`, no lógica de `core/`).
    Recomendación para un pase futuro: un error boundary local alrededor de los `GraphCanvas` para que
    un grafo con datos sucios degrade a un estado vacío en la voz de la app (invariante 5) en vez de
    romper la ruta entera.
- **Consolas del Rito (Duelo/Década)**: se les añadió eyebrow pero se dejó su header calmo para no
  divergir de `RiteConsole` (tentpole intocable). Si Pedro quiere el masthead completo también ahí, es
  un ajuste de una línea por consola —pero implicaría tocar el tentpole del Rito para mantener la
  coherencia de la tríada.
