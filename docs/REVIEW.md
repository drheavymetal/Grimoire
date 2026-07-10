# REVIEW — puerta de calidad de cada ola

Este documento define la revisión que **toda ola de desarrollo debe pasar antes de que el orquestador haga commit**. La herramienta mecánica es `scripts/audit.sh`; este documento dice qué comprueba, qué **no puede** comprobar, y qué debe verificar un revisor (humano o LLM) a mano.

---

## 1. El mandato, en criterios comprobables

Mandato de Pedro, literal: no dejar *«espacios sin implementar o cosas solo estéticas sin implementar»*.

Traducido a criterios que se pueden verificar, una ola solo está terminada si:

1. **Todo lo que compila, funciona.** Ningún método lanza `NotImplementedException`, ningún `TODO`/`FIXME` queda en `src/`.
2. **Todo lo que se ve, está conectado.** Ningún componente de UI renderiza datos inventados en el propio fichero. Los datos vienen de un hook de `core/` que llama a la API, o de props que descienden de uno.
3. **Todo fallo se ve.** Ningún `catch` vacío, ningún error silenciado que haga parecer que algo funciona.
4. **Toda excepción al mandato está declarada.** O lleva `// audit-ok: <razón>` en el código (y sale listada en el resumen del audit), o está en `docs/progress/<agente>.md` con su porqué (§4).
5. **Los invariantes de `CLAUDE.md` se cumplen** — en particular el 6 (`core/` sin DOM) y el 7 (i18n desde el primer commit).

---

## 2. Qué comprueba `scripts/audit.sh` mecánicamente

Uso: `scripts/audit.sh` (todo) · `--fast` (sin builds) · `--strict` (un SKIPPED cuenta como fallo — es el modo que debe usar el orquestador antes de commit una vez exista el esqueleto).

| # | Check | Naturaleza | Escape |
|---|---|---|---|
| 1 | Marcadores prohibidos en `src/` (`TODO`, `FIXME`, `HACK`, `XXX`, `NotImplementedException`, `throw new NotSupportedException`) | **Robusto** | `// audit-ok: <razón>` en la misma línea |
| 2 | Bloques `catch` vacíos o solo-comentario en C# | **Robusto** (regex multilínea; no ve catch con llaves anidadas — esos nunca son vacíos) | `// audit-ok:` dentro del bloque |
| 3 | `console.log` en `src/front/src/` (`console.error`/`warn` permitidos) | **Robusto** | `audit-ok:` en la línea |
| 4 | Invariante 6: `core/` no referencia `window`/`document`/`localStorage`/`sessionStorage`/`navigator` ni importa de `ui/` o `platform/` | **Robusto** para lo que busca | **Sin escape.** Relajarlo exige entrada nueva en `DECISIONS.md` |
| 5 | Componentes solo-estéticos en `ui/` | **Heurístico** (ver §3) | `audit-ok:` en el array o en el fichero |
| 6 | Textos de UI que esquivan i18next (nodos de texto JSX y atributos `placeholder`/`title`/`alt`/`aria-label`/`label` con 3+ palabras) | **Heurístico** (ver §3) | `// audit-ok:` en la línea |
| 7 | Puertas de build: `dotnet build -warnaserror`, `dotnet test`, `pnpm lint`, `pnpm build` | **Robusto** | Ninguno. Si el path no existe aún, SKIPPED ruidoso (falla con `--strict`) |

Toda excepción `audit-ok` en vigor se lista en cada ejecución: no puede esconderse. El revisor debe releerlas en cada ola y comprobar que la razón sigue siendo cierta.

---

## 3. Qué NO puede comprobar el script — límites honestos

`audit.sh` detecta **formas** de hueco, no huecos. No es análisis estático riguroso y no sustituye al revisor. En concreto:

**El check 5 (cascarones estéticos) es una aproximación con dos patas:**

- *5a — arrays de contenido hardcodeado*: marca cualquier array con 3+ strings multi-palabra en `ui/`. **Caza** el mock clásico (`const FAKE_BANDS = [...]`). **No caza**: mocks en objetos sueltos (no arrays), mocks con 2 elementos, mocks importados de un fichero `fixtures.ts` fuera de `ui/`, ni un componente que llama al hook correcto pero *ignora* su resultado y pinta otra cosa. **Falso positivo esperable**: listas legítimamente estáticas (nombres de los 7 movimientos, opciones de un select técnico) — se declaran con `audit-ok`.
- *5b — páginas sin `core/`*: marca ficheros en `pages/`/`views/`/`screens/`/`routes/` (o `*Page.tsx` etc.) que no importan nada de `core/`. **Caza** la página-fachada. **No caza**: una página que importa un hook de `core/` y no lo usa, o que lo usa y descarta los datos. **Falso positivo esperable**: páginas genuinamente estáticas (about, legal) — `audit-ok` con razón.

