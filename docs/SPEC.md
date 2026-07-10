# Grimoire — Especificación funcional

> Estado: **borrador de definición**. Nada implementado. Este documento fija el *qué* y el *por qué*; el *cómo* detallado vive en `docs/ARCHITECTURE.md` (pendiente).
>
> Última revisión: 2026-07-10

---

## 1. El problema

Siempre acabamos escuchando lo mismo. La causa no es la falta de recomendaciones —los servicios de streaming las dan a puñados— sino el **prejuicio de etiqueta**: lees «brutal death técnico eslovaco» y saltas antes del segundo uno. El filtro se aplica antes de escuchar.

A eso se suma el sesgo de popularidad de los recomendadores por filtrado colaborativo: convergen hacia lo que ya escucha mucha gente, que es exactamente lo que ya se conoce.

## 2. La respuesta

Grimoire es una **base de datos musical + motor de descubrimiento** para **metal, rock y folk** — y, en un movimiento posterior, música clásica.

Los tres primeros son ciudadanos de primera clase desde el movimiento I. El folk aquí es el que orbita el metal: viking folk, nordic y ritual folk, neofolk, celtic folk, pagan folk, folk metal. Wardruna, Heilung, Skáld, Gealdýr, y un larguísimo etcétera. Comparten forma con una banda de metal —grupo, miembros, discos, sellos— así que no cuestan nada en el modelo de dominio. Lo que sí cambian es **cómo se define el corpus**: no por una lista de géneros, sino por anclas más expansión por el grafo de linaje. Ver D23.

Tres pilares:

1. **The Rite** — la banda se sirve a ciegas. Sin nombre, sin género, sin país, sin portada. 45 segundos. Solo si gusta, se revela.
2. **Ranks** — la rareza es inversa a la popularidad. Descubrir Metallica no vale nada; descubrir un sludge finlandés de 300 oyentes sí.
3. **Bloodline** — el linaje real: quién tocó con quién, qué banda salió de qué ruptura, quién enseñó a quién.

Alrededor, una ficha de artista profunda: formación a lo largo del tiempo, créditos por disco, discografía, sellos, escenas — y un segundo cuerpo de features (§5.6 en adelante) que convierte esos tres pilares en un producto con memoria, aprendizaje activo y una cartografía del gusto propio.

## 3. Lo que Grimoire NO es

- **No es un reproductor.** Solo se dispone legal y gratuitamente de previews de 30–45 s. Tras el reveal se enlaza a los servicios de streaming. Intentar ser reproductor implica licencias y hunde el proyecto.
- **No tiene coste operativo.** Ninguna fuente de datos, modelo ni servicio de pago. Ver §7.
- **No es multi-tenant** ni forma parte de qlaios. Producto independiente, repo propio.
- **No es un mapa de bandas por país.** Eso ya lo hace Metal Map, y lo hace bien. C11 (escenas) es deliberadamente otra cosa — ver D17.

---

## 4. Modelo de dominio

```
artists      kind: person | group | orchestra | choir
artist_edges kind: member_of | side_project | collaboration | teacher | influenced_by
releases     type: album | ep | demo | split | live | compilation
credits      artista × release/recording × rol × instrumento
works        (reservada — música clásica, movimiento VII)
```

Decisiones tomadas:

- **La demo es un release de primera clase.** En metal la demo importa tanto como el álbum. No se esconde bajo un desplegable.
- **`artists.kind` y el enum de `artist_edges` nacen abiertos** para que la clásica quepa sin migración destructiva. La tabla `works` se reserva pero no se escribe en v1.
- **Miembro oficial ≠ invitado.** Los créditos distinguen ambos. Confundirlos arruina el Gantt.
- **Los créditos y la temática llevan procedencia.** `credits.source` (`discogs | musicbrainz | inferred`) y `credits.confidence`, más `artists.themes` con su propia columna `themes_source`. Un dato inferido (intersección de intervalos) se marca como tal en la UI — ver D9.

---

## 5. Catálogo de funcionalidades

Leyenda de datos: ✅ sólido · ⚠️ irregular según banda · 🔨 se calcula · ⏸️ pendiente de spike, no decidido

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
| B22 | **Constelación**: proyección 2D (UMAP, offline) del atlas; tu nube encima, las zonas negras vacías | Tu ignorancia, dibujada — ver C18, que sustituye la vista |
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

### 5.6 Arranque en frío

No hay Rite sin `user_taste.emb`, y un usuario nuevo no tiene vector. Dos vías, no excluyentes (D15):

