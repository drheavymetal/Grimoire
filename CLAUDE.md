# Grimoire

App de descubrimiento musical para metal y rock (clásica más adelante). Producto independiente, gratuito, sin coste operativo. No forma parte de qlaios.

**Estado**: movimiento I terminado, commiteado y verde. El movimiento II está sin empezar.

---

## Dónde se dejó (2026-07-10)

### Funciona, verificado contra la base viva

307 artistas y 5 320 lanzamientos reales de MusicBrainz en Postgres, 40 países. Búsqueda con `pg_trgm` tolerante a erratas y a diacríticas (`skald` → `SKÁLD`, `darkthron` → `Darkthrone`). Ficha básica, tema claro/oscuro, i18n es/en. Auth con Identity + JWT que **se niega a arrancar fuera de `Development` con la clave de desarrollo**. 22 tests que muerden (comprobado moviendo un umbral). `scripts/audit.sh --strict` en verde.

Folk dentro (D23): Wardruna, Heilung, SKÁLD, Gealdýr, Einar Selvik, Danheim. Myrkur y Faun **no** se sembraron: MusicBrainz devuelve 3 y 7 coincidencias exactas, y se prefirió una fila ausente a una banda equivocada.

### Vacío a propósito, nunca inventado

`artist_edges` = 0 · `labels` = 0 · `listeners` = null · `rank` = null · `embedding` = null · `preview_url` no existe todavía.

### Para levantar el entorno

```bash
docker compose -f build/dev/docker-compose.yml up -d     # postgres+pgvector, host 5433
dotnet run --project src/console/server -- seed           # idempotente; upsert por MBID
dotnet run --project src/web/server                       # escucha en 5080, NO en ASPNETCORE_URLS
cd src/front && pnpm dev
bash scripts/audit.sh --strict                            # gate obligatorio antes de commitear
```
Postgres dev: `grimoire/grimoire` en `localhost:5433`. Ollama con `nomic-embed-text` ya descargado.

### Siguiente paso — movimiento II, dos agentes con rutas disjuntas

Se lanzaron y se pararon antes de escribir nada. **Las migraciones de EF tienen un único dueño**: dos agentes creando migraciones a la vez producen snapshots incompatibles.

1. **ETL** (`src/shared/**`, `src/console/server/**`, migraciones) — relaciones `member_of` con fechas e instrumentos (sin ellas no existe ni el Gantt ni Bloodline ni el criterio de admisión de D23); resolución de previews con **iTunes primero** (41 %) y Deezer de complemento (19 %), nunca al revés; embeddings **centrados** (D26) con el vector medio persistido; y un comando `stats` que mida la distancia al vecino 10/50/90 — **si los tres números salen casi iguales, el arreglo de D26 no funciona a esta escala y el motor sigue roto.**
2. **Ficha** (`src/web/server/**`, `src/front/**`, sin migraciones) — discografía por tipo con la demo visible, portadas del Cover Art Archive proxiadas y cacheadas en disco (también los 404), y estados vacíos diseñados. **No cablear los cortes de corrosión de `Redaction` por rank**: el rank es null y elegir corte por rank renderizaría una mentira. Función pura + tests, y el componente usa el corte base.

### Después: despliegue

Pedro quiere llevarlo a **git y Docker Hub**. Git ya está (`main`, sin firma en los commits, `git@github.com:drheavymetal/Grimoire.git`). Docker Hub **no**:

- `build/production/docker-compose.yml` y los `Dockerfile` están escritos y `docker compose config` los valida, pero **nunca se han construido ni ejecutado**. Eso es lo primero que hay que probar.
- Convención del equipo: registro privado `go2chaindev/<imagen>`. Grimoire **no** es una app del contrato qlaios (D1), así que **no** aplica la skill `publicar-app-aios`. El patrón a seguir es el de `desplegar-cromowin`: build + push de las imágenes a Docker Hub privado, luego pull + up en el servidor.
- Imágenes previstas: `go2chaindev/grimoire-api`, `go2chaindev/grimoire-front`, `go2chaindev/grimoire-worker`.
- **Antes de desplegar nada**: la guarda de arranque exige `Jwt__SigningKey` de 32+ bytes fuera de `Development` (D28), y los refresh tokens **no son revocables** durante 16 días (D28) — decidido, no accidental, pero conviene releerlo antes de abrir la app a nadie.

### Bloqueadores

- **Falta una clave de API de Last.fm** (gratis, inmediata). Sin ella no hay `listeners`, luego no hay ranks, ni Depth Score, ni degradación tipográfica, ni C1 (import de scrobbles para el arranque en frío). **No hay sustituto**: los `nb_fan` de Deezer son una medida circular, hay que estar en Deezer para tenerla.
- **Q1 y Q2** siguen sin ratificar por Pedro (ver `DECISIONS.md`).
- **Q8**: a Gemini le falta entregar el SVG y la marca hermana para tamaños pequeños (D27).

