# Rediseño visual v2 — llevar la dirección aprobada a la app real

> Estado: **terminado y verde**. `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones,
> 0 skips; dotnet build/test + pnpm lint/build en verde). Reskin puro: **solo `src/front/**`**, cero
> cambios en `core/**` (lógica), `platform/**`, `src/web/server/**`, `src/shared/**`, `src/console/**`
> ni migraciones. Fecha: 2026-07-11.

Referencia: el artifact aprobado por Pedro (`grimoire-redesign.html`). Este pase replica su lenguaje
—void limpio + hiss de cinta, azufre como único acento, tipografía que se corroe con la rareza,
Rito como ritual, Gantt como héroe— sobre la app viva, sin reescribir su lógica.

Se apoya en D14 (generación de copia), D27 (marca/favicon), D38 (dirección de corrosión) y en la ola
Q (`visual.md`), que ya había cableado Q1 (corte por rank) y Q2 (claro híbrido). Aquel pase fijó el
sistema de tokens; **este lo alinea a los hexes exactos del artifact y le añade la atmósfera, la
marca y el shell** que faltaban.

---

## 1. Sistema de diseño (`styles.css`)

- **Tokens a los hexes exactos del artifact.** Oscuro (la cinta): void `#0B0B0D`, void-2 `#101014`,
  panel `#131317`, bone `#E6E2D9`, dim `#8f8d86`, faint `#5a5852`, line `#26262b`, azufre `#D6C34A`,
  azufre-profundo `#8F7C18`, oxblood `#9B2C2F`. Claro (el flyer): papel `#E5E1DA` (gris frío sucio,
  D27 — nunca crema), y el acento es **siempre la variante profunda** `#8F7C18` (el azufre brillante
  es ilegible sobre papel, 1.60:1 — D27), nunca texto. Se conservan los **nombres de token**
  (`bg/panel/strong/muted/accent/danger/line`) para no romper ninguna clase existente; se añaden
  `--color-faint`, `--color-accent-deep` y `--color-panel2`.
- **Hiss de scanline solo sobre el vacío** (`.dark body::before`): una línea de barrido apenas
  perceptible con `mix-blend-mode:screen`. La cinta no tiene grano, tiene silencio — esta es su única
  textura (D14). En claro no hay scanline.
- **Semitono (flyer) solo en claro**, opt-in por superficie con `.flyer` (splash + reveal + el player
  del Rito). Los cuerpos de lectura larga quedan papel limpio (Q2 híbrido). En oscuro `.flyer` no
  pinta nada.
- **Foco de teclado siempre visible en azufre** (`:focus-visible` global, `outline` azufre + offset) —
  suelo de accesibilidad de DESIGN §7, nunca el outline del navegador ni invisible.
- **`prefers-reduced-motion`**: desactiva el develop del wordmark y el pulso del Rito. El revelado por
  cortes ya estaba gateado en `core/reveal.ts` (intacto).
- Tipografía sin cambios de rol: display `Redaction` (con los 6 cortes graduados ya instalados),
  cuerpo `Archivo`, utilidad/datos `Courier Prime` en mayúsculas con tracking.

## 2. Marca (`ui/Logo.tsx`, nuevo) + favicon (`index.html`)

- **`Mark`** (SVG reutilizable): el anillo de linaje que se deshace en semitono por la derecha
  (pérdida de generación) + el eje vertical de azufre (el eje del tiempo del Gantt). Theme-aware sin
  JS: el anillo es `currentColor` (bone en oscuro, tinta en claro), el eje es el token de acento.
  Primitivas SVG puras (react-native-svg las acepta — invariante 6/D12). **El icono nunca lleva el
  wordmark dentro** (D27).
- **`Wordmark`**: `GR[I]MOIRE` con la `I` en azufre (el mismo eje). Opción `develop` para el revelado
  fotográfico del hero. Es texto inerte con `aria-label`, nunca un heading.
- **`BrandLockup`**: mark + wordmark, el lockup del shell.
- **Favicon SVG** inline (data URI) en `index.html`: la **marca hermana para 16 px** (D27) — sin
  wordmark, trazo grueso, la degradación reducida a tres puntos gruesos, sobre el void para leerse
  como objeto propio. El eje de azufre es el único color.

## 3. Shell / Nav (`ui/Layout.tsx`)

Barra pegajosa sobre el void con backdrop-blur: **marca a la izquierda** (fuera del landmark `nav` —
el icono es la vuelta a casa, no una ruta), **rutas en Courier mayúsculas** con **subrayado de azufre
marcando dónde estás** (`activeProps`), y las utilidades (salir / idioma / tema) a la derecha —
**dentro de `nav`** para no romper `i18n.spec` (que busca el botón ES/EN en el landmark de
navegación). Se conservan **todas** las rutas y **todos los valores i18n** de los labels (los tests
buscan por nombre accesible). Footer Courier con la tagline.

## 4. Pantallas reskineadas

- **Landing / home (`SearchPage`)** — hero de impacto: `Mark` + `Wordmark` que se revela, tesis en dos
  líneas, subtítulo, y CTA de azufre **Comienza el rito**, sobre el flyer (grano en claro, void limpio
  en oscuro). Debajo, la búsqueda intacta (heading `Search the grimoire` conservado — lo exige
  `search.spec`/`i18n.spec`). Nuevas claves i18n `landing.*` en **es y en**.
- **The Rite (`RitePlayer`, `RiteConsole`)** — el ritual: una **señal que pulsa** en la oscuridad
  (anillos concéntricos + núcleo de azufre que late **solo mientras suena**, con glow), contador
  `m:ss`, `LISTEN BLIND`, y las acciones **Invocar / Otra vez / Desterrar** en Redaction (Desterrar en
  oxblood). El **revelado por cortes de corrosión** (`RevealName`) queda intacto y verificado. Lógica
  de audio y coordinación one-at-a-time **sin tocar**.
- **Ficha (`ArtistPage`)** — sin editar el componente: el nuevo sistema de tokens la transforma sola.
  El nombre en su **corte de corrosión por rank** (Absu Obscure → Redaction 70), el **Gantt de héroe**
  con barras por instrumento, meta en Courier con el rank en azufre, discografía por tipo, Bloodline.
- **Búsqueda, Escenas, y el resto (Atlas, compositor, sellos, In Memoriam, grimorio, duelo, década,
  memoriam, comparar, etc.)** — heredan el lenguaje por los tokens compartidos y los componentes
  comunes (nav, footer, `.flyer`, foco de azufre): void atmosférico, Courier de datos, azufre de
  acento, estados vacíos y de error con la misma voz (los textos ya vivían en i18next).

## 5. Verificación en vivo (capturas)

Postgres `:5433` + API Grimoire `:5080` (Development). El `:5173` estaba ocupado por el dev server de
**otro proyecto** (CromoWin), así que se levantó el front de Grimoire en `:5174` con un proxy `/api`
temporal (para no chocar con el CORS de la API, que solo permite `:5173`) — **sin tocar la API**. Los
temporales (config de vite y de playwright de verificación) se borraron al terminar; puertos liberados
por pid.

Capturas comprobadas contra el artifact (en `scratchpad/after-*.png`):

- **landing (oscuro)** — lockup, mark + wordmark con la I de azufre, tesis, CTA de azufre; nav con el
  subrayado de azufre en la ruta activa.
- **landing (claro)** — el flyer: grano de semitono visible, CTA en oro profundo legible (no el azufre
  brillante).
- **rite console (oscuro)** — el ritual: señal con anillos + núcleo de azufre latiendo, `Invocar` de
  azufre, Summon/Again/Banish en Redaction (Banish oxblood).
- **ficha (Absu)** — nombre corroído (corte 70), Gantt de héroe, meta Courier, rank en azufre,
  Bloodline.

### Gate y tests

```
bash scripts/audit.sh --strict → RESULT: PASS (0 violaciones, 0 skips)
dotnet build/test · pnpm lint · pnpm build → verde
```

**E2E (41 specs) contra `:5174`**: **37 passed**. Los **4 fallos son data-dependientes**, no
regresiones: piden bandas concretas que **no están sembradas en este corpus dev parcial** — se
verificó que *Deep Purple*, *Rainbow* y *Don Airey* devuelven **0 hits** en `/api/artists`. Los cuatro
(`artist-extra` pivotal, `discovery` comparar, `explore-more` splits, `lineage` seis-grados) tocan
lógica/páginas que **este pase no editó**. Los specs sensibles a selector/copia (search, i18n,
i18n-routes, rite completo, auth-pages) pasan **10/10**, incluida la afirmación de `Redaction 70` en la
ficha y el flujo completo del Rito (cold start → serve → summon → reveal).

## 6. Pendiente / declarado

- **Los 4 specs data-dependientes** volverán a verde cuando el ETL siembre esas bandas; no dependen de
  este reskin.
- **Reskin por tokens, no por reescritura**, en las pantallas no-tentpole: se ven correctas por el
  sistema compartido, pero no se rediseñó su layout una por una (Atlas/compositor/espejo/duelo/década
  conservan su estructura funcional). Si Pedro quiere tratamiento de héroe específico por pantalla, es
  un pase siguiente.
- **El develop del wordmark** dura 0.9 s; en una captura muy temprana se ve algo blando — resuelve en
  el navegador real y se desactiva con `prefers-reduced-motion`.
- Sin cambios en `DECISIONS.md`: el pase implementa direcciones ya ratificadas (D14/D27/D38 + ola Q),
  no toma decisiones nuevas.
