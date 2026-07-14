# Decisiones

Log append-only. Cada entrada: qué se decidió, **por qué**, y qué se descartó. Una decisión que cambia no se edita: se añade una entrada nueva que la supersede.

Formato: `## D<n> — <título>` · fecha · estado (`vigente` | `superseded by D<n>`).

---

## D1 — Producto independiente, no app de qlaios
`2026-07-10` · vigente

Repo propio (`GO2chainSL`… no: `drheavymetal/Grimoire`), auth propia, dominio propio.

**Por qué**: el contrato de apps de qlaios impone multi-tenant, SSO del workspace y `X-Tenant-Id`. Grimoire es B2C para un grupo de amigos. Nada de eso encaja.

---

## D2 — Sin cromos ni coleccionismo
`2026-07-10` · vigente

La propuesta inicial reutilizaba el `SkiaCardRenderer` de GOAL para mintear cromos por banda descubierta. **Descartado por Pedro.** La referencia a CromoWin era por el *stack*, no por los cromos.

**Consecuencia**: fuera SkiaSharp, fuera DO Spaces para render. La rareza sobrevive como **rank** inscrito en el grimorio, no como objeto.

---

## D3 — Tres pilares
`2026-07-10` · vigente

1. **The Rite** — cata a ciegas. 45 s de audio, sin nombre, género, país ni portada. `Summon` / `Banish` / `Again`. Solo se revela si gusta.
2. **Ranks** — rareza inversa a la popularidad (`listeners` de Last.fm). Descubrir Metallica no vale nada.
3. **Bloodline** — linaje real: miembros compartidos (MusicBrainz) + influencia declarada (Wikidata `P737`).

**Por qué The Rite**: el problema de «siempre escuchamos lo mismo» no es escasez de recomendaciones, es **prejuicio de etiqueta**. El filtro se aplica antes de escuchar.

---

## D4 — El motor busca en anillo, no en bola
`2026-07-10` · vigente

Un recomendador normal hace `ORDER BY emb <=> taste LIMIT 10` y devuelve más de lo mismo. Grimoire filtra `WHERE emb <=> taste BETWEEN r_min AND r_max` — lo más lejano que aún cae en tu tolerancia. `r_min`/`r_max` los mueve el slider **Comfort ↔ Abyss**.

Además `user_taste.repulsion`, media de lo desterrado, **resta**. Un recomendador que aprende de los «no» es raro y se nota.

---

## D5 — El mirror de MusicBrainz es artefacto de build, no servicio de producción
`2026-07-10` · vigente

Se importa el dump de MB (~30 GB) y se procesan los dumps de Discogs (~10 GB, en streaming) **en una máquina de desarrollo**. El ETL destila el subgrafo rock/metal. Se despliega solo el Postgres de Grimoire (~8–10 GB). Refresco trimestral repitiendo el proceso en local.

**Por qué no la API de MB**: 1 req/s. El Gantt de formación exige `begin_date`/`end_date` de cada relación miembro-banda: 4-5 llamadas encadenadas por banda. Para ~300k bandas no sale.

**Por qué no un mirror permanente**: producción no lo necesita, y era la única objeción real de Pedro (disco en Cloudmax).

---

## D6 — Coste operativo cero
`2026-07-10` · vigente

Restricción dura de Pedro: la app es informativa y gratuita, **no se paga por nada**.

Consecuencias: embeddings con `nomic-embed-text` en el Ollama autohospedado (no OpenAI). Imágenes vía Cover Art Archive con proxy y caché en disco (no Spaces). Sin Spotify API.

**Fleco abierto**: email transaccional (verificación de cuenta). O tier gratuito, o v1 no manda correos. Ver Q5.

---

## D7 — No se scrapea Metal Archives
`2026-07-10` · vigente

Pedro pidió primero un scrape completo. Se descartó tras analizar **qué aporta MA de verdad**:

| Pilar | ¿MA aporta? |
|---|---|
| The Rite | nada — no tiene audio |
| Ranks | nada — el rank sale de Last.fm |
| Bloodline | nada nuevo — el grafo es MB + Wikidata |

MA aporta exactamente **un campo irremplazable** (temática lírica, que es un *filtro*, no una mecánica) y una mejora de cobertura de formaciones que **Discogs ya cubre**.

Y el remate: las bandas que *solo* están en MA (ensayos en cassette nunca prensados) tampoco están en Discogs porque no hay disco — y por lo mismo **no tienen preview en iTunes ni Deezer**. Nunca podrían sonar en The Rite.

Tres semanas de crawl y violar sus términos, por un facet.

**Además**: el 2026-07-10 Pedro envió un correo a `webmaster@metal-archives.com` afirmando por escrito que no les scrapeamos y que enlazamos de vuelta a sus fichas. Ya no es una recomendación técnica: es palabra dada. Ver `docs/outreach/metal-archives.md`.

---

## D8 — MA se pide, no se toma
`2026-07-10` · vigente

Investigación de los proyectos que MA enlaza en su página oficial *Tools & add-ons*:

- **Metal Archives Graphs** (neocities), changelog de mayo 2025: *«Retrieved a new and improved dataset from Hellblazer.»*
- La hoja de cálculo de todas las bandas que circula por su foro está publicada *«with kind permission of HellBlazer»*.
- Hellblazer, webmaster fundador, en su foro: la base entera no está disponible, **pero un subconjunto concreto se puede pedir por email** y probablemente lo exporten.
- Respuesta oficial del foro a quien quería hacer una app: *«habla con Morrigan o con Hellblazer.»*

**El subconjunto a pedir** (~180k filas, pocos MB):
```
band_id · name · country · year_formed · status · genre · lyrical_themes
```
Nada de reseñas, formaciones ni imágenes.

**Problema de emparejado**: MA no guarda MBIDs. El cruce `MA.band_id` ↔ `MB.mbid` va por nombre + país + año de formación; los ambiguos se marcan sin emparejar. Hay que preguntarles si tienen MBIDs. Ver R3.

---

## D9 — Toda fuente es un adaptador opcional
`2026-07-10` · vigente

```csharp
interface IEnrichmentSource {
    string Name { get; }
    Task<ArtistEnrichment?> FetchAsync(Artist artist, CancellationToken ct);
}
```

`MusicBrainzSource` · `DiscogsSource` · `WikipediaSource` · `MetalArchivesSource` (declarado, sin implementar).

**Dos razones, ambas suficientes**: la cobertura es irregular por naturaleza, y la respuesta de MA es binaria y no la controlamos. Ninguna vista puede romperse porque falte una fuente.

Los créditos y la temática llevan `source` + `confidence`. Una capa de inferencia (intersección de intervalos) se marca como inferida en la UI. Un badge «inferido» es honesto y además queda bien.

---

## D10 — Grimoire no reproduce música
`2026-07-10` · vigente

Solo hay previews de 30–45 s legal y gratuitamente. Tras el reveal, enlaces a siete servicios:

| Servicio | Precisión | Cómo |
|---|---|---|
| Apple Music | exacto | `artistLinkUrl` de iTunes Search API |
| Deezer | exacto | campo `link` de su API pública |
| Spotify · YouTube · YT Music · Tidal · Bandcamp | búsqueda | URL de búsqueda |

Spotify exacto exigiría su API (gratis, pero key y rotación de tokens): no compensa la deuda. **Spotify eliminó `preview_url` y `audio-features` para apps nuevas en noviembre de 2024** — no se puede depender de ella para audio.

