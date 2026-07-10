# Movimiento II — Ficha (agente Ficha)

> Estado: **terminado y verde**. Discografía por tipo con portadas reales, proxy+caché de Cover Art Archive (incluidos los 404), y estados vacíos diseñados en toda la ficha. Frontera respetada: solo se tocó `src/web/server/**` y `src/front/**`. Fecha: 2026-07-11.

Registra qué existe, qué se verificó (comando → salida real), y los huecos con su porqué. Complementa `docs/progress/skeleton.md` (movimiento I).

---

## 1. Qué existe (nuevo en este pase)

### Backend (`src/web/server/`)

- **`Services/CoverArtCache.cs`** — resuelve la portada de un release-group desde el Cover Art Archive (feature B6, D6: fuente gratis, caché en disco, sin object storage). Contrato:
  - Pide `release-group/{mbid}/front-500` (miniatura JPEG de 500 px; CAA la sirve siempre como `image/jpeg`, verificado contra MBIDs reales).
  - **Cachea los aciertos** como `{mbid}.jpg` y **los 404 como marcador `{mbid}.404`** (fichero vacío): una banda sin portada se pregunta **una sola vez**.
  - **Los fallos transitorios (5xx, timeout, error de red) NO se cachean**: se reintentan en la siguiente petición. Distinguir 404 (hecho) de 503 (transitorio) es el punto delicado y está cubierto por test.
  - Escritura atómica (fichero temporal + `File.Move` con overwrite) para que un lector concurrente nunca vea una entrada a medio escribir.
  - Directorio configurable (`CoverCache:Directory`); por defecto una carpeta temporal por máquina (`{TMPDIR}/grimoire-cover-cache`) para **no ensuciar el repo**. Producción lo sobrescribe con `CoverCache__Directory` apuntando a un volumen montado.
  - `HttpClient` tipado con User-Agent `Grimoire/0.1 ( pmanso@go2chain.es )` y timeout de 15 s.
- **`Controllers/CoversController.cs`** — `GET /api/covers/release-group/{mbid:guid}`. Found → `PhysicalFile(..., image/jpeg)` con `Cache-Control: public, max-age=604800`. NotFound → 404 con `max-age=86400`. Transitorio → 503. **El cliente nunca golpea CAA directamente.**
- **`Program.cs`** — registra `CoverCacheOptions` + el `HttpClient` tipado de `CoverArtCache`.
- **`Dtos/ArtistDtos.cs`** + **`Controllers/ArtistsController.cs`** — `ReleaseDto` ahora incluye `Mbid` (el release-group MBID), para que el front construya la URL de portada. Sin esto no hay forma de pedir la carátula.

### Front (`src/front/`)

- **`core/api/client.ts`** — `GrimoireClient.coverUrl(mbid)`: construcción **pura** de la URL proxy (sin fetch, sin DOM), portable a React Native (invariante 6). El `<img>` de UI la consume.
- **`core/domain/types.ts`** — `Release.mbid` añadido.
- **`core/domain/redaction.ts`** (NUEVO) — función **pura** `redactionCutForRank(rank)` que mapea rareza → corte de corrosión de Redaction (`10` nítido … `100` corroído), más `BASE_REDACTION_CUT = 10`. **NO está cableada a ningún componente**: elegir corte por rank mientras el rank es null renderizaría una mentira (CLAUDE.md, D14/Q1). Queda con tests para el futuro. La ficha usa el **corte base** (la cara `@fontsource/redaction`).
- **`ui/Cover.tsx`** (NUEVO) — portada de release. Estado `loading | loaded | missing`. Si CAA no tiene portada (el `<img>` recibe 404 del proxy → `onError`), muestra un **estado vacío diseñado** ("Sin portada" / "No cover") como sobre en blanco, no un icono de imagen rota. i18n en ambos idiomas.
- **`ui/pages/ArtistPage.tsx`** — ampliada:
  - **Discografía por tipo con la demo visible**: cada release muestra su portada (`<Cover>`); la **demo es un grupo etiquetado propio**, nunca bajo desplegable (SPEC §4).
  - **Rango**: muestra el rank real; si es null (todo el corpus hoy) → estado vacío "Aún sin inscribir". No inventa rank.
  - **Biografía**: `abstract` real; si falta → estado vacío diseñado.
  - **Linaje (Bloodline)**: itera `data.edges` reales (kind, instrumentos, fechas, enlace al artista conectado); si está vacío → estado vacío "Linaje aún sin trazar". Verificado que consume datos reales insertando y borrando una arista en Postgres.
- **`locales/en.json` + `locales/es.json`** — claves nuevas en **ambos** catálogos: `cover.*`, `edgeKind.*`, `artist.bio/noBio/lineage/noLineage/rank`.
- **Vitest** añadido (`package.json`, `vite.config.ts`) con entorno **node** (los tests de `core/` corren sin navegador, D12). Tests: `core/domain/redaction.test.ts`, `core/api/client.test.ts`.