---

---

## Cómo cargar el contexto

Lee en este orden. Cada fichero dice de qué va en su primera línea.

| Fichero | Qué contiene |
|---|---|
| `docs/DECISIONS.md` | **Empieza aquí.** Toda decisión tomada, con su porqué. Preguntas abiertas y riesgos vivos al final |
| `docs/SPEC.md` | Especificación funcional: los tres pilares, catálogo de features, esquema, movimientos |
| `docs/DESIGN.md` | Dirección visual: xerox, degradación tipográfica por rareza, tokens |
| `docs/outreach/metal-archives.md` | Correspondencia con Metal Archives. Lo que se les prometió |
| `docs/spikes/` | Mediciones hechas, con sus números y su sesgo |

Convención: `docs/DECISIONS.md` es **append-only**. Una decisión que cambia no se edita, se supersede con una entrada nueva que la referencia. Perder el razonamiento descartado es perder el motivo por el que no volvemos ahí.

---

## Qué es la app, en tres frases

El problema no es que falten recomendaciones, es que filtramos por etiqueta antes que por oído. Grimoire sirve la banda **a ciegas** —sin nombre, género ni portada, 45 segundos— y solo se revela si te gusta. La rareza va **al revés** que en Spotify: descubrir Metallica no vale nada.

Tres pilares: **The Rite** (cata a ciegas), **Ranks** (rareza inversa a la popularidad), **Bloodline** (linaje real de miembros compartidos).

---

## Invariantes — no se rompen sin una entrada nueva en DECISIONS.md

1. **Coste operativo cero.** Ninguna fuente, modelo ni servicio de pago. Embeddings en el Ollama autohospedado.
2. **No se scrapea Metal Archives.** Comprometido por escrito con sus webmasters el 2026-07-10. Ver `docs/outreach/`.
3. **Toda ficha de banda enlaza a su entrada de Metallum.** También comprometido por escrito.
4. **Grimoire no reproduce música.** Previews de 30–45 s y enlaces a los servicios de streaming. Nada más.
5. **Ninguna fuente de datos es estructural.** Todas detrás de `IEnrichmentSource`, con feature flag. Ninguna vista se rompe si una falta — y faltarán, porque la cobertura es peor justo en las bandas oscuras que son el corazón de la app.
6. **`src/front/src/core/` no toca el DOM.** Ni `window`, ni `document`, ni librerías acopladas. Recibe adaptadores por contexto. Es lo que hará barato el port a React Native.
7. **i18n (es/en) desde el primer commit.** Retrofitearlo es caro.
8. **El mirror de MusicBrainz es un artefacto de build**, nunca un servicio de producción.

---

## Stack

.NET 10 · ASP.NET Core Web API (controllers) · EF Core 10 + Npgsql · PostgreSQL 16 + pgvector + pg_trgm · ASP.NET Identity + JWT Bearer · Serilog · Polly · xUnit.

Front: Vite + React + TS + TanStack Router/Query + Tailwind v4 + shadcn/ui + i18next.

Embeddings: Ollama autohospedado, `nomic-embed-text` (768 dims).

Deploy: Docker Compose + Traefik → Cloudmax.

Monorepo con el patrón de CromoWin: `src/{shared,web,console,front}` + `build/{production,demo}`.

---

## Convenciones de código

1. **El código va siempre en inglés.** Identificadores, comentarios, mensajes de log, mensajes de commit. Sin excepciones ni mezclas.
2. **Llaves siempre, aunque el cuerpo sea de una sola línea.** Nada de `if (x) return;` en una línea suelta.

```csharp
// no
if (artist is null) return null;

// sí
if (artist is null)
{
    return null;
}
```

Se aplican mecánicamente, no de memoria:

- **C#** — `.editorconfig`: `csharp_prefer_braces = true:warning`
- **TypeScript** — ESLint: `curly: ["error", "all"]`

La documentación de `docs/` y este fichero van en **español**. Los textos de interfaz pasan por i18next (`es`/`en`), con las claves en inglés.

---

## Dónde va cada cosa

- **Este repo** — todo lo del proyecto: decisiones, spec, diseño, spikes, correspondencia.
- **Wiki del equipo (`~/Obsidian`)** — nada, por ahora. Decisión de Pedro el 2026-07-10. Cuando el proyecto se estabilice, tocará una entity page y un puntero.
- **Memoria local del agente** — solo un puntero a este `CLAUDE.md`.