Todo resuelto en ETL, guardado en `artists.links jsonb`. Cero llamadas en caliente.

---

## D11 — La música clásica es un movimiento aparte
`2026-07-10` · vigente

Rock entra con metal sin trabajo extra: misma forma (banda con miembros y fechas).

**Clásica rompe el modelo.** No hay formación: hay obra (compositor) e interpretación (director, orquesta, solista). El Gantt no significa nada para una orquesta. El rank por `listeners` miente porque los tags de clásica son ruido.

A cambio, dos cosas salen *mejor*: MB documenta la relación `teacher`/`student` entre personas, y `P737` de Wikidata está mucho mejor poblado para compositores.

**Conclusión**: The Rite y Bloodline funcionan igual o mejor en clásica; la ficha de artista no funciona en absoluto. Movimiento VII, con ficha propia. El esquema de v1 lleva ya `artists.kind` y un enum abierto en `artist_edges.kind`; la tabla `works` se reserva sin escribir.

---

## D12 — El front se escribe para portarlo a React Native
`2026-07-10` · vigente

Sin app móvil en v1. Pero:

```
src/front/src/
├── core/       ← 100% portable. Cero window/document/DOM.
├── platform/   ← adaptadores: audio, storage, navigation (.web.ts / .native.ts)
└── ui/         ← solo web. Tailwind v4 + shadcn.
```

Tres reglas desde el primer commit:
1. `core/` recibe adaptadores por contexto, no los importa. Un test de `useRite` corre sin navegador.
2. Ni el Gantt ni Bloodline usan librerías acopladas al DOM. Layout con función pura (`elkjs` / `d3-force` headless), pintado con primitivas SVG — `react-native-svg` acepta las mismas.
3. Nada de animación solo-CSS. `framer-motion` (tiene `/native`) o transiciones dirigidas por estado.

---

## D13 — Alcance completo, en siete movimientos
`2026-07-10` · vigente

Pedro: «no recortar nada». Aceptado. Estimación honesta: **5–6 meses** a dedicación parcial, no 8 semanas. Se trocea en movimientos que se despliegan solos.

| Mov. | Contenido |
|---|---|
| I — Cimientos | Dumps + ETL. Esquema. B1, B4, B5, B6. Front i18n |
| II — El Rito | B13, B14, B15, B26. Proxy de audio. Vector de gusto y repulsión |
| III — Sangre y tiempo | B7, B8, B9, B10. El Gantt |
| IV — Linaje | B16, B19, B11, B3 |
| V — Escenas | B20, B21, B12, B2, B24 |
| VI — Espejo | B17, B18, B22, B23, B25 |
| VII — Clásica | Modelo `works`. Ficha de compositor. Linaje maestro-discípulo |

---

## D14 — Dirección visual: generación de copia
`2026-07-10` · **pendiente de confirmar por Pedro** (Q1, Q2)

El metal no nace del vinilo, nace de la **cinta de demo y el flyer fotocopiado**. Cada generación pierde información — que es literalmente lo que hace la app: buscar en la periferia, donde la señal se degrada.

- **Modo claro = el flyer fotocopiado** (papel sucio, tóner, semitono). **Modo oscuro = la cinta** (el vacío, limpio, sin grano). Dos artefactos reales, no un tema y su concesión.
- **Firma**: la display es `Redaction` (SIL OFL), que tiene cortes de corrosión progresiva (`10`…`100`). **El corte lo decide el rank.** Metallica se lee nítida; una banda `Nameless` es casi ilegible. La tipografía *es* el dato.
- **El reveal de The Rite** no es un volteo de carta: el nombre aparece en `Redaction 100` y **se revela** hasta su corte en 600 ms, como una foto en el líquido.
- **Tipos**: display `Redaction` · cuerpo `Archivo` · utilidad `Courier Prime` (créditos, MBIDs, años — la vernácula de una J-card).
- **Color**: la app es monocroma. Único acento **azufre** `#D6C34A` (no verde ácido, que es el defecto de toda app de metal). `oxblood` solo para `Banish`.
- Semitono **solo en modo claro**. El vacío no tiene grano.

Restricción convertida en principio: **no hay derechos sobre fotos de bandas**. La ficha no tiene héroe fotográfico. El Gantt es el héroe.

---

## D15 — Arranque en frío
`2026-07-10` · vigente

Agujero detectado en la spec: The Rite necesita `user_taste.emb` y un usuario nuevo no tiene vector.

Dos soluciones, ambas: **importar Last.fm** (scrobbles → artistas más escuchados → media de embeddings; muchos metaleros llevan quince años scrobbleando) y, si no lo tiene, **elegir cinco bandas** al registrarse.

---

## D16 — Features añadidas tras la primera ronda
`2026-07-10` · vigente

C1 import Last.fm · C2 duelo a ciegas (preferencia por pares, Bradley-Terry: mucha más señal que un like suelto) · C3 segunda oportunidad (lo desterrado vuelve a los 6 meses; juzgaste a ciegas) · C4 explicabilidad («por qué te sirvo esto») · C5 **el eslabón perdido** (interpola `emb = (A+B)/2` y busca vecinos — tres líneas de SQL, y nadie más puede responder esa pregunta hoy) · C6 muro de portadas + paleta dominante del Cover Art Archive · C7 duración como eje (funeral doom vs grindcore) · C8 Rabbit Hole.

---

## D17 — No reconstruir lo que ya existe
`2026-07-10` · vigente

MA enlaza tres herramientas de terceros. **Ninguna es de descubrimiento. Ninguna suena.**

- **Metal Map** — bandas por país. No lo rehagas. Nuestro C11 debe ser **escenas** (ciudad + año + tag): Gotemburgo 93 no es Suecia.
- **MA Graphs** — estadísticas de lanzamientos y reseñas. Enlaza.
- **Empath** (de Glenn McDonald, el de *Every Noise at Once*) — similitud por **solape de reseñadores**, ajustada por popularidad. Es una señal **independiente** de nuestros embeddings de texto: dos bandas lejos en el vector y cerca en Empath son exactamente la zona de anillo que The Rite busca. Mezclarlas sería fuerte. **No pedir reseñas a MA ahora**: encarece el favor.

---

## D18 — Los grafos se renderizan con `d3-force` + SVG, no con `react-force-graph-2d`
`2026-07-10` · vigente

Bloodline (B16), Tu grimorio (C17), el grafo de splits (C9) y el de versiones (C10) son grafos de escala pequeña-media (decenas a ~400 nodos). Se renderizan con `d3-force` headless calculando el layout y primitivas SVG para el pintado, siguiendo el patrón ya existente del equipo en `GraphCanvas.tsx` (base-wiki): auto-fit por bounding box con las posiciones transformadas en JS (nunca escalando un `<g>`), glifos contra-escalados por `1/k` en zoom, etiquetas solo en foco/coincidencia de búsqueda/`k ≥ 1.6`.

**Por qué no `react-force-graph-2d`** (lo que usa OdinEngine): está acoplado a canvas, lo que rompe el invariante 6 (`core/` sin DOM — D12) al no admitir `react-native-svg` como salida alternativa. Y peor: ata el bucle de repintado al bucle de la simulación de fuerzas, así que la animación se congela en cuanto `d3` se enfría — un bug que el equipo ya tiene documentado de otros proyectos.