---

## 2. Verificación (comando → salida real)

**`bash scripts/audit.sh --strict`** → `RESULT: PASS` (7/7; gates `dotnet-build`, `dotnet-test`, `pnpm-lint`, `pnpm-build` en verde; 0 skips, 0 violations).

**`dotnet test src/web/Grimoire.slnx`** → `Superado: 25` (los 22 del esqueleto + 3 de `CoverArtCacheTests`).

**Proxy de portadas, contra CAA y Postgres vivos** (API en `:5080`):
```
positive (Darkthrone RG real con portada):  HTTP 200  image/jpeg  45027 bytes
positive de nuevo (servido de disco):        HTTP 200  image/jpeg  45027 bytes
negative (RG sin portada):                   HTTP 404  cache-control: public, max-age=86400
negative de nuevo (servido de disco):        HTTP 404
```
Contenido del directorio de caché tras las llamadas:
```
3ab57384-...-15ffdd.jpg   45027 bytes   (acierto cacheado)
76df3287-...-15ffdd.404   0 bytes       (404 cacheado)
```

**`ReleaseDto` lleva `mbid`** (contra `/api/artists/{Darkthrone}`):
```
release keys: ['id', 'mbid', 'title', 'type', 'releaseDate', 'coverUrl']
sample: Soulside Journey 7d4f11c8-0409-325e-a584-94c0812118c5 Album
edges: 0 | abstract: None | rank: None   ← todos los estados vacíos se ejercitan
```

**El linaje consume datos reales, no es un cascarón** (insertar → GET → borrar):
```
insert artist_edges (Darkthrone → otro, MemberOf, 1988–1993, [guitar,vocals])  → INSERT 0 1
GET /api/artists/{Darkthrone}  → edges: [{ kind:MemberOf, beginDate:1988-01-01, ... }]
delete  → DELETE 1  → edges after cleanup: 0
```

**`cd src/front && pnpm test`** → `Test Files 2 passed (2) · Tests 6 passed (6)`.

**Tests que muerden — comprobado invirtiendo la lógica**:
- Invertir el mapa de `redactionCutForRank` (Known↔Nameless) → `2 failed | 4 passed`. Restaurado → `6 passed`.
- Hacer que el 503 transitorio escriba marcador negativo en `CoverArtCache` → `TransientFailure_IsNotCached_AndSecondCallRefetches [FAIL]`. Restaurado → `3 passed`.

---

## 3. Huecos que quedan (y por qué)

- **Front sin captura E2E de navegador.** Igual que el esqueleto: verificado por `pnpm lint` + `pnpm build` + `pnpm test` y contra la API viva (contrato de datos, proxy de portadas, y prueba de que el linaje reacciona a un cambio en Postgres). No se levantó un navegador headless para una captura de render — no hay herramienta de navegador en este entorno.
- **La discografía muestra miniatura + título + año, no créditos por disco (B9) ni el Gantt (B7).** Fuera de alcance de este agente (B7/B9 son movimientos III/IV y dependen de `artist_edges`/`credits`, propiedad del agente ETL). La sección de linaje es un render mínimo y honesto de lo que hay en `artist_edges`, con estado vacío diseñado; el Gantt real llega después.
- **Sin degradación tipográfica por rank en la UI.** Deliberado: el rank es null en todo el corpus y Q1 sigue sin ratificar (CLAUDE.md). La función pura `redactionCutForRank` existe y está testeada para cuando el rank exista, pero **ningún componente la llama**; la ficha usa el corte base.
- **La imagen de portada del proxy se sirve como miniatura JPEG de 500 px** (`front-500`). Suficiente para la lista de discografía. Si más adelante se quiere un muro de portadas a mayor resolución (C6), basta parametrizar el tamaño.

---

## 4. Notas para el coordinador / decisiones a ratificar

- **Discrepancia de dirección de Redaction entre docs.** `docs/DESIGN.md` §3 dice "`10` (casi ilegible) a `100` (nítida)", pero `docs/progress/skeleton.md` (verificado contra fontsource 5.2.5) y la realidad del paquete dicen lo contrario: **`10` = nítido, `100` = más corroído**, y el mapa correcto es Known→10 … Nameless→100. Seguí la realidad verificada del paquete. `DESIGN.md` es un documento de propuesta (Q1/Q2 sin ratificar) y está fuera de la frontera de este agente; queda anotado para corregirlo cuando Pedro cierre Q1.
- **Caché de portadas en disco: ruta por defecto en temp.** No se añadió una ruta al repo ni se tocó `.gitignore` (fuera de frontera). Producción debe montar un volumen y fijar `CoverCache__Directory`. El `build/production/docker-compose.yml` (fuera de frontera) necesitará esa variable + volumen cuando se despliegue.
- **`Release.Mbid` es el MBID del release-group**, que es lo que CAA indexa para `/release-group/{mbid}/front`. Correcto para portadas de discografía.
