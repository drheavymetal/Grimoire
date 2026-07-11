# Movimiento V — Escenas (agente Escenas)

> Estado: **terminado y verde**. Frontera respetada al 100 %: **solo `src/web/server/**` y `src/front/**`** (+ tests). Cero migraciones, cero `src/shared/**`, cero `src/console/**`. `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones, 0 skips, 4 gates verdes). Fecha: 2026-07-11.

Ola V del plan autónomo. Consume el esquema y los datos existentes sin cambiarlos: 2478 artistas (306 con `city`, 295 con `formed_year`, 305 con tags, 309 con embedding, 290 con rank), 5320 releases, 179 labels + 299 releases con `label_id`, ~21k credits, corpus mean en `corpus_stats`. Ollama (`nomic-embed-text`) vivo en `:11434`.

---

## 1. Features cerradas (endpoint + UI + i18n es/en + estado vacío + verificado en vivo)

| ID | Feature | Backend | Frontend |
|---|---|---|---|
| **B20/C11** | **Escenas** (ciudad + década + tag, NO mapa de países — D17) | `ScenesController` `/api/scenes` + `SceneClusterer` (puro) | `/scenes` + nav |
| **B21** | **Sellos como puerta** | `LabelsController` `/api/labels`, `/api/labels/{id}` | `/labels` + `/label/$id` + nav |
| **B2** | **Búsqueda semántica** («algo como Neurosis pero más lento») | `SemanticController` `/api/semantic` + `OllamaEmbedder` | pestaña «por significado» en `/` |
| **B24** | **Comparar dos bandas** | `CompareController` `/api/compare` + `CompareMath` (puro) | sección en `/explore` |
| **C24** | **Banda de un solo álbum** | `CatalogueController` `/api/catalogue/one-album` + `CatalogueMath` | sección en `/explore` |
| **C25** | **El hiperprolífico** | `/api/catalogue/hyperprolific` + `CatalogueMath` | sección en `/explore` |
| **C6** | **Muro de portadas** | `CoversController` `/api/covers/wall` (una portada por banda, debut, por listeners) | sección en `/explore` |
| **C9** | **Grafo de splits** | `SplitsController` `/api/splits` + `SplitTitle` (puro) — reusa el motor de grafo D18 | sección en `/explore` |
| **C22** | **Regala un descubrimiento** (boca abajo, firmado, se revela si gusta) | `GiftController` — **stateless vía ASP.NET Data Protection** (sin migración) + `GiftToken` (puro). Reusa `PreviewAudioProxy` (anti-leak D32) | `/gift/$token` (recibe, reusa `RitePlayer`) + `GiftButton` en la ficha |
| **C23** | **Grimorios cruzados** | `RiteController` `/api/rite/grimoire/code` + `/compare?other=` | sección en `/grimoire` [auth] |

**Motor semántico (B2)**: el texto libre se embebe con el mismo `nomic-embed-text` que indexó el corpus (mismo shape, sin prefijo de tarea), se **centra restando el mean de `corpus_stats`** (D26/D31 — el mean existe justo para traer un vector de consulta externo al frame indexado) y se busca por HNSW. Ollama caído → **503 honesto**, nunca ranking inventado.

**Regalo (C22)**: el token es un payload cifrado (Data Protection) — opaco (el receptor no lee la banda del enlace) y anti-manipulación (un token forjado no abre). **Sin fila de BD, sin migración.** El audio va por el mismo proxy anti-leak del Rito. Solo se puede regalar una banda con preview (si no, no puede sonar a ciegas → 422).

**La firma tipográfica por rank (D38)** se reusa vía `RankedName` en el reveal del regalo; en las listas nuevas los nombres van en `font-display` plano (legibilidad, igual que los resultados de búsqueda — coherente con el hueco declarado en `visual.md` §5 de no corroer texto pequeño).

---

## 2. Verificación en vivo (comando → salida real, API :5080 + Postgres :5433 + Ollama :11434)

```
scenes(minSize=3)   → 14 clusters. Tampa 1980 death metal (4), Stockholm 1990 metal (3),
                      Gothenburg presente (melodeath). Real, no inventado.
labels              → 179 labels. Peaceville (GB, 8): Darkthrone, Rotting Christ, Candlemass,
                      Autopsy, Opeth, My Dying Bride — roster real.
compare Megadeth↔Slayer → tags [heavy/metal/speed/thrash], jaccard 0.444, dist 0.560,
                      miembro compartido Kerry King.