**Las tres vistas de grafo no comparten técnica.** Bloodline y Tu grimorio son SVG por lo anterior. **El Atlas (C18)**, con ~300k nodos, es la excepción explícita: exige canvas/WebGL, vive únicamente en `ui/` y rompe el invariante 6 a conciencia — no hay port razonable de esa vista a React Native sin reescribirla desde cero.

---

## D19 — El eje tímbrico (C19) queda pendiente, no decidido
`2026-07-10` · vigente

C19 propone rasgos de audio (BPM, centroide espectral, rango dinámico, densidad de onsets, ratio armónico/percusivo, crest factor) calculados offline con librosa/Essentia sobre los previews de 45 s, como eje de búsqueda independiente del embedding de texto. La motivación es real: el embedding de texto es ruido precisamente para las bandas sin tags ni abstract de Wikidata, que es la cola oscura que la app existe para servir.

**La versión ingenua (analizar el catálogo entero) es inviable y queda descartada.** La aritmética que la mata: resolver la existencia de preview vía iTunes para las ~300k bandas del catálogo son ~250 h de llamadas (1 req razonable/s, sin key pero con rate limiting de facto); vía Deezer bajan a ~8 h. Descargar 300k previews completos son ~144 GB. Pedro objetó el coste en cuanto se puso la cifra encima de la mesa — no el enfoque, la escala.

**Diseño perezoso que sustituiría a la versión ingenua, si C19 se construye**: resolver la *existencia* de preview para todo el catálogo vía Deezer (~8 h, y hace falta de todos modos para B13/B26), analizar audio solo sobre el pool de la Rite (~20–30k bandas, estratificado por rank, ~15 GB, el audio se descarta tras extraer los seis números) y enriquecer de forma perezosa cualquier banda que se busque o se invoque fuera de ese pool.

**Esto no ships todavía.** Depende enteramente de la respuesta del spike v2 a una pregunta muy concreta: qué fracción de bandas underground no tiene ni tags de Last.fm ni abstract de Wikidata. Si esa fracción es baja, el embedding de texto ya cubre el caso y C19 no se justifica. Ver D22 para por qué el spike v1 no puede responder esto todavía.

---

## D20 — Segunda ronda de features (C17–C27)
`2026-07-10` · vigente

C17 tu grimorio como grafo (el análogo directo del grafo de memoria de OdinEngine, pero con las propias bandas) · C18 El Atlas (proyección UMAP de todo el catálogo, nebulosa precalculada + estrellas cercanas al vector de gusto en vivo) · C19 eje tímbrico (**pendiente, ver D19**) · C20 el espejo (la app contrasta lo que el usuario dice que le gusta con lo que rechazó a ciegas) · C21 minería de títulos de canción (aproxima temática lírica sin depender de Metal Archives) · C22 regala un descubrimiento (se envía la banda boca abajo, no un enlace) · C23 grimorios cruzados (el Dark Twin, pero con un amigo real) · C24 la banda de un solo álbum · C25 el hiperprolífico (más lanzamientos que años de vida) · C26 deriva cromática (paleta dominante de la discografía en el tiempo) · C27 adivina la década (The Rite con marcador: año, país, subgénero).

Ver `docs/SPEC.md` §5.6–§5.11 para el desglose de datos y coste de cada una, y §8 para su reparto por movimiento.

---

## D21 — Convenciones de código formalizadas en la spec
`2026-07-10` · vigente

Lo que `CLAUDE.md` ya fijaba (todo el código en inglés — identificadores, comentarios, mensajes de log y de commit, sin excepciones ni mezclas; llaves siempre aunque el cuerpo sea de una sola línea) pasa a ser también contenido de primera clase en `docs/SPEC.md` §9.1, no solo una norma del fichero de arranque del agente.

**Por qué formalizarlo dos veces**: `SPEC.md` es el documento que describe el *qué* del producto para cualquiera que llegue sin haber leído `CLAUDE.md` primero. Enforcement mecánico, no de memoria: `.editorconfig` (`csharp_prefer_braces = true:warning`) en C#, ESLint `curly: ["error", "all"]` en TypeScript.

---

## D22 — Spike v1: resultado inconcluyente
`2026-07-10` · vigente

El primer spike de cobertura de previews (Q3) **no responde la pregunta que se le hizo**, y el motivo es un sesgo de muestreo de raíz, no ruido estadístico.

**Qué se hizo**: muestrear MusicBrainz por *tag* de género y medir cobertura de preview por bucket de número de lanzamientos.

**Por qué no sirve**: muestrear por tag selecciona bandas que **alguien se molestó en etiquetar** — eso ya es una señal de popularidad, no una muestra neutra. Resultado: 140 de 226 bandas muestreadas tenían 15 o más lanzamientos. El único bucket que se parece a una banda realmente oscura (1–2 lanzamientos) tuvo **n = 4** y **0 % de cobertura de preview**: no dice nada con esa n, aunque la dirección (cero) sí es inquietante.

**Dos cifras que salieron del spike v1 y no deben volver a citarse**:
- El **85 %** de cobertura global — es el promedio de una muestra sesgada hacia bandas populares, no representa a la cola oscura que le importa a la app.
- El **82 % de bandas con menos de 500 fans de Deezer que tienen preview** — es **circular**: hace falta *estar* en Deezer para tener un contador de fans, así que la muestra ya excluye a las bandas que ni siquiera están indexadas allí, que son probablemente las de peor cobertura de todas.

**Ruido adicional**: el emparejado por nombre coló falsos positivos — salió «Toto» (nada que ver con metal), y el «Death» con más fans en Deezer no es el de Chuck Schuldiner.

**Consecuencia**: R1 (¿se puede sonar lo raro?) sigue sin resolver. Hace falta un spike v2 que muestree por una vía no sesgada hacia lo ya conocido — sellos underground concretos (Nuclear War Now!, Iron Bonehead) en vez de tags — y que además responda la pregunta que necesita D19/C19: qué fracción de esas bandas no tiene ni tags ni abstract de Wikidata.

---

## D23 — El folk entra, y el corpus deja de definirse por tags
`2026-07-10` · vigente

Pedro añade **folk** al alcance de primera clase: viking folk, nordic/ritual folk, neofolk, celtic folk, pagan folk, folk metal. Wardruna, Heilung, Skáld, Gealdýr y un largo etcétera.

**No rompe el modelo de dominio.** Son grupos con miembros, discos y sellos: la misma forma que una banda de metal. No es el caso de la clásica (D11), que sí exige otro modelo. El esquema no cambia.

**Sí rompe cómo definíamos el corpus.** La semilla pedía a MusicBrainz una lista de tags de géneros. Añadir `folk` a esa lista arrastra el canon folclórico entero. Y el defecto de fondo ya lo conocíamos: las bandas oscuras **no tienen tags**, así que definir el corpus por etiquetas es definirlo por lo que alguien se molestó en etiquetar.

**El corpus pasa a ser: anclas explícitas ∪ lista acotada de tags ∪ expansión por el grafo.**

- **Expansión por grafo** es el criterio principal y el más bonito: Wardruna entra porque Einar Selvik tocó en Gorgoroth. Es una arista `member_of` real, no una opinión sobre qué es folk. Se expande por miembros compartidos, sellos y splits. Lo que no está conectado, no entra. **Bloodline, que era una feature, pasa a ser también el criterio de admisión.**
- **Anclas explícitas** porque la expansión pura dejaría fuera a Skáld y probablemente a Gealdýr, que no comparten miembros con el metal.
- **Tags acotados**: `viking folk`, `nordic folk`, `neofolk`, `pagan folk`, `celtic folk`, `dark folk`, `folk metal`, `ritual folk`. **Nunca `folk` a secas.**

