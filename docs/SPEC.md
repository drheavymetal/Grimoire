# Grimoire — Especificación funcional

> Estado: **borrador de definición**. Nada implementado. Este documento fija el *qué* y el *por qué*; el *cómo* detallado vive en `docs/ARCHITECTURE.md` (pendiente).
>
> Última revisión: 2026-07-10

---

## 1. El problema

Siempre acabamos escuchando lo mismo. La causa no es la falta de recomendaciones —los servicios de streaming las dan a puñados— sino el **prejuicio de etiqueta**: lees «brutal death técnico eslovaco» y saltas antes del segundo uno. El filtro se aplica antes de escuchar.

A eso se suma el sesgo de popularidad de los recomendadores por filtrado colaborativo: convergen hacia lo que ya escucha mucha gente, que es exactamente lo que ya conoces.

## 2. La respuesta

Grimoire es una **base de datos musical + motor de descubrimiento** para metal, rock y (en un movimiento posterior) música clásica.

Tres pilares:

1. **The Rite** — la banda se sirve a ciegas. Sin nombre, sin género, sin país, sin portada. 45 segundos. Solo si te gusta, se revela.
2. **Ranks** — la rareza es inversa a la popularidad. Descubrir Metallica no vale nada; descubrir un sludge finlandés de 300 oyentes sí.
3. **Bloodline** — el linaje real: quién tocó con quién, qué banda salió de qué ruptura, quién enseñó a quién.

Alrededor, una ficha de artista profunda: formación a lo largo del tiempo, créditos por disco, discografía, sellos, escenas.

## 3. Lo que Grimoire NO es

- **No es un reproductor.** Solo se dispone legal y gratuitamente de previews de 30–45 s. Tras el reveal se enlaza a los servicios de streaming. Intentar ser reproductor implica licencias y hunde el proyecto.
- **No tiene coste operativo.** Ninguna fuente de datos, modelo ni servicio de pago. Ver §7.
- **No es multi-tenant** ni forma parte de qlaios. Producto independiente, repo propio.

---

## 4. Modelo de dominio

```
artists      kind: person | group | orchestra | choir
artist_edges kind: member_of | side_project | collaboration | teacher | influenced_by
releases     type: album | ep | demo | split | live | compilation
credits      artista × release/recording × rol × instrumento
works        (reservada — música clásica, movimiento 3)
```

Decisiones tomadas:

- **La demo es un release de primera clase.** En metal la demo importa tanto como el álbum. No se esconde bajo un desplegable.
- **`artists.kind` y el enum de `artist_edges` nacen abiertos** para que la clásica quepa sin migración destructiva. La tabla `works` se reserva pero no se escribe en v1.
- **Miembro oficial ≠ invitado.** Los créditos distinguen ambos. Confundirlos arruina el Gantt.

---

## 5. Catálogo de funcionalidades

Leyenda de datos: ✅ sólido · ⚠️ irregular según banda · 🔨 se calcula

### 5.1 Búsqueda y ficha

| ID | Feature | Datos |
|----|---------|-------|
| B1 | Búsqueda de artista: trigram (`pg_trgm`), alias, tolerante a erratas | ✅ |
| B2 | Búsqueda semántica: «algo como Neurosis pero más lento» → sobre el embedding | 🔨 |
| B3 | Búsqueda por miembro: «bandas donde tocó Steve Von Till» | ✅ |
| B4 | Ficha de artista: origen, ciudad, años activos, sellos, tags, rank, bio | ⚠️ bio |
| B5 | Discografía agrupada por tipo (álbum / EP / demo / split / live) | ✅ |
| B6 | Portadas (Cover Art Archive por MBID) | ✅ |

### 5.2 Gente y tiempo — el núcleo

| ID | Feature | Datos |
|----|---------|-------|
| **B7** | **Lineup Timeline.** Eje X = años. Una fila por miembro. Barra = periodo de pertenencia, color = instrumento. Marcas verticales = discos. | ✅ |
| **B8** | **Al pasar por un disco se iluminan los miembros que estaban dentro.** Intersección de intervalos. | 🔨 |
| B9 | Créditos por disco: quién tocó qué, separando miembro oficial de invitado/sesión, más producción/mezcla/máster | ⚠️ |
| B10 | Ficha de miembro: todas sus bandas, solapes temporales, instrumentos | ✅ |
| B11 | Diáspora: la banda se rompe en 1994 — a dónde fue cada uno | 🔨 |
| B12 | «El disco donde cambió todo»: el release con mayor rotación de formación | 🔨 |

