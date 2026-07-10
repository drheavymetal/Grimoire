# Grimoire

App de descubrimiento musical para metal y rock (clásica más adelante). Producto independiente, gratuito, sin coste operativo. No forma parte de qlaios.

**Estado**: definición. Sin código. La spec funcional se cierra cuando acabe la ronda de features.

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