**Nota de verificación**: MusicBrainz devuelve «Tartalo Music» como artista de tipo `Person` (coincidencia exacta), y no lo encuentra como sello. Se registra lo que dice MB, no lo que sea en realidad. No se usa como ancla sin confirmación de Pedro.

---

## D24 — C18 (El Atlas) es la implementación de B22, no otra feature
`2026-07-10` · vigente · resuelve la contradicción detectada por el agente de documentación

`SPEC.md` acabó con dos entradas describiendo lo mismo: **B22 «Constelación»** (proyección UMAP del catálogo con tu nube de gusto encima) y **C18 «El Atlas»** (idem, más la técnica de render: nebulosa pregenerada + estrellas vivas solo cerca de tu vector). Fue un error mío al añadir C18 sin retirar B22.

**Resolución**: B22 queda **retirada como feature independiente**. C18 es su implementación y hereda su intención. **B23 (gaps)** deja de colgar de B22 y pasa a colgar de C18 — las zonas oscuras del Atlas *son* los huecos.

El agente hizo lo correcto al señalarlo y no fusionarlos por su cuenta: elegir entre dos IDs es una decisión de producto, no una edición de estilo.

---

## D25 — Spike v2: R1 contestado, C19 degradado, y una cifra mía que era falsa
`2026-07-10` · vigente · supersede la aritmética de coste de D19

Muestra: 182 artistas publicados por sellos underground (Nuclear War Now!, Iron Bonehead, Fallen Empire), no por tags. Corrige el sesgo estructural del v1.

### Q_A — ¿pueden sonar?

```
iTunes              41%
Deezer              19%
alguno de los dos   52%   <- pool servible de The Rite
NINGUNO             48%   <- insonorizables
```

**R1 queda contestado y no es fatal.** Ni el 8 % que se temía, ni el 90 % que lo habría hecho trivial.

Consecuencias de diseño, ya firmes:
- El pool de The Rite se filtra por `preview IS NOT NULL`. Es una constante del diseño, no un caso raro.
- Casi la mitad de las bandas oscuras **existen en la app pero no se pueden invocar**: salen en búsqueda, ficha y Bloodline, no en el rito.
- Y encaja con la tesis: **las bandas verdaderamente innombrables son inaudibles.** No es un fallo, es lo que significa `Nameless`.

**Dos cavéats honestos**: el 52 % es una **cota inferior** — el emparejado va por nombre exacto y las diacríticas y los nombres estilizados convierten fallos de emparejado en falsos «sin preview». Y **no se ha medido si la cobertura correlaciona con la oscuridad *dentro* del underground**, porque sin key de Last.fm no hay `listeners`.

### Corrección de un error propio

`D19` afirmaba que Deezer resolvía el catálogo en ~8 h frente a las ~250 h de iTunes, y que por tanto Deezer sería la fuente masiva.

**Es falso.** iTunes cubre el 41 % y Deezer el 19 %: **iTunes tiene más del doble de cobertura en el underground.** El solape es de 8 puntos, así que Deezer aporta 11 puntos propios y es un buen complemento, pero **no puede ser la fuente principal.**

Por tanto el límite de **20 req/min de iTunes vuelve a mandar**, y la resolución de previews del catálogo entero deja de ser barata. Se hace **perezosa y por lotes**, nunca de una sentada. La cifra de «8 horas» no debe volver a citarse.

### Q_B — el eje del timbre (C19) baja de necesario a opcional

```
cero tags                    17%
sin wikidata                 72%
cero tags Y sin wikidata     16%   <- el agujero
sin texto PERO con audio      7%   <- lo que el audio rescataría
```

C19 se defendió con: *«las bandas oscuras no tienen texto, su vector es ruido, el audio es la única señal que no se degrada»*. **El agujero es del 16 %, no del 60 %, y el audio solo rescata al 7 %** — trece bandas de 182. Descargar y analizar audio para arreglar un 7 % no se sostiene. **La objeción de Pedro era correcta; el argumento a favor era una corazonada disfrazada.**

Lo que sobrevive: **ya se descarga el preview para servir The Rite**, así que para las bandas del pool los rasgos de audio salen **gratis**, extraídos al vuelo mientras se cachea el audio que ya se está sirviendo. Cero descarga adicional. Con eso siguen en pie la discrepancia texto-vs-audio y la guerra del volumen, pero **como feature opcional tardía, no como arreglo de cobertura.**

C19: `necesario` → `bonito y casi gratis, más adelante`.

### El riesgo que sí apareció

El 72 % no tiene Wikidata, luego no tiene abstract. Su embedding se construye **solo con tags**.

Eso no es ruido: es **señal de baja dimensión**, que es peor porque no se nota. Si cincuenta bandas tienen como única señal el tag `black metal`, sus cincuenta vectores son casi idénticos, y **la búsqueda en anillo (D4) —que es todo el motor— degenera justo en el underground**: todo cae a la misma distancia y el slider Comfort ↔ Abyss no mueve nada. El audio no lo arregla, porque no lo tienes para todas.

Se mide en el spike v3 (`docs/spikes/embedding-collapse.md`). Ver R8.

---

## D26 — El anillo se define en percentiles, y los embeddings se centran
`2026-07-10` · vigente · **corrige el mecanismo central de D4**

Spike v3 (`docs/spikes/embedding-collapse.md`) fue a buscar colapso de vectores en el underground y encontró otra cosa, peor y más silenciosa.

**No hay colapso.** Cero vectores duplicados, cero anillos vacíos. El underground está incluso **más disperso** que las bandas conocidas (rango 0.144 frente a 0.100), probablemente porque las famosas comparten los mismos cuatro tags canónicos y las oscuras tienen etiquetas raras.

**Pero el espacio es una cáscara fina.** Todas las distancias coseno caen entre 0.18 y 0.35. La mediana de vecinos dentro del anillo `[0.15, 0.35]` es de **177 sobre 181**: el anillo contiene el catálogo entero. Es una propiedad conocida de los embeddings de frases cortas y formulaicas — se apiñan en un cono estrecho.

**Consecuencia**: el slider Comfort ↔ Abyss, tal como lo especificaba D4, **no movería nada**. `WHERE emb <=> taste BETWEEN 0.15 AND 0.35` selecciona el 98 % del corpus, se ponga donde se ponga.

### Los arreglos, medidos (spike v3b)

`sep(p10→p70)` = distancia real entre un vecino cercano y uno lejano **de la misma banda**. Es lo que el slider tiene que recorrer.

```
variante                     rango p05..p95   sep(p10→p70)   p05
A  nombre+plantilla+tags        0.1439          0.058       0.204
B  solo tags                    0.3817          0.164       0.005  <- colisiones
C  A centrado                   0.3277          0.187       0.827
D  B centrado                   1.4409          0.678       0.061  <- colisiones
```

**Se adopta C**: texto rico (nombre, tags, país, miembros, sello, y abstract cuando exista) **más centrado del corpus** — restar el vector medio antes de indexar. Triplica la separación, sin efectos secundarios. El vector medio se persiste y se aplica también al vector de consulta.