semantic "slow heavy doom sludge" → The Gates of Slumber, Crowbar, Oceans of Slumber (doom/sludge).
one-album           → 6 (Heaven & Hell — The Devil You Know, etc.).
hyperprolific       → 7 (King Gizzard 25 rel/16 años ratio 1.56; Drowning the Light 23/23).
splits              → 6 nodos, 3 aristas (Cianide↔Coffins es un split real).
cover wall(12)      → 12 bandas distintas, su debut (Metallica—Kill 'Em All, Black Sabbath—Black Sabbath…).
gift C22            → token NO contiene el id de banda; peek muestra la nota (a ciegas);
                      reveal → Cirith Ungol; token manipulado → 404.
crossed C23         → alice/bob: theirsOnly=[Abruptum], yoursOnly=[Abominator], shared=[1914];
                      comparar con el propio código → 400.
```

**Anti-cascarón (reactividad contra Postgres)**: se anuló el `label_id` de un release de Peaceville → `/api/labels` bajó de **8 a 7**; restaurado → vuelve a **8**. La vista lee la base viva.

**Gate**: `bash scripts/audit.sh --strict` → **RESULT: PASS**. `dotnet test` → **248 pasan** (+32 nuevos: SceneClusterer 6, CatalogueMath 3, CompareMath 6, SplitTitle 4, GiftToken 5, y los que muerden dentro). `pnpm test` → **74 pasan**. `pnpm lint` 0 errores (warnings fast-refresh preexistentes). `pnpm build` ✓.

**Tests que muerden (comprobado)**: la lógica de negocio de estas features vive en el backend (xUnit): clustering de escenas (multi-tag → varias escenas; floor de tamaño), one-album/hiperprolífico en fronteras (ratio > 1 estricto; divide-by-zero al año de formación), Jaccard de tags (case, vacío), parse de splits, y el round-trip + manipulación del token de regalo (garantiza que la banda no se filtra en el enlace). El front es presentacional sobre hooks de `core/`, sin lógica nueva testeable aparte.

Base dejada limpia: **0 usuarios / 0 rites / 0 taste** (borrados los de prueba); corpus intacto (2478 / 5320 / 179).

---

## 3. Huecos declarados (y su porqué)

**Fuera por falta de dato en este entorno (declarado en el brief):**
- **C7 duración como eje** — la duración de grabación no se importó (`credits.recording_id` es un UUID suelto, sin tabla Recording ni columna de duración). Sin ese número no hay eje de duración. **Fuera, declarado.**
- **C10 grafo de versiones** — la tabla `works` está vacía y no hay relaciones de cover. **Fuera, declarado.**
- **C21 minería de títulos** — no hay recordings con título en la base (solo el UUID). Sin títulos no hay minería. **Fuera, declarado.**

**Cerradas parcialmente, con su límite honesto:**
- **C26 deriva cromática / paleta dominante de C6** — **declarado, no construido.** El muro (C6) sí se construyó, pero la paleta dominante calculada en cliente exigiría leer píxeles de imágenes servidas por el proxy (`:5080`) desde el front (`:5173`) → **canvas contaminado por cross-origin** (`getImageData` falla sin cabeceras CORS en las imágenes). Arreglarlo tocaría el proxy de portadas (cabeceras CORS + `crossOrigin="anonymous"`), un cambio de riesgo fuera del corazón de la feature. La rejilla de portadas reales es la parte con valor; la paleta es guarnición. Declarado, no inventado.
- **C9 grafo de splits — realmente escaso, no roto.** Solo **3 aristas / 6 nodos** resuelven ambos lados al corpus. Razón de datos: MusicBrainz modela el split con un único artista dueño (D29), y la mayoría de compañeros de split de este corpus de ~2.5k **no están sembrados**. El endpoint reusa el motor de grafo y el emparejado exacto-normalizado del ETL (D25): un fallo descarta la arista, nunca cuela la banda equivocada. El front pinta estado vacío diseñado si no resuelve nada. La feature crecerá gratis cuando el corpus crezca; el motor ya está listo.

**No construido (task #9, «si da tiempo»):**
- **B12 «el disco donde cambió todo»** (máxima rotación de formación por release) — **fuera, declarado.** Exige cruzar aristas `member_of` con fechas contra las fechas de cada release (intersección de intervalos por disco) — cómputo no trivial, y quedó tras cerrar las ocho features de más valor con datos ricos. El dato existe (2342 aristas con fechas + releases con `release_date`); es implementable en un pase siguiente sin migración.

---

## 4. Ficheros tocados (todos dentro de frontera)

**Backend nuevos** (`src/web/server`): `Controllers/{Scenes,Labels,Catalogue,Compare,Semantic,Gift,Splits}Controller.cs`; `Services/{SceneClusterer,CatalogueMath,CompareMath,OllamaEmbedder,GiftToken,SplitTitle}.cs`; `Dtos/{Scenes,Labels,Catalogue,Compare,Semantic,CoverWall,Gift}Dtos.cs`.
**Backend modificados** (aditivo): `Controllers/{Covers,Rite}Controller.cs`, `Dtos/RiteDtos.cs`, `Program.cs` (registro del `OllamaEmbedder`).
**Backend tests nuevos**: `SceneClustererTests`, `CatalogueMathTests`, `CompareMathTests`, `SplitTitleTests`, `GiftTokenTests` (todos muerden).

**Front nuevos** (`src/front/src`): `ui/pages/{Scenes,Labels,Label,Explore,Gift}Page.tsx`, `ui/GiftButton.tsx`, `core/hooks/{useScenes,useLabels,useCatalogue,useSemanticSearch,useCoverWall,useGift,useCrossedGrimoires}.ts`.
**Front modificados** (aditivo): `core/domain/types.ts` (tipos + `GraphEdge.kind` acepta `'split'`), `core/api/client.ts` (12 métodos), `ui/pages/{Search,Artist,Grimoire}Page.tsx`, `ui/Cover.tsx` (prop `className` opcional para el muro), `ui/routes.tsx`, `ui/Layout.tsx` (nav: Scenes, Labels, Explore, con `flex-wrap`), `locales/{en,es}.json`.

Migraciones: **ninguna**. Modelos compartidos: **no tocados**.