**El check 6 (i18n) solo ve una línea a la vez.** Un texto JSX partido en varias líneas se le escapa. Strings pasados como props arbitrarias (`<Card subtitle="..."/>` con prop no listada) también. Y no verifica que la clave exista en **ambos** catálogos (es/en).

**Y lo fundamental — nada de esto lo puede ver un grep:**

- Si una feature **funciona de verdad** de extremo a extremo.
- Si un endpoint devuelve **datos reales de Postgres** o una constante con forma de dato real.
- Si un test **afirma algo** o solo ejecuta código (asserts triviales, tests sin `Assert`).
- Si un componente está cableado **a la API que dice llamar** o a otra cosa.
- Si el `catch` no-vacío hace algo útil o solo loggea y devuelve un valor que enmascara el fallo.

Para eso está el §4. **Un revisor humano o LLM sigue siendo obligatorio en cada ola.**

---

## 4. Checklist del revisor (por ola)

Antes de dar el visto bueno, el revisor ejecuta `scripts/audit.sh --strict` y además verifica a mano, con la app levantada:

**Datos reales, no constantes**
- [ ] La ficha de artista renderiza filas que vinieron de Postgres vía `GET /api/artists/{id}` — comprobar cambiando un dato en la BD y recargando. Si la UI no cambia, es un cascarón.
- [ ] The Rite sirve un preview real y la query del motor usa el anillo (`WHERE emb <=> taste BETWEEN r_min AND r_max`, D4), no un `ORDER BY ... LIMIT`. Leer el SQL generado o el log de EF.
- [ ] `Summon`/`Banish` **escriben** en `user_taste` (vector y repulsión). Comprobar la fila antes y después.
- [ ] Los enlaces de streaming salen de `artists.links` (jsonb, resuelto en ETL) — cero llamadas a APIs externas en caliente (D10).

**Jobs e importaciones**
- [ ] El job de seed/ETL inserta filas de verdad (contar antes/después) y **re-ejecutarlo no duplica** (upsert por mbid, no insert ciego).
- [ ] Con una fuente de `IEnrichmentSource` desactivada por flag, la vista degrada con dignidad (estado vacío diseñado), no revienta ni deja un hueco roto (Invariante 5, R2).

**Tests que muerden**
- [ ] Elegir 2-3 tests de la ola e **invertir la condición bajo test** (o romper el código que cubren): deben fallar. Un test que pasa igual no es un test.
- [ ] Los tests de `core/` corren **sin navegador** (D12): `pnpm test` en un entorno node puro.

**i18n e invariantes**
- [ ] Cambiar el idioma a `en` y recorrer las vistas de la ola: nada queda en español hardcodeado (y viceversa). Las claves existen en **ambos** catálogos.
- [ ] Releer las excepciones `AUDIT-OK` listadas por el script: ¿sigue siendo válida cada razón?

**Gaps declarados**
- [ ] Todo lo no implementado está en `docs/progress/<agente>.md` con su porqué, y cumple §5.

---

## 5. Gaps aceptables vs. inaceptables

**Aceptable** — un hueco que cumple las tres condiciones:
1. Es **imposible o improcedente en este entorno** (necesita la respuesta de Metal Archives — Q4; necesita el dump de MB de 30 GB que no está en esta máquina; necesita decisión pendiente de Pedro — Q1/Q2/Q5).
2. Está **declarado** en `docs/progress/<agente>.md` con la razón concreta.
3. **No aparenta estar terminado**: o no existe la superficie, o es un estado vacío diseñado que dice qué falta. `MetalArchivesSource` declarado sin implementar (D9) es el ejemplo canónico: el contrato existe, el flag está apagado, nada lo disimula.

**Inaceptable** — cualquiera de estos, aunque esté declarado:
- Un stub que devuelve datos inventados con forma de datos reales.
- Un mock cableado a la UI «para que se vea algo».
- Un `TODO` en `src/`.
- Un componente que parece funcional y no hace nada (botón sin handler, form que no envía, gráfico con datos de ejemplo).
- Un test que no puede fallar.

La regla corta: **un gap aceptable se ve como un gap; uno inaceptable se disfraza de feature.**