**Se rechaza D** pese a ser diez veces mejor en dispersión: reducir el texto a los tags hace que dos bandas con el mismo conjunto de etiquetas tengan el mismo texto y el mismo vector. El `p05` de 0.005 **es** ese colapso. El 17 % sin tags acabaría entero en un punto. D cambia una cáscara fina por colisiones.

### El anillo, redefinido

Aun centrado, un radio absoluto no es interpretable. **El anillo se expresa en percentiles de la distribución de vecinos**, no en distancias.

Implementación: se muestrean unos miles de artistas al azar, se calculan sus distancias al vector de gusto del usuario, se obtienen los **radios correspondientes a los percentiles del slider**, y esos dos radios se pasan a la consulta HNSW. Percentiles hacia el usuario, radios hacia el índice. Evita un `ORDER BY` sobre todo el catálogo.

### Defectos declarados del spike

- La métrica «textos únicos 182/182» **es vacía**: el texto incluía el nombre de la banda, luego era único por construcción. La conclusión de «no hay colapso» se sostiene por la distribución de distancias y los anillos, no por esa fila.
- Se usaron tags de **MusicBrainz**, no de Last.fm. Producción usará Last.fm, que es más rico: esto es una cota **pesimista** sobre la señal de tags.
- La columna `anillo_p60_80` de la tabla del v3b no informa: se pidió el 20 % central por construcción y sale el 20 %. Quedó como comprobación de cordura.

---

## D27 — Identidad visual v1 (Gemini): aceptada como dirección, no como entregable
`2026-07-10` · vigente · ver `docs/assets/branding-v1-gemini.png`

Propuesta de icono, wordmark y favicon generada por Gemini a partir del brief de Pedro.

### Qué acierta

**El concepto es la app, no un adorno.** El círculo es el linaje. La línea vertical es el tiempo — el eje del Gantt. El borde derecho que se deshace es la información que se pierde. Es exactamente la pérdida de generación de D14.

**La `I` de sulfuro del wordmark es el eje del tiempo**, y es el único color de todo el sistema.

**Resistió los defectos prohibidos en el brief.** El fondo claro muestreado es `#E5E1DA`, un gris frío sucio — no el crema cálido `#F4F1EA` en el que cae por defecto casi todo modelo generativo. Sin pentagramas, sin blackletter, sin verde ácido.

### Qué no sirve todavía

**El icono lleva el wordmark dentro, y a 16 px muere.** Se verificó bajando el PNG a tamaños reales y reescalando con vecino más cercano (`docs/assets/branding-v1-favicon-test.png`): a 16×16 la palabra GRIMOIRE es ruido y la degradación —que es *la idea*— desaparece. Queda un anillo con una raya: un reloj. La hoja de Gemini mostraba el 16×16 y el 8×8 **ampliados**, que es lo que oculta el problema.

Hace falta **una marca hermana para tamaños pequeños**: sin wordmark, trazo más grueso, y la degradación reducida a tres o cuatro tramos gruesos en lugar de cincuenta hilos finos.

**Falta el SVG.** Un PNG no se puede poner de favicon. Faltan también los nombres y paquetes npm de las tipografías, la paleta con hexes y ratios, el tono de voz, y la decisión arriesgada que el brief pedía. El wordmark **no está en `Redaction`**, así que Q6 (¿existe en npm/fontsource?) sigue abierta y con ella la firma de Q1.

### Contraste, medido

```
bone #E6E2D9 sobre void #0B0B0D    15.21:1  ✓
toner #17161A sobre paper           16.10:1  ✓
sulphur #D6C34A sobre void          11.02:1  ✓
sulphur #D6C34A sobre paper          1.60:1  ✗  ilegible
sulphur oscuro #8F7C18 sobre paper   3.71:1  ✓ trazos y foco (WCAG 1.4.11), ✗ texto
```

El ámbar de Gemini (`#E8BB3B` oscuro / `#E0B434` claro) tiene el mismo problema. **En modo claro se usa la variante oscura, y nunca para texto.**

### Reconciliación con D14

El icono oscuro **tiene grano**, y D14 dice que el semitono va solo en modo claro porque «el vacío no tiene grano». Se resuelve así: **el icono es un objeto impreso; la interfaz no.** El grano vive en el icono y en el modo claro. El fondo oscuro de la app sigue limpio.

### Tensión abierta

El icono ya encarna la pérdida de generación. Si además la tipografía se degrada según el rank de la banda (Q1), hay **dos firmas compitiendo**. Se mantienen ambas porque son de naturaleza distinta —el icono es la marca, la degradación tipográfica es **un dato**— pero queda anotado para que Pedro lo decida a sabiendas.

---

## D28 — Postura de autenticación de la ola 0, con su exposición escrita
`2026-07-10` · vigente

El esqueleto trae ASP.NET Identity + JWT Bearer (access 15 min, refresh 16 días), y dos cosas que no se deciden solas.

**La clave de firma vive en `appsettings.json`** con el valor `dev-only-grimoire-signing-key-change-in-production-…`. Que una clave de desarrollo esté commiteada es normal; lo que no lo es era que **nada impidiese arrancar en producción con ella**. Cualquiera que hubiese leído el repo podría firmarse un token. Se añade una **guarda de arranque**: si el entorno no es `Development` y la clave es la de desarrollo o tiene menos de 32 bytes (HS256 necesita 256 bits), el proceso no levanta y el mensaje dice qué variable poner. Con tests que lo comprueban en ambos sentidos.

La contraseña de Postgres (`grimoire`) también está commiteada, y ahí sí no hay problema: es la misma del `docker-compose` de desarrollo y no protege nada.

**Los refresh tokens no son revocables.** No hay tabla: son *stateless*. Consecuencia, dicha sin adornos:

- Un refresh token robado es válido **dieciséis días**.
- Cerrar sesión **no lo invalida**. No existe logout del lado del servidor.
- Cambiar la contraseña **tampoco** lo invalida.

Para un puñado de amigos es un intercambio aceptable, pero es una **decisión**, no un accidente. El arreglo más barato, cuando toque: una claim de versión de token (o el `security_stamp` de Identity) comprobada en cada refresco — una columna y una comparación. **No se implementa ahora**: se decide explícitamente, no se cuela.

---

## D29 — Decisiones de implementación que tomó la ola 0
`2026-07-10` · vigente

El agente del esqueleto tuvo que resolver seis cosas que la spec no fijaba. Se ratifican todas:

- **PostgreSQL 17, no 16.** La spec decía 16; la imagen `pgvector/pgvector:pg17` es la que está mantenida. Nada de lo que usamos depende de la diferencia.
- **Enums guardados como texto**, no como `smallint`. Legible en `psql` y a prueba de reordenaciones del enum en C#. El coste de espacio es irrelevante a esta escala.
- **`snake_case` en las columnas**, vía `EFCore.NamingConventions`. EF generaba `PascalCase` entrecomillado y el esquema de la spec está en `snake_case`; sin esto, cualquier SQL escrito a mano falla.
- **`releases.mbid` es único globalmente**, y los splits y recopilatorios aparecen bajo varios artistas. Se atribuyen **al primer artista que los importa**. Es arbitrario y hay que revisarlo cuando `credits` exista: un split entre dos bandas pertenece a las dos.
- **El worker nunca siembra solo.** `dotnet run --project src/console/server` sin argumentos imprime el uso y sale. Sembrar exige `-- seed`.
- **`Redaction` sí está en fontsource** — contra lo que yo suponía. Q6 queda contestada, y con ella se desbloquea la firma de la degradación tipográfica por rareza (Q1), que sigue pendiente de Pedro.

