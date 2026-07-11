# Autonomous build — plan y estado

Pedro pidió (2026-07-11 ~02:30 CEST, se fue a dormir): acabar **todo el producto, todas las features, de forma autónoma**, guardando progreso y con **commit sin firmar + push** incremental; al final, **rondas de agentes que verifiquen con Playwright** ellos mismos; nada sin implementar; dar la **hora de finalización** al terminar. Sin usar Fable 5.

Este fichero es el **estado durable** entre rondas. Se actualiza y commitea en cada hito.

## Reglas del modo autónomo
- Invariantes de `CLAUDE.md` intactos. `scripts/audit.sh --strict` verde antes de cada commit.
- **Migraciones = un solo dueño por ola.** Nunca dos agentes creando migraciones a la vez.
- **No inventar datos** (REVIEW.md). Donde la fuente no exista en este entorno (dumps de Discogs de 30 GB, etc.), se implementa el **vertical completo** (esquema + endpoint + UI + estado vacío diseñado) y se declara el hueco; jamás un stub con forma de dato real.
- Corpus pequeño (~2478 artistas) → las **APIs gratis sí escalan** aquí: Wikidata SPARQL (P737 influencia, P570 fallecimiento), MusicBrainz a 1 req/s, Cover Art Archive, iTunes/Deezer, UMAP local sobre 309 embeddings.
- Commit sin firma GPG (convención del repo) + push a `origin/main` tras cada ola.
- Q1/Q2 (decisiones visuales) las autoriza Pedro explícitamente → se implementan por la recomendación del estudio (Q1 Opción 1 con mapa corregido D38; Q2 híbrido).

## Olas

- [ ] **Ola D — Data backbone** (dueño único de migraciones): crear `credits`, `works`, `artists.death_*`; confirmar `artists.xy`. Poblar gratis: Wikidata P737→`artist_edges(influenced_by)`, P570/P20→fallecimientos, MB label-rels→`labels`/`releases.label_id`, MB artist-rels de release→`credits` (best-effort), UMAP→`artists.xy`.
- [ ] **Ola III — Gantt (héroe)**: B7 timeline, B8 miembros-al-pasar-por-disco, B10 página de miembro. [relanzar] + C12 In Memoriam, C15 instrumentos raros, B9 créditos por disco.
- [ ] **Ola IV — Linaje**: B16 Bloodline (d3-force+SVG, D18), B19 Six Degrees (BFS), B11 diáspora, B3 búsqueda por miembro, C5 eslabón perdido, C8 Rabbit Hole, C9 splits, C10 versiones, C17 tu grimorio grafo.
- [ ] **Ola V — Escenas**: B20/B21 escenas+sellos, B12, B2 búsqueda semántica, B24 comparar, C6 muro de portadas, C7 duración, C11 escenas, C14 tarjeta, C21 minería de títulos, C22 regalo, C23 grimorios cruzados, C24 un álbum, C25 hiperprolífico, C26 deriva cromática.
- [ ] **Ola VI — Espejo**: B17 Weekly Rite (WebPush), B18 Dark Twin, C18 Atlas (canvas/WebGL), B23 gaps, B25 anti-rec, C16 trayectoria, C20 el espejo.
- [ ] **Ola VII — Clásica**: modelo `works`, ficha de compositor, linaje maestro-discípulo. (Datos de clásica ausentes en el corpus → shell + siembra mínima si MB lo permite; hueco declarado si no.)
- [ ] **Ola Q — Firma visual**: cablear `redactionCutForRank` corregido en nombres/reveal (Q1 Opción 1); modo claro híbrido (Q2).
- [ ] **Ola V&V — Verificación**: rondas de agentes con **Playwright** E2E sobre todas las features; arreglar fallos; audit --strict; commit+push.

## Bitácora
- 02:32 CEST — push de mov. I+II (rank chain incluido). Backend del Gantt (edges con contraparte) commiteado. Arranca Ola D + Ola III en paralelo (frentes disjuntos: shared/console vs front).
