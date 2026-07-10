# Progreso — documentación

`2026-07-10` · agente de documentación

## Qué se hizo

- **`docs/SPEC.md` reescrito entero.** Se mantiene la estructura previa (§1–§10, B1–B26 sin renumerar) y se añaden: §5.6–§5.11 con las 27 features nuevas (C1–C27), cada una con su fuente de datos y su coste; el arranque en frío (antes solo en D15) como contenido de primera clase en §5.6 y referenciado en §6; una subsección «Los tres grafos: tres técnicas» en §9 explicando por qué Bloodline/Tu grimorio usan `d3-force` + SVG y El Atlas es la excepción a canvas/WebGL; §9.1 con las convenciones de código; y el esquema de §10 ampliado con `artists.themes`/`themes_source`, `artists.xy`, `credits.source`/`confidence` y la tabla `audio_features` (marcada como gateada a que C19 supere el spike v2). La tabla de movimientos (§8) se actualizó para dar hueco a las 27 features nuevas manteniendo siete movimientos; el Movimiento I sigue sin depender de ningún otro.
- **`docs/DESIGN.md` creado.** Desarrolla D14: generación de copia, los dos artefactos (flyer fotocopiado / cinta), la firma tipográfica `Redaction` con corrosión por rank, el reveal como revelado fotográfico de 600 ms, los tres roles tipográficos, la paleta monocroma con azufre `#D6C34A` como único acento y oxblood solo para `Banish`, la ausencia de héroe fotográfico en la ficha, y el suelo de accesibilidad. Se marca explícitamente arriba y al final que **Q1 y Q2 siguen sin respuesta de Pedro** — la dirección es propuesta, no ratificada.
- **`docs/DECISIONS.md` — cinco entradas nuevas, D18 a D22**, append-only, sin tocar ninguna entrada existente:
  - D18 — render de grafos con `d3-force`+SVG, no `react-force-graph-2d`; El Atlas como excepción al invariante 6.
  - D19 — eje tímbrico (C19) pendiente, no decidido; aritmética que mató la versión ingenua (iTunes ~250 h, 144 GB) y el diseño perezoso que la sustituiría, condicionado al spike v2.
  - D20 — segunda ronda de features, C17–C27, una línea cada una.
  - D21 — convenciones de código formalizadas también en `SPEC.md` §9.1.
  - D22 — resultado del spike v1: inconcluyente, con el porqué (sesgo de muestreo por tag), y las dos cifras (85 % global, 82 % circular) que no deben volver a citarse.
  - Se extendieron **«Preguntas abiertas»** (Q6: disponibilidad de `Redaction` como paquete; Q7: qué fracción de bandas underground carece de tags y de abstract, para el spike v2) y **«Riesgos vivos»** (R1 y R4 actualizados con el resultado de D22; R6 sobre `Redaction`; R7 sobre si el eje tímbrico compensa incluso en su versión perezosa).

## Qué se dejó fuera deliberadamente

- **No se creó `docs/spikes/`.** `CLAUDE.md` y `DECISIONS.md` lo referencian como destino de las mediciones, pero no existe en el repo y no es alcance de este agente crearlo (pertenece a quien ejecute el spike v2, no a documentación). Queda anotado como hueco, no resuelto.
- **No se añadió el campo `releases.format`** que necesitaría C13 (filtro «solo casete») aunque el brief lo mencionaba. El brief de esquema (§10) especificaba exactamente qué columnas añadir (`themes`, `xy`, `audio_features`, `credits.source/confidence`) y no incluía `releases.format`; en vez de inventarlo, `SPEC.md` §5.9 deja anotado que ese filtro concreto requiere ampliar el esquema más allá de lo que cubre esta revisión.
- **No se tocó `CLAUDE.md`, `docs/REVIEW.md`, `docs/outreach/**` ni ningún código.** Fuera de alcance por instrucción explícita.
- **`Redaction` se trata como no verificada** en todo momento, tanto en `DESIGN.md` como en la nueva Q6 de `DECISIONS.md`. No se afirmó ni se negó su existencia en npm/fontsource.
- **C19 no se marcó como decidido en ningún punto** — ni en `SPEC.md` (⏸️, sección propia con advertencia), ni en `DECISIONS.md` (D19 dice explícitamente «pendiente, no decidido»).

## Contradicción encontrada (no corregida en silencio)

**`SPEC.md` §5.4 (B22 «Constelación») y §5.10 (C18 «El Atlas») describen la misma vista con dos diseños distintos.** B22, ya existente, es «proyección 2D (UMAP, offline) del atlas; tu nube encima, las zonas negras vacías» sin más detalle de render. C18, nueva, especifica un render concreto y distinto: universo lejano como raster de densidad pregenerado, solo estrellas cercanas al vector de gusto dibujadas en vivo y clicables. Son la misma feature en dos estados de resolución de diseño, no dos features independientes — B23 (gaps) depende explícitamente de B22 en el texto original.

Se dejó constancia cruzada («ver C18, que sustituye la vista» en B22; C18 mantiene su propio ID) en vez de fusionar o borrar B22, porque fusionar IDs ya citados en otros documentos (el propio `SPEC.md`, potencialmente `REVIEW.md` que este agente no puede tocar ni leer con certeza de estar actualizado) es una decisión de producto, no editorial. Se recomienda que quien cierre movimiento VI decida si B22/B23 se retiran en favor de C18 o si C18 se renombra como implementación de B22.

## Ficheros escritos

- `/home/drheavymetal/projects/Grimoire/docs/SPEC.md` (reescrito)
- `/home/drheavymetal/projects/Grimoire/docs/DESIGN.md` (nuevo)
- `/home/drheavymetal/projects/Grimoire/docs/DECISIONS.md` (append: D18–D22, más extensión de Preguntas abiertas y Riesgos vivos)
- `/home/drheavymetal/projects/Grimoire/docs/progress/docs.md` (este fichero)