B7 y B8 juntos son el motivo por el que alguien enseña Grimoire a un amigo. Nadie los tiene bien resueltos visualmente.

**Riesgo estructural**: los créditos son excelentes para Iron Maiden y pésimos para el sludge finlandés de 300 oyentes. El motor de descubrimiento lleva justo a donde la ficha está más vacía. La ficha **debe degradar con dignidad** — estados vacíos diseñados, no huecos rotos.

### 5.3 Descubrimiento

| ID | Feature | Datos |
|----|---------|-------|
| B13 | **The Rite**: cata a ciegas, 45 s, `Summon` / `Banish` / `Again` | ✅ |
| B14 | Slider **Comfort ↔ Abyss**: radio del anillo de búsqueda | 🔨 |
| B15 | Ranks + Depth Score | ✅ |
| B16 | Bloodline: grafo de miembros + influencia (Wikidata P737) | ✅ |
| B17 | Weekly Rite: 7 a ciegas cada lunes, WebPush | 🔨 |
| B18 | Dark Twin: el usuario de gusto más cercano con colección más disjunta | 🔨 |

**Anti-filtración**: el preview se sirve por proxy desde la API (`GET /api/rite/{token}/audio`), nunca la URL de iTunes al cliente. Sin esto, devtools revienta la mecánica en diez segundos.

### 5.4 Lo que sale casi gratis y da el efecto WOW

| ID | Feature | Por qué |
|----|---------|---------|
| B19 | **Six Degrees of Metal**: camino más corto entre dos bandas por miembros compartidos | Un BFS sobre `artist_edges`. Un endpoint. Es lo que la gente comparte por captura |
| B20 | **Escenas**: cluster por ciudad + año + tag. Gotemburgo 93, Bergen 91, Tampa 89 | El metal se organiza por escenas, no por artistas |
| B21 | **Sellos como puerta de entrada**: Peaceville, Earache, Southern Lord | Así descubre metal la gente de verdad |
| B22 | **Constelación**: proyección 2D (UMAP, offline) del atlas; tu nube encima, las zonas negras vacías | Tu ignorancia, dibujada |
| B23 | Gaps: décadas, países, subgéneros sin tocar | Deriva de B22 |
| B24 | Comparar dos bandas: solape de tags, distancia vectorial, miembros compartidos | Barato |
| B25 | Anti-recomendación: «esta banda te va a repeler, y esto es por qué» | Usa el vector de repulsión |

### 5.5 Escucha (B26)

Grimoire no reproduce. Tras el reveal ofrece enlaces:

| Servicio | Precisión | Cómo |
|----------|-----------|------|
| Apple Music | **exacto** | `artistLinkUrl` de iTunes Search API |
| Deezer | **exacto** | campo `link` de su API pública |
| Spotify | búsqueda | `open.spotify.com/search/<nombre>` |
| YouTube Music | búsqueda | `music.youtube.com/search?q=` |
| YouTube | búsqueda | — |
| Tidal | búsqueda | `tidal.com/search?q=` |
| Bandcamp | búsqueda | — |

Todo resuelto en ETL y guardado en `artists.links jsonb`. Cero llamadas en caliente. Spotify exacto exigiría su API (gratis, pero con key y rotación de tokens): no compensa la deuda.

---

## 6. El motor de descubrimiento

Un recomendador normal hace `ORDER BY emb <=> taste LIMIT 10` y devuelve más de lo mismo. Ese es el bug de fondo.

Grimoire busca en **anillo**, no en bola:

```sql
SELECT a.id, a.emb <=> :taste AS dist
FROM artists a
WHERE a.emb <=> :taste BETWEEN :r_min AND :r_max    -- anillo, no bola
  AND a.emb <=> :repulsion > :r_safe                -- lejos de lo desterrado
  AND NOT EXISTS (SELECT 1 FROM rites r WHERE r.user_id = :u AND r.artist_id = a.id)
ORDER BY (a.emb <=> :taste) * :w_dist
       - ln(1e6 / GREATEST(a.listeners, 1)) * :w_rare
       - tag_novelty(a.tags, :u) * :w_novel
LIMIT 7;
```

- `r_min` / `r_max` los mueve el slider **Comfort ↔ Abyss**.
- `user_taste.emb` — media con decay de lo invocado.
- `user_taste.repulsion` — media de lo desterrado. **Resta activamente.** Un recomendador que aprende de los «no» es raro, y se nota.

