# Grimoire

App de descubrimiento musical para metal y rock (clásica más adelante). Producto independiente, gratuito, sin coste operativo. No forma parte de qlaios.

**Estado**: movimiento I y movimiento II terminados, commiteados y verdes. El movimiento III (el Gantt) está sin empezar.

---

## Dónde se dejó (2026-07-11)

### Funciona, verificado contra la base viva

**Movimiento I** — 307 artistas «raíz» y 5 320 lanzamientos reales de MusicBrainz en Postgres, 40 países. Búsqueda con `pg_trgm` tolerante a erratas y a diacríticas (`skald` → `SKÁLD`, `darkthron` → `Darkthrone`). Ficha, tema claro/oscuro, i18n es/en. Auth con Identity + JWT que **se niega a arrancar fuera de `Development` con la clave de desarrollo**. `scripts/audit.sh --strict` en verde.

**Movimiento II** — commits `c15eae1` (cimientos de datos + ficha) y `ccb4200` (motor del Rito + UI):
- **Bloodline base**: 2 342 aristas `member_of` con fechas e instrumentos (307 → 2 478 artistas al añadir las filas mínimas de miembro). Miembro oficial ≠ invitado.
- **Previews** iTunes primero, Deezer de complemento (D25), perezoso y por lotes, tras `IEnrichmentSource`. `artists.preview_url` poblándose (80 al cierre del pase; el resto por re-ejecución).
- **Embeddings centrados** (D26 variante C): 309 vectores, vector medio del corpus persistido en `corpus_stats`. El comando `stats` da p10/p50/p90 = 0.85/1.01/1.14, spread 0.29 → **los tres divergen, el motor en anillo es viable a esta escala.**
- **Ficha**: proxy del Cover Art Archive cacheado a disco (también los 404), discografía por tipo con la demo visible, estados vacíos diseñados es/en. Corte **base** de `Redaction` (rank null → nada de corrosión por rank).
- **El Rito** (D30–D34): tablas `user_taste`/`rites`, motor en anillo por **percentiles** (ventana 0.20, repulsión que resta en p20, uno al azar dentro del anillo, sin término de rareza mientras `listeners` sea null), servido **a ciegas** con proxy de audio por URL de capacidad (SSRF cerrado dos veces), `Summon`/`Banish`/`Again` que escriben taste/repulsión, reveal de 600 ms sobre el corte base, explicabilidad C4, arranque en frío por 5 bandas, C3/C13. Verificado de punta a punta contra la base viva (register → seed → serve ciego → audio real → summon revela → grimorio crece → borrar fila lo baja: lee dato vivo).

### Cadena de rank encendida (2026-07-11, D35–D38)

`listeners`/`rank` **poblados**: 290/307 vía Last.fm por MBID (Known 76 · Obscure 104 · Hidden 67 · Forgotten 28 · Nameless 15); 17 null honestos (sin match de mbid, incl. SKÁLD) + las filas de miembro. **Término de rareza** encendido en el motor (sorteo Gumbel-max dentro del anillo, `w_rare`=0.15, **null neutro**). **Depth Score** = Σ puntos por rank de lo invocado (null→0). **C1 import Last.fm** encendido y verificado en vivo. Corte de `Redaction` corregido: **100 nítido … 10 corroído** (`Known→100 … Nameless→10`), `redactionCutForRank` arreglado pero aún sin cablear (Q1).

### Vacío a propósito, nunca inventado

`labels` = 0 · abstract = null · 17 `listeners` null (sin match) → su `rank` null. La invariante de **doble-centrado** (D26/D31): `taste`/`repulsion` se promedian de embeddings ya centrados — el medio de `corpus_stats` **no** se resta otra vez; es solo para un vector de consulta externo crudo.

### Para levantar el entorno

```bash
docker compose -f build/dev/docker-compose.yml up -d     # postgres+pgvector, host 5433
dotnet run --project src/console/server -- seed           # corpus base (idempotente; upsert por MBID)
dotnet run --project src/console/server -- edges          # member_of a 1 req/s (honra 429)
dotnet run --project src/console/server -- previews        # iTunes→Deezer, perezoso por lotes
dotnet run --project src/console/server -- listeners       # Last.fm por MBID → listeners → rank (needs DOTNET_ENVIRONMENT=Development)
dotnet run --project src/console/server -- embeddings      # centrados (D26), Ollama
dotnet run --project src/console/server -- stats           # p10/50/90 deben DIVERGIR
dotnet run --project src/web/server                       # escucha en 5080, NO en ASPNETCORE_URLS
cd src/front && pnpm dev
bash scripts/audit.sh --strict                            # gate obligatorio antes de commitear
```
Postgres dev: `grimoire/grimoire` en `localhost:5433`. Ollama con `nomic-embed-text` ya descargado.

### Siguiente paso — movimiento III (Sangre y tiempo): el Gantt

B7 (Lineup Timeline) y B8 (al pasar por un disco se iluminan los miembros dentro), más B9/B10, C12, C15. Los datos ya están: `member_of` con fechas e instrumentos poblado en el movimiento II. Falta el render — `d3-force` no; el Gantt es su propia técnica (SPEC §9, «los tres grafos»). `LineupIntervalResolver` (con tests) ya resuelve la intersección de intervalos de B8.

### Después: despliegue

Pedro quiere llevarlo a **git y Docker Hub**. Git ya está (`main`, sin firma en los commits, `git@github.com:drheavymetal/Grimoire.git`). Docker Hub **no**:

- `build/production/docker-compose.yml` y los `Dockerfile` están escritos y `docker compose config` los valida, pero **nunca se han construido ni ejecutado**. Eso es lo primero que hay que probar.
- Convención del equipo: registro privado `go2chaindev/<imagen>`. Grimoire **no** es una app del contrato qlaios (D1), así que **no** aplica la skill `publicar-app-aios`. El patrón a seguir es el de `desplegar-cromowin`: build + push de las imágenes a Docker Hub privado, luego pull + up en el servidor.
- Imágenes previstas: `go2chaindev/grimoire-api`, `go2chaindev/grimoire-front`, `go2chaindev/grimoire-worker`.
- **Antes de desplegar nada**: la guarda de arranque exige `Jwt__SigningKey` de 32+ bytes fuera de `Development` (D28), y los refresh tokens **no son revocables** durante 16 días (D28) — decidido, no accidental, pero conviene releerlo antes de abrir la app a nadie.

### Bloqueadores

- ~~**Falta una clave de API de Last.fm**~~ **Resuelto (2026-07-11).** Key obtenida (registrada a `drheavymetal`), en user-secrets de `web/server` y `console/server` (nunca commiteada; solo el `UserSecretsId`). Con ella: `listeners`/`rank` poblados (D37), rareza + Depth Score vivos (D35/D36), C1 encendido. Lo único que **sigue** dependiendo de una decisión —no de la key— es la degradación tipográfica por rank: **gateada por Q1**, no por datos.
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
