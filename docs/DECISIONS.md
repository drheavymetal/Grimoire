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

## Preguntas abiertas

| | Pregunta | Bloquea |
|---|---|---|
| Q1 | ¿La degradación tipográfica por rareza como firma, o algo más frontal? | `DESIGN.md` |
| Q2 | Modo claro: ¿flyer fotocopiado, o neutro y limpio para fichas largas? | `DESIGN.md` |
| Q3 | Resultado del spike de cobertura de previews | umbrales de rank |
| Q4 | Respuesta de Metal Archives | temática lírica curada |
| Q5 | Email transaccional gratuito, o v1 sin correos | registro |

---

## Riesgos vivos

**R1 — Puede que no podamos sonar lo raro.** The Rite depende de previews de iTunes/Deezer. Si las bandas de menos de 500 oyentes tienen un 8 % de cobertura, el tier `Nameless` es insonorizable y el Depth Score se cae. **Se mide antes de escribir código de producto.** Ver `docs/spikes/`.

**R2 — La ficha está más vacía justo donde la app te lleva.** Los créditos son excelentes para Iron Maiden y pésimos para el sludge finlandés de 300 oyentes. El motor de descubrimiento conduce exactamente a donde el dato no está. La ficha **debe degradar con dignidad**: estados vacíos diseñados, no huecos rotos.

**R3 — Emparejado MA ↔ MusicBrainz sin MBIDs.** Por nombre + país + año. Los ambiguos se quedan fuera. Preguntar a Hellblazer si tienen MBIDs.

**R4 — Sesgo de muestreo del spike.** Se muestrea MB por *tag*, y las bandas oscuras de verdad no tienen tags. La lectura honesta será «cobertura entre bandas tagueadas», no «cobertura general». Repetir muestreando por sellos underground (Nuclear War Now!, Iron Bonehead). El emparejado por nombre además cuela ruido (salió «Toto»; el «Death» con 2 457 fans de Deezer no es el de Chuck Schuldiner).

**R5 — MA cambiará el HTML y las APIs cambian.** Ya pasó: ListenBrainz, que era libre, ahora exige token — *«Due to AI scrapers causing undue traffic on our sites»*. Toda fuente detrás de `IEnrichmentSource` (D9).