| ID | Feature | Datos | Coste |
|----|---------|-------|-------|
| C1 | **Import Last.fm**: scrobbles → artistas más escuchados → media de sus embeddings → vector de gusto inicial | ✅ Last.fm API | gratis, una llamada por alta |
| — | Alternativa sin cuenta de Last.fm: elegir cinco bandas al registrarse | ✅ | trivial |

### 5.7 Duelo, memoria y explicabilidad

El aprendizaje activo alrededor de The Rite: cómo se refina el vector más rápido que con un simple like, y cómo se le explica al usuario por qué se le sirvió algo.

| ID | Feature | Datos | Coste |
|----|---------|-------|-------|
| C2 | **Duelo a ciegas**: dos bandas a ciegas, se elige una. Preferencia por pares (Bradley-Terry) enseña el vector mucho más que un like suelto | 🔨 sobre embeddings existentes | trivial, sin fuente nueva |
| C3 | **Segunda oportunidad**: lo desterrado vuelve a ciegas a los seis meses. Se juzgó a ciegas, así que no se sabe qué se rechazó | ✅ `rites` | trivial |
| C4 | **Explicabilidad**: tras el reveal, tags compartidos, miembros en común, distancia. Sin esto, un recomendador raro parece roto | ✅/🔨 combinación de fuentes ya cargadas | trivial |
| C13 | **Filtros duros**: década, país, rank, duración, formato («solo casete», de los formatos de Discogs) | ✅/⚠️ el filtro de formato requiere ampliar el esquema de releases más allá de `type` — no incluido en el §10 de v1, pendiente | trivial salvo el campo de formato |
| C27 | **Adivina la década**: 45 s a ciegas y se apuesta año, país y subgénero. The Rite con marcador. Entrena el oído, que es literalmente la misión de la app | ✅ reusa el pipeline de C2/B13 | trivial |

### 5.8 El grafo ampliado

Bloodline no se limita a miembros compartidos.

| ID | Feature | Datos | Coste |
|----|---------|-------|-------|
| C5 | **El eslabón perdido**: «me gustan Neurosis y Burzum, ¿qué hay *entre* medias?». Interpola `emb = (A+B)/2` y busca vecinos. Tres líneas de SQL. Nadie más responde hoy esta pregunta | 🔨 sobre embeddings existentes | trivial |
| C8 | **Rabbit Hole**: sesión guiada de diez invocaciones, cada una elegida por el linaje de la anterior | 🔨 Bloodline + embeddings | trivial |
| C9 | **Grafo de splits**: quién compartió split con quién. La red social real del underground — un release con varios créditos de artista | ✅ MusicBrainz/Discogs (créditos de release) | trivial, ya en el ETL |
| C10 | **Grafo de versiones**: quién versionó a quién | ✅ MusicBrainz `works` + relaciones de cover | requiere ETL nuevo sobre `works` |
| C17 | **Tu grimorio como grafo**: solo las bandas invocadas por el usuario y las aristas entre ellas. Decenas a pocos cientos de nodos. Clic en un nodo → ficha. El análogo directo del grafo de memoria de OdinEngine: el propio conocimiento, creciendo | ✅ `rites` + `artist_edges` del usuario | trivial |

### 5.9 Escenas, comunidad y curiosidades del catálogo