**Embeddings**: texto (tags de Last.fm + abstract de Wikidata + miembros + sello) → `nomic-embed-text` sobre el Ollama autohospedado del equipo (768 dims). Índice HNSW de pgvector.

**Ranks**:

| listeners (Last.fm) | rank |
|---|---|
| > 500 000 | Known |
| 50 000 – 500 000 | Obscure |
| 5 000 – 50 000 | Hidden |
| < 5 000 | Forgotten |
| < 500 | Nameless |

El **Depth Score** no premia cuánto escuchas, sino cuán lejos has llegado.

---

## 7. Datos: gratis, y sin mirror en producción

Los dumps de MusicBrainz y Discogs son gratuitos. Lo que cuestan es **disco**, no dinero. Y las APIs no son alternativa: MusicBrainz limita a 1 req/s, y el Gantt exige `begin_date`/`end_date` de cada relación miembro-banda — cuatro o cinco llamadas encadenadas por banda. Para ~300 000 bandas no sale.

> **Decisión: el mirror de MusicBrainz es un artefacto de build, no un servicio de producción.**

Se importa MB y se procesan los dumps de Discogs en una máquina de desarrollo, se corre el ETL, y se despliega únicamente el Postgres destilado. Producción nunca ve MusicBrainz. Refresco trimestral repitiendo el proceso en local.

| | Disco | Dónde |
|---|---|---|
| MB core dump importado | ~30 GB | máquina de desarrollo, transitorio |
| Discogs `releases.xml.gz` | ~10 GB (se lee en streaming, no se almacena) | máquina de desarrollo, transitorio |
| **Postgres de Grimoire** | **~8–10 GB** | producción |

### Fuentes

| Fuente | Aporta | Coste |
|---|---|---|
| MusicBrainz (dump Postgres) | artistas, miembros **con fechas**, releases, recordings, créditos, sellos, países | gratis |
| Discogs (dumps XML mensuales) | créditos por release, mejor cobertura en metal que MB | gratis |
| Cover Art Archive | portadas por MBID | gratis |
| Wikidata (SPARQL) | `P737 influenced by`, `P18 image`, abstracts | gratis |
| Last.fm API | tags, `stats.listeners` → rank | gratis, key inmediata |
| iTunes Search API | preview 30–45 s, `artistLinkUrl` | gratis, **sin key** |
| Deezer API | preview de respaldo, `link` exacto | gratis, sin auth |

### Fuentes descartadas

- **Spotify** — eliminó `preview_url` y `audio-features` para aplicaciones nuevas en noviembre de 2024. No se puede depender de ella.
- **Metal Archives** — es *la* referencia del metal (formación por disco, temática lírica, estado de la banda), pero no tiene API oficial ni dumps, y scrapearla va contra sus términos. **Fuera del alcance.** No se construye nada que dependa de ella.

### Coste operativo

Cero euros. Embeddings en Ollama autohospedado. Imágenes vía Cover Art Archive con proxy y caché en disco. Único fleco: el email transaccional (verificación de cuenta) — o tier gratuito, o v1 no manda correos.

---

## 8. Alcance por movimientos

Metal y rock comparten forma (banda con miembros y fechas): entran juntos, sin trabajo extra.

**La música clásica no es un género más, es un segundo modelo de datos.** No hay formación: hay obra (compositor) e interpretación (director, orquesta, solista). El Gantt de miembros no significa nada para una orquesta, y el rank por `listeners` miente porque los tags de clásica son ruido. A cambio, dos cosas salen *mejor* que en metal: MusicBrainz documenta la relación `teacher`/`student` entre personas, y `P737` de Wikidata está mucho mejor poblado para compositores que para bandas de sludge.

Conclusión: **The Rite y Bloodline funcionan igual o mejor en clásica; la ficha de artista no funciona en absoluto.** Entra como movimiento propio, con su ficha, cuando metal y rock estén vivos.

| Movimiento | Contenido |
|---|---|
| **I — Cimientos** | Pipeline de dumps + ETL. Esquema. B1, B4, B5, B6. Front i18n (es/en). |
| **II — El Rito** | B13, B14, B15, B26. Proxy de audio. Vector de gusto y de repulsión. |
| **III — Sangre y tiempo** | B7, B8, B9, B10. El Gantt. |
| **IV — Linaje** | B16, B19, B11, B3. Grafo, Six Degrees, diáspora. |
| **V — Escenas** | B20, B21, B12, B2, B24. |
| **VI — Espejo** | B17, B18, B22, B23, B25. Constelación, Dark Twin, gaps. |
| **VII — Clásica** | Modelo `works`. Ficha de compositor. Linaje maestro-discípulo. |