---

## D30 — Esquema del Rito: `user_taste` y `rites` se materializan
`2026-07-11` · vigente · ver `docs/progress/rite-engine.md`

Los modelos `UserTaste` y `Rite` existían desde la ola 0 sin tabla (skeleton §3). El motor del Rito los necesita, así que la migración `20260710231224_AddUserTasteAndRites` los crea: `user_taste` (una fila por usuario, `embedding`/`repulsion` `vector(768)`, `depth_score`, FK en cascada) y `rites` (`state` texto `served|summoned|banished|again` — convención D29, índice `(user_id, artist_id)` de SPEC §10). El grimorio del usuario no es tabla: es `rites WHERE state='summoned'` (SPEC §10).

---

## D31 — El anillo se resuelve por percentiles con ventana fija, y sin término de rareza mientras `listeners` sea null
`2026-07-11` · vigente · concreta el mecanismo de D26 · ver `docs/progress/rite-engine.md`

D26 dijo «el anillo se expresa en percentiles». La implementación fija los números que D26 dejó abiertos:

- **Ventana de percentiles de ancho `0.20`** deslizada por el slider Comfort↔Abyss (`comfort ∈ [0,1]`): en 0 el extremo cercano, en 1 el lejano. Se muestrea el pool servible al azar, se calculan sus distancias al `taste` sobre el índice HNSW, y de esa distribución salen los **dos radios** en los percentiles del slider. Percentiles hacia el usuario, radios hacia el índice.
- **Repulsión como radio de seguridad en `p20`**: se excluye el 20 % más cercano al centroide de lo desterrado (D4 «resta activamente»).
- **Se toma uno al azar dentro del anillo**, sin ordenar por rareza. El término `ln(1e6/listeners)` de la query de SPEC §6 **no se aplica todavía** porque `listeners` es null (sin key Last.fm) — ordenar por rareza con el dato ausente sería una mentira. Vuelve cuando exista `listeners`.
- **Invariante de doble-centrado reafirmado**: `taste`/`repulsion` se promedian de embeddings **ya centrados** (`artists.embedding`), luego viven en espacio centrado y **no se resta el vector medio otra vez**. El medio de `corpus_stats` es solo para centrar un vector de consulta *externo* crudo, que este pase no usa. Anotado en `TasteMath`, `UserTaste`, el `DbContext` y `RiteEngine`.

---

## D32 — El audio del Rito se sirve por proxy con URL de capacidad, y el SSRF se cierra dos veces
`2026-07-11` · vigente · ver `docs/progress/rite-engine.md`

`GET /api/rite/{token}/audio` es **anónimo** (el token es el `rite.Id`, un GUID inadivinable = URL de capacidad) y hace stream del preview **en el servidor** (`ResponseHeadersRead`). La URL de origen del preview **nunca** llega al cliente — sin esto devtools revienta la mecánica a ciegas (SPEC §5.3).

**Defensa en profundidad contra SSRF**: la URL a la que hace fetch el proxy **jamás** viene del cliente (es siempre el `preview_url` que resolvió el ETL) y, además, el host destino debe estar en una **allowlist** (CDNs de iTunes/Apple y Deezer) con **redirecciones desactivadas** (`AllowAutoRedirect=false`) para que no salte a otro host. Las dos capas son independientes: aunque una fallara, la otra sostiene.

---

## D33 — Semántica de Summon/Banish/Again y la ventana de segunda oportunidad
`2026-07-11` · vigente · ver `docs/progress/rite-engine.md`

- **Summon**: media móvil exponencial del `taste` hacia la banda con **decay 0.25**; `state=summoned`; **revela** con explicabilidad C4 (distancia, tags y miembros compartidos).
- **Banish**: mueve la `repulsion` hacia la banda; `state=banished`; **no revela** — se juzgó a ciegas, y C3/C20 dependen de no saber qué se rechazó.
- **Again**: skip neutral, ni `taste` ni `repulsion` cambian; `state=again`; no revela.
- **C3 segunda oportunidad**: la exclusión de lo ya riteado deja volver lo desterrado a los **182 días**; served/summoned/again se excluyen siempre.
- Resolver un rito ya resuelto → 409.

---

## D34 — Arranque en frío: se construye la vía de 5 bandas; Last.fm queda como adaptador apagado
`2026-07-11` · vigente · ver `docs/progress/rite-engine.md` y `rite-front.md`

De las dos vías de D15, se construye la de **elegir 5 bandas**: `GET /api/rite/seed-candidates` (las más prolíficas primero, reconocibles, **no a ciegas** — es la pantalla de selección) → `POST /api/rite/seed` calcula `user_taste.embedding` como media de sus embeddings (ya centrados, no re-centra).

**C1 import Last.fm queda BLOQUEADO** por la falta de key (bloqueador vivo, no accidente): el adaptador `IColdStartImport`/`LastFmColdStart` está escrito y es real (no un stub), pero **con feature flag apagado**; `POST /api/rite/import-lastfm` devuelve **503 explícito** en vez de inventar scrobbles. La ruta viva no se puede probar sin key. Con ello, **B15 Ranks / Depth Score sigue bloqueado**: `rank`/`listeners` null, `depth_score` queda en 0 — no se deriva de un dato ausente.

---

## D35 — El término de rareza se enciende como sorteo ponderado, con null neutro
`2026-07-11` · vigente · **supersede la cláusula «sin rareza mientras listeners sea null» de D31** · ver `docs/progress/engine-rarity.md`

Con `listeners` ya poblado (D37), el término de rareza de SPEC §6 entra en el motor: `rarity = ln(1e6 / GREATEST(listeners,1)) * w_rare` (peso `w_rare` constante nombrada, default 0.15).

- **Reordena DENTRO del anillo, no lo sustituye.** Se aplica como **sorteo ponderado (Gumbel-max)**: `argmax_i(rarity_i + g_i)`, selección ∝ `exp(rarity)`. Sesga hacia lo raro sin colapsar a «siempre la banda más rara» — preserva la exploración aleatoria-dentro-del-anillo de D26/D31. Con `w_rare = 0` recupera el uniforme previo.
- **Null neutro (la salvaguarda):** `listeners IS NULL` → término **0**, nunca enorme. Un null es «desconocido», no «rarísimo»; pesa como una banda de 1e6 oyentes y **nunca domina** el sorteo. Sin esto, la cola oscura sin dato de Last.fm ganaría siempre — justo lo contrario de lo que se quiere. Orden: rara (positivo) > desconocida (0) > mega-popular (negativo).

`tag_novelty` (`w_novel`) de §6 sigue sin implementar: este pase encendió solo la rareza.

---

## D36 — Depth Score: cuán lejos, no cuánto
`2026-07-11` · vigente · ver `docs/progress/engine-rarity.md`

`user_taste.depth_score = Σ Points(rank)` sobre las bandas invocadas (`rites.state='summoned'`), con `Nameless=5, Forgotten=4, Hidden=3, Obscure=2, Known=1`, y **`rank null → 0`** (no se inventa). Premia haber llegado lejos, no haber escuchado mucho (SPEC §6). Función pura testeada en fronteras y con null. Se **recalcula en cada `summon`** dentro del mismo `SaveChanges` del `resolve`, y se expone en `GET /api/rite/taste` y en el reveal (campos aditivos, compatibles con el front). Verificado en vivo: Hidden(3) → Nameless(5) → Forgotten(4) acumula 3 → 8 → 12.