| ID | Feature | Datos | Coste |
|----|---------|-------|-------|
| C6 | **Muro de portadas**: Cover Art Archive, paleta dominante calculada offline. Variante de Rite: solo portada, sin nombre | ✅ Cover Art Archive | cálculo offline, trivial |
| C7 | **Duración como eje**: MusicBrainz guarda la duración de cada grabación. Funeral doom y grindcore en extremos opuestos; ningún tag de género captura eso | ✅ MusicBrainz | trivial |
| C11 | **Escenas, no un mapa**: cluster por ciudad + año + tag. Gotemburgo 93, Bergen 91, Tampa 89. Explícitamente NO un mapa de bandas por país — eso ya lo hace Metal Map (D17) | ✅/🔨 | trivial |
| C12 | **In Memoriam**: miembros fallecidos, cronología. Fechas de fallecimiento de Wikidata. Exige un tono cuidado | ⚠️ Wikidata | trivial, requiere trabajo editorial |
| C14 | **Tarjeta para compartir**: depth score, escenas, gaps | 🔨 datos ya derivados (B15, B23) | trivial |
| C15 | **Instrumentos raros**: violín, gaita, zanfona. MusicBrainz guarda el instrumento en cada crédito | ✅ MusicBrainz | trivial |
| C21 | **Minería de títulos de canción**: los títulos son hechos, disponibles libremente en MusicBrainz e iTunes, y no están protegidos como una letra. Aproxima temática lírica **sin Metal Archives**, vocabulario cerrado + contador. Más débil que el campo curado de MA, pero elimina la dependencia de Q4 (si Hellblazer responde) | ✅ MusicBrainz/iTunes (títulos) | trivial, offline |
| C22 | **Regala un descubrimiento**: no se manda un enlace de Spotify. Se manda la banda **boca abajo, firmada**. Quien la recibe la escucha a ciegas, sin saber si es un regalo o una trampa. Solo se revela si le gusta | ✅ reusa el pipeline de Rite | trivial |
| C23 | **Grimorios cruzados**: ver el grimorio de un amigo; la app dice qué tiene él que al usuario le falta. El Dark Twin, pero con alguien conocido | ✅ `rites` de ambos usuarios | trivial |
| C24 | **La banda de un solo álbum**: bandas con exactamente un largo y nada más | ✅ `releases` | trivial |
| C25 | **El hiperprolífico**: proyectos de una sola persona con más lanzamientos que años de existencia | ✅ `releases` + `artists.formed_year` | trivial |
| C26 | **Deriva cromática**: la paleta dominante de la discografía de una banda a lo largo del tiempo, como una tira visual | ✅ deriva de C6 | trivial |

### 5.10 Memoria del usuario y cartografía del gusto

| ID | Feature | Datos | Coste |
|----|---------|-------|-------|
| C16 | **Tu trayectoria**: cómo se ha movido el vector de gusto en el tiempo | ✅ histórico de `user_taste` | trivial, requiere versionar el vector |
| C18 | **El Atlas**: el universo entero. Posiciones precalculadas offline con UMAP sobre los embeddings, en `artists.xy`. Render: el universo lejano es un **raster de densidad pregenerado** (una nebulosa); solo las estrellas cerca del vector de gusto se dibujan en vivo y son clicables. Las regiones oscuras son los gaps (B23). El Dark Twin es otra estrella. El Rabbit Hole es un camino sobre el cielo | ✅/🔨 embeddings + UMAP offline | cálculo offline sobre todo el catálogo, una vez |
| C20 | **El espejo**: no necesita datos nuevos, solo el propio historial de ritos. «El 62 % de las bandas que rechazaste a ciegas pertenecen al género que dices que es tu favorito.» La app demuestra su propia tesis con el oído del usuario como testigo | ✅ `rites` | trivial |

### 5.11 El eje tímbrico — pendiente

| ID | Feature | Datos | Coste |
|----|---------|-------|-------|
| C19 | **El eje tímbrico** ⏸️ **pendiente del spike v2. No presentar como decidido.** Rasgos de audio (BPM, centroide espectral, rango dinámico, densidad de onsets, ratio armónico/percusivo, crest factor) calculados offline sobre los previews de 45 s con librosa/Essentia; el audio se descarta, solo quedan los seis números. Justificación: el embedding de texto (tags de Last.fm + abstract de Wikidata) es *ruido* precisamente para las bandas que no tienen ninguno de los dos — la cola oscura para la que existe la app. El audio es la única señal que no se degrada con la oscuridad. También produce la discrepancia «etiquetada como black metal, suena a shoegaze» y el gráfico de crest factor de la loudness war | ⏸️ Deezer (existencia de preview) + análisis offline del pool de la Rite | ver desglose de coste abajo |

**Desglose de coste** (lo que mató la versión ingenua): resolver la existencia de preview para todo el catálogo vía Deezer es ~8 h; vía iTunes serían ~250 h. Descargar 300k previews son ~144 GB. Por tanto, si se hace: resolver la *existencia* de preview para todo el catálogo vía Deezer (necesario de todos modos para B26/B13), analizar audio solo sobre el pool de la Rite (~20–30k bandas, estratificado por rank, ~15 GB, descartado tras la extracción) y enriquecer de forma perezosa cualquier banda buscada o invocada. **Si esta feature se construye depende de la respuesta del spike v2 a «qué fracción de bandas underground no tiene ni tags ni abstract de Wikidata»**. Ver D19.

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
- `user_taste.emb` — media con decay de lo invocado. Se inicializa en frío vía C1 (import Last.fm) o la selección manual de cinco bandas — ver §5.6 y D15.
- `user_taste.repulsion` — media de lo desterrado. **Resta activamente.** Un recomendador que aprende de los «no» es raro, y se nota.