Cada movimiento se despliega solo y aporta algo enseñable.

---

## 9. Stack

Canónico del equipo (ver el wiki: `cromowin`, `goal-app`, `qlaios`).

| Capa | Tecnología |
|---|---|
| Backend | .NET 10 · ASP.NET Core Web API (controllers) |
| ORM / BD | EF Core 10 + Npgsql · PostgreSQL 16 + **pgvector** + `pg_trgm` |
| Auth | ASP.NET Identity + JWT Bearer (access 15 min / refresh 16 d) |
| Front | Vite + React + TS + TanStack Router/Query + Tailwind v4 + shadcn/ui |
| i18n | i18next (es/en) desde el primer commit |
| Embeddings | Ollama autohospedado · `nomic-embed-text` (768) |
| Background | `IHostedService` en `src/console/server` |
| Logs | Serilog |
| HTTP saliente | `IHttpClientFactory` + Polly (los rate limits de MB exigen circuit breaker) |
| Tests | xUnit |
| Deploy | Docker Compose + Traefik → Cloudmax |

### Monorepo

```
Grimoire/
├── docs/
├── src/
│   ├── shared/GrimoireLibrary/      # class library: Models, Services, Data
│   ├── web/
│   │   ├── server/                  # ASP.NET Core 10 Web API
│   │   ├── GrimoireTest/            # xUnit
│   │   ├── Grimoire.slnx
│   │   └── global.json
│   ├── console/server/              # IHostedService: ETL, embeddings, refresco
│   └── front/                       # Vite + React + TS
└── build/{production,demo}/docker-compose.yml
```

### Front preparado para React Native

No hay app móvil en v1, pero el front se escribe para que portarlo a Expo cueste una semana y no tres meses.

```
src/front/src/
├── core/          ← 100 % portable. Cero `window`, cero `document`, cero DOM.
│   ├── api/       # cliente tipado
│   ├── hooks/     # useRite, useTaste, useBloodline
│   └── domain/    # tipos, rank(), depthScore()
├── platform/      ← adaptadores, un fichero por primitiva
│   ├── audio.web.ts       → HTMLAudioElement   │ audio.native.ts → expo-av
│   ├── storage.web.ts     → localStorage       │ → AsyncStorage
│   └── navigation.web.ts  → TanStack Router    │ → Expo Router
└── ui/            ← solo web. Tailwind v4 + shadcn.
```

Tres reglas, desde el primer commit:

1. **`core/` no importa de `ui/` ni de `platform/`.** Recibe los adaptadores por contexto. Un test de `useRite` corre sin navegador.
2. **Ni el Gantt (B7) ni Bloodline (B16) usan librerías acopladas al DOM.** El layout se calcula con una función pura (`elkjs` o `d3-force` headless, ambos sin DOM) y se pinta con primitivas SVG. En React Native, `react-native-svg` acepta las mismas primitivas: cambia el import, no el componente.
3. **Nada de animación solo-CSS.** `framer-motion` (tiene `framer-motion/native`) o transiciones dirigidas por estado.

---

## 10. Esquema inicial

```sql
CREATE EXTENSION vector;
CREATE EXTENSION pg_trgm;

artists(id, mbid uuid, name, sort_name, kind,
        country, city, formed_year, dissolved_year,
        listeners int, tags text[], abstract text,
        emb vector(768), rank smallint,
        links jsonb, image_url text)

artist_edges(from_id, to_id, kind,
             begin_date date, end_date date,
             instruments text[])          -- 'member_of' lleva fechas e instrumentos

releases(id, mbid uuid, artist_id, title, type, release_date, label_id, cover_url)

credits(artist_id, release_id, recording_id,
        role,                             -- performer | producer | engineer | mix | master
        instrument text, is_guest bool)

labels(id, mbid uuid, name, country)

users(...)                                -- ASP.NET Identity

user_taste(user_id, emb vector(768), repulsion vector(768), depth_score int, updated_at)

rites(id, user_id, artist_id, state, risk real, served_at, resolved_at)
      -- state: served | summoned | banished | again

works(...)                                -- reservada, movimiento VII

CREATE INDEX ON artists USING hnsw (emb vector_cosine_ops);
CREATE INDEX ON artists USING gin  (name gin_trgm_ops);
CREATE INDEX ON rites (user_id, artist_id);
```

El grimorio del usuario **no es una tabla**: es `rites WHERE state = 'summoned'`. Menos estado que sincronizar.
