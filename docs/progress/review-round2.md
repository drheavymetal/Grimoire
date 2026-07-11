# Revisión adversarial de correctitud — ronda 2

Agente de revisión adversarial. Solo lectura. Verifica contra la base viva
(`localhost:5433`, 2501 artistas / 309 embeddings / 80 con preview / 290 con rank) y por lectura de código.
`scripts/audit.sh --strict` en verde (exit 0) al empezar.

## Veredicto

**Cero defectos de correctitud de severidad alta o media confirmados** en la superficie revisada.
El código es excepcionalmente disciplinado en null-handling, fuga a ciegas, SSRF, doble-centrado y
huecos honestos. Lo que sigue son hallazgos de **severidad baja** (uno PLAUSIBLE de producto/UX, dos
notas de robustez), ninguno es un stub disfrazado ni un dato inventado.

Superficie cubierta y **verificada correcta**:
- **Motor (D4/D26/D31/D35)**: anillo por percentiles (`RingResolver` ↔ front `rite.ts`, idénticos),
  término de rareza con null neutro (`RaritySelector.RarityTerm` null→0, Gumbel-max uniforme si w=0),
  sin doble-centrado (`TasteMath`, `LineageMath.Midpoint`, `SemanticController` centra solo el vector
  crudo externo), anillo excluye lo riteado y respeta C3 (banished>182d vuelve), repulsión resta (p20).
- **ETL emparejado**: `CreditResolver` (is_guest, drop fuera de corpus), `MembershipResolver`
  (guest≠miembro, dirección backward/forward, merge de intervalos, fechas parciales),
  `TeacherStudentResolver` (dirección), `NameMatch` (normaliza, undercount honesto), iTunes-primero
  (`ITunesEnrichmentSource` con match exacto de nombre).
- **Grafos (D18)**: BFS `ShortestPath` (camino mínimo real, marca al encolar), `Neighbourhood`,
  `Walk` (no-repite), `LineageGraph` adyacencia undirected band↔persona↔band, colapso del par
  espejo teacher/student (`ComposerLineage`, ambas aristas dedupan a master→apprentice), C5 midpoint.
- **Auth/seguridad (D28/D32)**: guarda de arranque JWT (Program.cs 40-53), refresh con `token_type`
  distinto (no se puede replay un access), proxy de audio SSRF cerrado dos veces (allowlist +
  `AllowAutoRedirect=false` confirmado en Program.cs:130), gift tokens (Data Protection, tamper→null),
  capability URLs, front nunca recibe la URL de origen ni la identidad antes del reveal.
- **Invariantes / no-inventar**: `RankCalculator`/`rank.ts` (null→null, fronteras correctas),
  `DepthScore` (null→0), `redaction.ts` (rank null→corte base 100), `CoverArtCache`
  (404 negativo cacheado, 5xx/timeout NO), `MirrorMath`/`DarkTwinMath`/`WeeklyRiteSelector`
  (estados vacíos honestos, deterministas), `AtlasProjection` (reconstrucción PCA exacta — el
  script offline es PCA real por power-iteration, no UMAP, así que la reconstrucción lineal es válida).

---

## Hallazgos

### H1 — PLAUSIBLE · baja-media · Un rito `Served` abandonado consume una banda para siempre
`src/web/server/Controllers/RiteController.cs:139-144` (exclusión) + `RiteConsole.tsx:154` (invoke siempre visible)

**Escenario**: `POST /api/rite/serve` crea una fila `Served`. El motor excluye del anillo **todo**
lo riteado salvo lo desterrado-viejo (D33: "served/summoned/again se excluyen siempre"). No hay
resolución ni expiración de un `Served` sin resolver. En la UI el botón *Invoke* está siempre visible
(no solo en `idle`), así que un usuario puede pedir otra banda estando una en `listening` sin
resolverla → nueva fila `Served`, la anterior queda huérfana y su banda **excluida permanentemente**.
Con el pool servible actual de **80 bandas** (embedding ∧ preview), una sesión de servir-y-abandonar
agota el pool y `serve` pasa a devolver 204 para siempre para ese usuario.