**Embeddings**: texto (tags de Last.fm + abstract de Wikidata + miembros + sello) → `nomic-embed-text` sobre el Ollama autohospedado del equipo (768 dims). Índice HNSW de pgvector. Si C19 llega a implementarse, el vector de audio (seis rasgos, no 768 dims) se combina o se consulta como eje adicional, no sustituye al de texto — ver §5.11.

**Ranks**:

| listeners (Last.fm) | rank |
|---|---|
| > 500 000 | Known |
| 50 000 – 500 000 | Obscure |
| 5 000 – 50 000 | Hidden |
| < 5 000 | Forgotten |
| < 500 | Nameless |

El **Depth Score** no premia cuánto se escucha, sino cuán lejos se ha llegado.

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
| MusicBrainz (dump Postgres) | artistas, miembros **con fechas**, releases, recordings, créditos, sellos, países, `works` y relaciones de cover (C10) | gratis |
| Discogs (dumps XML mensuales) | créditos por release, formatos físicos, mejor cobertura en metal que MB | gratis |
| Cover Art Archive | portadas por MBID, base de C6/C26 | gratis |
| Wikidata (SPARQL) | `P737 influenced by`, `P18 image`, abstracts, fechas de fallecimiento (C12) | gratis |
| Last.fm API | tags, `stats.listeners` → rank, scrobbles del usuario (C1) | gratis, key inmediata |
| iTunes Search API | preview 30–45 s, `artistLinkUrl` | gratis, **sin key** |
| Deezer API | preview de respaldo, `link` exacto, resolución de existencia de preview catalogue-wide (base de C19 si se construye) | gratis, sin auth |

### Fuentes descartadas

- **Spotify** — eliminó `preview_url` y `audio-features` para aplicaciones nuevas en noviembre de 2024. No se puede depender de ella.
- **Metal Archives** — es *la* referencia del metal (formación por disco, temática lírica, estado de la banda), pero no tiene API oficial ni dumps, y scrapearla va contra sus términos. **Fuera del alcance.** No se construye nada que dependa de ella — C21 existe precisamente para aproximar temática lírica sin ella.

### Coste operativo

Cero euros. Embeddings en Ollama autohospedado. Imágenes vía Cover Art Archive con proxy y caché en disco. Único fleco: el email transaccional (verificación de cuenta) — o tier gratuito, o v1 no manda correos.

---

## 8. Alcance por movimientos

Metal y rock comparten forma (banda con miembros y fechas): entran juntos, sin trabajo extra.

**La música clásica no es un género más, es un segundo modelo de datos.** No hay formación: hay obra (compositor) e interpretación (director, orquesta, solista). El Gantt de miembros no significa nada para una orquesta, y el rank por `listeners` miente porque los tags de clásica son ruido. A cambio, dos cosas salen *mejor* que en metal: MusicBrainz documenta la relación `teacher`/`student` entre personas, y `P737` de Wikidata está mucho mejor poblado para compositores que para bandas de sludge.

Conclusión: **The Rite y Bloodline funcionan igual o mejor en clásica; la ficha de artista no funciona en absoluto.** Entra como movimiento propio, con su ficha, cuando metal y rock estén vivos.

| Movimiento | Contenido |
|---|---|
| **I — Cimientos** | Pipeline de dumps + ETL. Esquema. B1, B4, B5, B6. Front i18n (es/en). Shippable en solitario: no depende de ningún movimiento posterior. |
| **II — El Rito** | B13, B14, B15, B26. Proxy de audio. Vector de gusto y de repulsión. C1 (arranque en frío), C2 (duelo a ciegas), C3 (segunda oportunidad), C4 (explicabilidad), C13 (filtros duros), C27 (adivina la década). C19 (eje tímbrico) vive aquí en cuanto eje candidato, pero está gateado al spike v2 — no es requisito de movimiento. |
| **III — Sangre y tiempo** | B7, B8, B9, B10. El Gantt. C12 (In Memoriam), C15 (instrumentos raros). |
| **IV — Linaje** | B16, B19, B11, B3. Grafo, Six Degrees, diáspora. C5 (el eslabón perdido), C8 (Rabbit Hole), C9 (grafo de splits), C10 (grafo de versiones), C17 (tu grimorio como grafo). |
| **V — Escenas** | B20, B21, B12, B2, B24. C6 (muro de portadas), C7 (duración como eje), C11 (escenas, no un mapa), C14 (tarjeta para compartir), C21 (minería de títulos), C22 (regala un descubrimiento), C23 (grimorios cruzados), C24 (banda de un solo álbum), C25 (el hiperprolífico), C26 (deriva cromática). |
| **VI — Espejo** | B17, B18, B22, B23, B25. Constelación, Dark Twin, gaps. C16 (tu trayectoria), C18 (El Atlas, sustituye la vista de constelación de B22), C20 (el espejo). |
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