---

## D37 — `listeners` se resuelve por MBID, no por nombre; Last.fm queda encendido
`2026-07-11` · vigente · ver `docs/progress/listeners.md`

Verbo nuevo `listeners` (`ListenersJob` + `LastFmEnrichmentSource` tras `IEnrichmentSource` + flag). Puebla `artists.listeners` desde Last.fm `artist.getInfo` y deriva `rank` con `RankCalculator` (ya existente). Rate limit ~5 req/s.

- **Emparejado por `mbid`, no por nombre.** `getInfo?mbid=` devuelve exactamente nuestra entidad — cero ambigüedad. Una pasada por nombre daba más matches pero colaba la banda-equivocada (Avenger/Castle/Stillborn resolvían a la entidad popular). Cuesta ~6 famosas (KISS, LOUDNESS) cuyo mbid en Last.fm difiere del nuestro, pero es el trade correcto bajo D25 para una app del underground.
- **Resultado**: 290/307 con `listeners` y `rank`, 0 inconsistencias. Distribución (los cinco tiers): Known 76, Obscure 104, Hidden 67, Forgotten 28, Nameless 15. **17 quedan null** (incl. el ancla SKÁLD) donde Last.fm no indexa nuestro mbid — null honesto antes que oyentes prestados.
- Con la key en su sitio (memoria local + user-secrets), **C1 (import Last.fm) queda encendido**: verificado en vivo, siembra el gusto desde scrobbles reales. Se supera el bloqueador de B15 en la parte de datos; `depth_score` ya es calculable (D36).

---

## D38 — La dirección de corrosión de Redaction: número alto = nítido
`2026-07-11` · vigente · **corrige la nota errónea de `docs/progress/skeleton.md` §3; confirma `DESIGN.md` §3**

En Redaction el número del corte es **legibilidad, no corrosión**: `redaction-100` es el corte **nítido** y `redaction-10` el **más corroído**. Verificado empíricamente en revisión visual (lo cazó Pedro: `Nameless`, que se había mapeado a 100, se leía mejor que `Forgotten` en 70) y por el peso de los woff2 (el de `10` es el más pesado, cargando el detalle de bordes rotos; el de `100`, el más ligero).

`DESIGN.md` §3 («de 10 casi ilegible a 100 nítida») era **correcto**; la nota de `skeleton.md` («10 = nítido … 100 = corroído») estaba **al revés**, y `redactionCutForRank` (front) heredó la inversión: mapeaba `Known→10` (corroído) y `Nameless→100` (nítido) — lo contrario de la tesis. Corregido a **`Known→100 … Nameless→10`**, con `BASE_REDACTION_CUT = 100` (desconocido → nítido, nunca corroído, misma regla que el null neutro del motor en D35). La función seguía **sin cablear** (Q1), así que la inversión era latente: no renderizaba mal, pero el test afirmaba lo contrario de lo cierto. Test y comentarios corregidos.

---

## D39 — Un rito servido y abandonado no bloquea su banda; el siguiente serve lo supersede
`2026-07-11` · vigente · **refina D33** · hallazgo de la revisión adversarial (V&V round 2)

D33 dejó que el motor excluyera del anillo **todo** lo riteado salvo lo desterrado-viejo (C3). Consecuencia no vista: un rito en estado `Served` que el usuario **nunca resuelve** (pide otra banda sin pulsar Summon/Banish/Again) queda para siempre, y **excluye esa banda del pool permanentemente**. Con el pool servible pequeño (~80 hoy, D25), servir-y-abandonar repetidamente lo **agota** hasta que `serve` devuelve 204 para siempre.

**Decisión**: un `Served` sin resolver **no lleva señal** (el usuario no juzgó nada), así que **no debe contar**. Al servir, se **purgan los `Served` sin resolver del usuario** antes de crear el nuevo — esas bandas vuelven al pool. Los estados resueltos (`Summoned`/`Banished`/`Again`) se conservan: esos sí llevan señal. El `FindAsync` corre **antes** de la purga, así que la banda recién abandonada no se re-sirve en el mismo turno (evita repetición inmediata); vuelve a ser elegible en un serve posterior. Implementado en `RiteController.Serve`.

---

## D40 — La escucha a ciegas resuelve el preview online al servir (just-in-time), a escala de catálogo
`2026-07-11` · vigente · escala el Rito a las 207k del import D5 · ver `docs/progress/jit-preview.md`

Con el catálogo completo (D5, 207k artistas), pre-resolver el `preview_url` de todas es inviable (iTunes 20 req/min — D19). El Rito deja de servir de un pool pre-resuelto y pasa a **resolver el preview online en el momento de servir**:

- **El anillo (D4/D26/D31) filtra solo por `embedding IS NOT NULL`** (se quita el requisito de `preview_url`). El pool de descubrimiento es todo lo embebible (~172k), no lo pre-resuelto (~80).
- **Serve saca varios candidatos** (12) del anillo; para cada uno, si no tiene `preview_url`, lo **resuelve JIT** (`PreviewResolver`: **iTunes primero, Deezer de complemento** — D25; emparejado exacto por nombre, mejor null que banda equivocada), lo **persiste** (cache que crece orgánicamente), y sirve el primero que suene; si ninguno de los 12 suena → 204. Aplicado también a Duelo (C2) y Adivina-la-década (C27).
- **Nada de audio local** (D10 intacto): solo se resuelve la **URL**; el stream sigue por el proxy de capacidad anti-leak (D32), que valida la URL resuelta contra su allowlist (iTunes/Apple/Deezer) antes de servir. La allowlist **no se abrió**.
- **Caché de negativos sin migración**: se reutiliza el marcador `listen:` de `StreamingLinks` para no re-resolver insonorizables (~48%, D25) en cada anillo.
- Coste típico por serve: 0–1 llamada a iTunes. El rate-limit interactivo (600/350 ms por host) se apoya en el retry ante 429; si crece el tráfico concurrente, encolar es el siguiente paso. Sin expiración del `preview_url` cacheado (una URL caducada → 404 del proxy → estado vacío); refrescarla sería un job del ETL.

---

## D41 — `listeners` empareja mbid-primero, con fallback por nombre (cobertura sobre precisión a escala)
`2026-07-11` · vigente · **matiza D37** · ratificado por Pedro

D37 emparejaba `listeners` **solo por MBID** (Last.fm `getInfo?mbid=`) para no coger la banda equivocada (D25). A escala del catálogo completo (D5, 207k) eso **falla masivamente**: Last.fm indexa cada banda bajo **su propio MBID**, que a menudo difiere del de MusicBrainz — así que el lookup por mbid **falla incluso con bandas famosas** (había dos «Iron Maiden» en la base; solo una matcheó). Resultado: **2 639 de 79 729 con tags (3,3%)** tenían rank, lo que **dejaba sin rank al 97%** del catálogo → sin Depth Score, sin degradación tipográfica, sin término de rareza para casi todo. La tesis de rareza iba a medias.

**Decisión**: emparejado **híbrido** — MBID primero (preciso, sin ambigüedad); si no matchea, **fallback por nombre** (`getInfo?artist=NAME&autocorrect=0`) verificado **solo por nombre** (`NameMatch`), **aceptando un MBID distinto** (`ResolveByName`). El coste, que D37 evitaba: un nombre común podría coger la banda equivocada → rank equivocado en esa banda. Con la guarda de nombre exacto normalizado + `autocorrect=0`, el riesgo es bajo y acotado. Pedro lo ratificó: **sin cobertura de rank, media app no funciona**; vale más un rank aproximado para casi todos que uno perfecto para el 3%.