**Por qué es baja-media y no alta**: es la consecuencia honesta de D33 (decisión explícita) + el pool
pequeño de D25 (48% insonorizable), no un dato inventado. Pero degrada una mecánica central a lo largo
de una sesión y el camino es alcanzable desde la UI.

**Arreglo sugerido** (decisión de producto, no la aplico): o bien resolver/expirar el `Served`
anterior al pedir uno nuevo (un served sin resolver caduca a los N minutos y deja de excluir), o bien
tratar `Served` sin resolver como no-excluyente (re-servible). Requiere entrada en DECISIONS por tocar
la semántica de D33.

### H2 — PLAUSIBLE · baja · Double-checked locking sin barrera en `AtlasProjector`
`src/web/server/Services/AtlasProjector.cs:46-48`

El fast-path `if (_loaded) return _basis;` lee `_loaded` (bool) y `_basis` (referencia) **fuera** del
`SemaphoreSlim`. Dentro del lock se asigna `_basis` y luego `_loaded=true`. En el modelo de memoria de
la CLR las escrituras tienen semántica release (el orden `_basis` antes de `_loaded` se preserva), pero
las lecturas ordinarias **no** tienen semántica acquire garantizada por ECMA en ARM64. En un target ARM
un lector podría ver `_loaded==true` con `_basis` aún null.

**Consecuencia real**: mínima — una única omisión del marcador "you are here" del Atlas, auto-sanada en
la siguiente petición. En x64 (target probable de Cloudmax) no ocurre.

**Arreglo sugerido**: marcar `_loaded`/`_basis` como `volatile`, o usar `Volatile.Read`/`Write`, o
`Lazy<Task<Basis?>>`.

### H3 — Nota (no bug) · El sample del anillo incluye bandas ya riteadas
`src/web/server/Services/RiteEngine.cs:102-106`

La distribución de distancias que fija los radios `rLo/rHi` se muestrea del pool servible **sin**
excluir lo ya riteado, mientras que el resultado `inRing` sí los excluye. Con un pool de 80 y un
usuario que ha juzgado muchas, los radios quedan levemente sesgados respecto a la población elegible.
No es un bug (los radios son aproximados por diseño, D26) — se anota por si el pool crece y se quiere
afinar: bastaría muestrear ya excluyendo, al coste de una subconsulta.

---

## Cosas que verifiqué y descarté (no son bugs)

- **Doble-centrado**: no ocurre en ningún camino. El único `Subtract(raw, mean)` es en
  `SemanticController` sobre un vector crudo de Ollama (correcto, D31).
- **SSRF por redirect**: `AllowAutoRedirect=false` está puesto (Program.cs:130). La allowlist además
  cubre exact host y subdominios de un apex.
- **CAA release vs release-group**: el ETL guarda `releases.Mbid` desde `GetReleaseGroupsAsync`
  (release-**group** MBID), que es justo lo que consume `covers/release-group/{mbid}`. Consistente.
- **Reveal solo en summon**: banish/again no revelan; `depth_score` se recalcula solo en summon sin
  doble-contar (excluye la propia banda y suma su rank a mano); rank null puntúa 0.
- **Refresh token**: lleva `token_type=refresh`; un access no se puede canjear en `/refresh`.
- **Guest≠miembro**: `CreditGrouping` marca guest solo si TODAS las filas performer del artista en el
  release son guest — la distinción de D9 se preserva.
- **AtlasProjection UMAP vs PCA**: `scripts/atlas_project.py` es PCA real (power iteration), no UMAP,
  pese a que SPEC/D20 dicen UMAP. La reconstrucción lineal `Cᵀs/‖s‖²` es exacta para PCA, así que la
  colocación en vivo del taste reproduce el mapa. (Desviación documentada en el propio script, no bug.)