### 9.1 Convenciones de código

1. **Todo el código va en inglés.** Identificadores, comentarios, mensajes de log, mensajes de commit. Sin excepciones ni mezclas.
2. **Llaves siempre, aunque el cuerpo sea de una sola línea.**

Se aplican mecánicamente, no de memoria: `.editorconfig` (`csharp_prefer_braces = true:warning`) en C#, ESLint `curly: ["error", "all"]` en TypeScript. La documentación de `docs/` va en español.

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
2. **Ni el Gantt (B7) ni ningún grafo (B16, C9, C10, C17) usan librerías acopladas al DOM.** Ver más abajo, «Los tres grafos: tres técnicas».
3. **Nada de animación solo-CSS.** `framer-motion` (tiene `framer-motion/native`) o transiciones dirigidas por estado.

#### Los tres grafos: tres técnicas

Bloodline no es un solo componente — son tres vistas de grafo con escalas muy distintas, y cada una exige su propia técnica de render:

| Vista | Tamaño | Técnica |
|---|---|---|
| Bloodline (ego-grafo) | ~100–400 nodos | `d3-force` headless + primitivas SVG |
| Tu grimorio (C17) | decenas a pocos cientos de nodos | `d3-force` headless + primitivas SVG |
| El Atlas (C18) | ~300k nodos | canvas/WebGL |

Bloodline y Tu grimorio siguen el patrón ya existente del equipo en `GraphCanvas.tsx` (base-wiki): auto-fit por bounding box con las posiciones transformadas en JS —nunca escalando un `<g>`—, glifos contra-escalados por `1/k` al hacer zoom, y etiquetas solo en foco, en coincidencia de búsqueda o a `k ≥ 1.6`. Con `react-native-svg` en Expo se cambia el import, no el componente.

**No se usa `react-force-graph-2d`** (lo que usa OdinEngine): está acoplado a canvas, así que rompe el invariante 6 (`core/` sin DOM), y ata el bucle de repintado al bucle de la simulación — la animación se congela en cuanto `d3` se enfría, un bug ya documentado por el equipo.

**El Atlas (C18) es la excepción explícita.** 300k nodos exigen canvas/WebGL, vive solo en `ui/` y rompe el invariante 6 a conciencia — no hay port a React Native razonable para esa vista sin reescribirla.

---

## 10. Esquema inicial

```sql
CREATE EXTENSION vector;
CREATE EXTENSION pg_trgm;

artists(id, mbid uuid, name, sort_name, kind,
        country, city, formed_year, dissolved_year,
        listeners int, tags text[], abstract text,
        themes text[], themes_source text,        -- temática lírica (C21, o MA si D8/Q4 se resuelve)
        emb vector(768), xy point,                 -- xy: proyección UMAP offline, para C18
        rank smallint,
        links jsonb, image_url text)

artist_edges(from_id, to_id, kind,
             begin_date date, end_date date,
             instruments text[])          -- 'member_of' lleva fechas e instrumentos

releases(id, mbid uuid, artist_id, title, type, release_date, label_id, cover_url)

credits(artist_id, release_id, recording_id,
        role,                             -- performer | producer | engineer | mix | master
        instrument text, is_guest bool,
        source text,                      -- discogs | musicbrainz | inferred
        confidence real)

labels(id, mbid uuid, name, country)

users(...)                                -- ASP.NET Identity

user_taste(user_id, emb vector(768), repulsion vector(768), depth_score int, updated_at)

rites(id, user_id, artist_id, state, risk real, served_at, resolved_at)
      -- state: served | summoned | banished | again

-- gateada a que C19 supere el spike v2 (ver D19) — no se crea si el eje tímbrico no se construye
audio_features(artist_id, recording_id,
               bpm real, spectral_centroid real, dynamic_range real,
               onset_density real, harmonic_percussive_ratio real, crest_factor real,
               computed_at timestamptz)

works(...)                                -- reservada, movimiento VII

CREATE INDEX ON artists USING hnsw (emb vector_cosine_ops);
CREATE INDEX ON artists USING gin  (name gin_trgm_ops);
CREATE INDEX ON rites (user_id, artist_id);
```

El grimorio del usuario **no es una tabla**: es `rites WHERE state = 'summoned'`. Menos estado que sincronizar.