---

## D42 — Metal Archives autoriza el scrape (no comercial, sin martillear) — **supersede D7**
`2026-07-14` · vigente · **supersede D7 y el invariante 2 de `CLAUDE.md`** · pendiente de que Pedro decida si se ejerce

Metal Archives **contestó** al correo de presentación del 2026-07-10 (`docs/outreach/metal-archives.md` §1b). Literal:

> If it would be helpful for you to scrape some data from MA, that's fine as long as it remains for non-commercial use and that you don't hammer the site with requests.

**D7 decía «no se scrapea Metal Archives»** apoyándose en dos patas: (a) un argumento técnico —MA solo aporta un campo irremplazable, la temática lírica— y (b) **palabra dada por escrito**. La pata (b) **la han retirado ellos**. La pata (a) **sigue intacta**: MA no aporta audio (The Rite), ni rank (Last.fm), ni grafo (MB+Wikidata). Sigue aportando exactamente **un facet**.

**Lo que cambia**: el scrape pasa de *prohibido por palabra dada* a **permitido bajo contrato**. Lo que no cambia: **no es gratis en esfuerzo y sigue valiendo un solo facet**. Ejercer el permiso es una decisión de producto, no un automatismo — y no se ha ejercido todavía.

**Las dos condiciones son ahora obligaciones nuestras**, y una de ellas asciende de principio a contrato:

1. **Uso no comercial.** Coincide con D6 (coste cero, gratuito), pero **deja de ser una preferencia interna revocable**: monetizar Grimoire rompería el permiso de MA. Cualquier futura decisión de monetizar **debe** volver aquí primero.
2. **Sin martillear el sitio.** No dieron cifra. **Nosotros la ponemos y erramos por lo lento**: cualquier crawler contra MA irá a **≤ 1 req/s, secuencial, con backoff ante 429/503, `User-Agent` identificable con contacto, y resumible** (como el de `edges`). El permiso es de ellos y se retira solo si lo abusamos.

**El invariante 3 sigue vigente y no se toca**: toda ficha de banda enlaza a su entrada de Metallum. Eso no era condición suya, era palabra nuestra.

**Alcance si se ejerce**: la **temática lírica** (Q4, el único campo irremplazable) y, como mucho, el género canónico de MA. **No** un mirror de MA: eso ni lo permitieron ni hace falta (D17: no reconstruir lo que ya existe). Nada de reseñas.

**Alternativa que sigue abierta**: pedirles el subconjunto por correo (borrador §2 de `outreach/`) en vez de crawlear. Un export es más limpio, más rápido y les cuesta menos ancho de banda que 180k peticiones nuestras — y de paso responde si guardan MBIDs, sin los cuales el emparejamiento con MusicBrainz es por nombre+país+año.

---

## Preguntas abiertas

| | Pregunta | Bloquea |
|---|---|---|
| Q1 | ¿La degradación tipográfica por rareza como firma, o algo más frontal? Ahora es **posible** (ver Q6), pero el icono de D27 ya encarna la pérdida de generación: serían dos firmas | `DESIGN.md` |
| Q2 | Modo claro: ¿flyer fotocopiado, o neutro y limpio para fichas largas? | `DESIGN.md` |
| Q9 | ¿Se ejerce el permiso de MA (D42)? ¿Crawl propio a ritmo lento, o pedirles el export? ¿Y se hacen los **filtros de género opcionales** que sugirió su webmaster —género concreto y **exclusión** de subgénero, manteniendo el ciego aleatorio por defecto? | temática lírica (Q4 ya contestada), filtros del Rito |
| Q5 | Email transaccional gratuito, o v1 sin correos | registro |
| Q8 | A Gemini le faltan el **SVG**, la **marca hermana para tamaños pequeños** (D27), la paleta con hexes, las tipografías con paquete npm, y el tono de voz | favicon, tokens de `ui/` |

### Contestadas

| | Pregunta | Respuesta |
|---|---|---|
| ~~Q4~~ | Respuesta de Metal Archives | **Contestaron el 2026-07-14.** Autorizan el scrape si es no comercial y sin martillear; sugieren filtros de género opcionales. `D42` · `outreach/metal-archives.md` §1b |
| ~~Q3~~ | ¿Cobertura de previews en el underground? | **52 %** puede sonar; el 48 % es insonorizable. D25 |
| ~~Q6~~ | ¿`Redaction` es instalable? | **Sí, está en fontsource.** Contra lo que se suponía. D29 |
| ~~Q7~~ | ¿Agujero de texto en el underground? | **16 %**, no 60 %. El audio rescataría al 7 %. C19 degradado. D25 |

---

## Riesgos vivos

**R1 — Puede que no podamos sonar lo raro.** The Rite depende de previews de iTunes/Deezer. El spike v1 no lo resolvió (D22): su única lectura sobre bandas realmente oscuras (n = 4, 0 % cobertura) es direccionalmente alarmante pero estadísticamente inútil. Si las bandas de menos de 500 oyentes resultan tener cobertura muy baja, el tier `Nameless` es insonorizable y el Depth Score se cae. **Se mide con el spike v2, con un muestreo no sesgado, antes de escribir código de producto.** Ver `docs/spikes/` (pendiente de crear).

**R2 — La ficha está más vacía justo donde la app te lleva.** Los créditos son excelentes para Iron Maiden y pésimos para el sludge finlandés de 300 oyentes. El motor de descubrimiento conduce exactamente a donde el dato no está. La ficha **debe degradar con dignidad**: estados vacíos diseñados, no huecos rotos.

**R3 — Emparejado MA ↔ MusicBrainz sin MBIDs.** Por nombre + país + año. Los ambiguos se quedan fuera. Preguntar a Hellblazer si tienen MBIDs.

**R4 — Sesgo de muestreo del spike.** Se muestrea MB por *tag*, y las bandas oscuras de verdad no tienen tags. La lectura honesta será «cobertura entre bandas tagueadas», no «cobertura general». Repetir muestreando por sellos underground (Nuclear War Now!, Iron Bonehead). El emparejado por nombre además cuela ruido (salió «Toto»; el «Death» con 2 457 fans de Deezer no es el de Chuck Schuldiner). **Materializado en D22**: las cifras del 85 % y del 82 % quedan inutilizables por este mismo motivo.

**R5 — MA cambiará el HTML y las APIs cambian.** Ya pasó: ListenBrainz, que era libre, ahora exige token — *«Due to AI scrapers causing undue traffic on our sites»*. Toda fuente detrás de `IEnrichmentSource` (D9).

**R6 — `Redaction` puede no estar disponible como paquete.** Toda la firma tipográfica de D14/`DESIGN.md` se apoya en tener cortes de corrosión progresiva reales, no simulados. Si no existe en npm/fontsource, el fallback (`Archivo` como display, sin corrosión) es una degradación seria de la identidad visual propuesta. Sin verificar — ver Q6.

**R7 — El eje tímbrico puede no valer su coste incluso en su versión perezosa.** D19 solo reduce el coste, no lo elimina, y la pregunta de fondo (¿hace falta audio si el texto ya cubre casi todo?) sigue sin dato. No se escribe una línea de código de C19 hasta que el spike v2 conteste Q7.
