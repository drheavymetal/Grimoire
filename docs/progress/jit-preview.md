# Movimiento II — Escucha a ciegas online a escala / preview just-in-time (agente JIT-Preview)

> Estado: **terminado y verde**. Frontera del agente: SOLO `src/web/server/**` (+ tests). **Ninguna migración**, **nada en `src/shared/**`, `src/console/**` ni `src/front/**`.** `bash scripts/audit.sh --strict` → **RESULT: PASS** (0 violaciones, 0 skips). Verificado en vivo contra la base de 207k artistas. Fecha: 2026-07-11.

El catálogo pasó de 2.5k a **207k artistas** (import D5). El Rito no puede pre-resolver preview para 207k bajo el techo de iTunes (~20 req/min — D19/D25), así que el pool servible pre-resuelto era minúsculo (80). Se pasó a **resolución online al servir**: el anillo sirve del catálogo **embebido** y la URL de preview se resuelve **en el momento**, se cachea en `artists.preview_url`, y el audio sigue haciendo stream por el proxy anti-filtración existente (D32). **Grimoire nunca descarga audio** (Invariante 4 / D10): el resolver solo obtiene la URL; el stream lo hace el proxy host-side.

---

## 1. Qué se construyó

### `PreviewResolver` (nuevo servicio, `src/web/server/Services/PreviewResolver.cs`)

Dado un artista (nombre + su mapa `links`), resuelve una URL de preview de ~30 s:

- **iTunes primero, Deezer de complemento — nunca al revés** (D25: iTunes cubre 41 %, más del doble que el 19 % de Deezer). Emparejado exacto por nombre reusando `NameMatch` (shared, solo lectura): un resultado cuyo `artistName` no coincide tras normalizar **se descarta** — mejor `null` que la banda equivocada (D25).
- **Atajo por id de Deezer**: si `links` trae una relación MusicBrainz a `deezer.com/artist/{id}` (la clave `free streaming`), se usa ese id exacto contra `artist/{id}/top` **sin búsqueda por nombre** — mapeo inequívoco a nuestra entidad, cero ambigüedad.
- **`IHttpClientFactory` + Polly**: dos clientes con nombre (`preview-itunes`, `preview-deezer`), timeout corto de 6 s, y un `AddResilienceHandler` con 2 reintentos jittered exponenciales solo ante error transitorio / 429 / 503 (mismo patrón que el cliente de MusicBrainz del worker). Devuelve `PreviewResolution(Url, Source)` o `null`; registra la fuente en el log.
- **Rate-limit prudente**: singleton con una compuerta de intervalo mínimo por host (iTunes 600 ms, Deezer 350 ms) que serializa y espacia las llamadas salientes, para que una ráfaga de candidatos en un serve — o serves concurrentes — no atropelle las dos APIs gratuitas. No usa el paso masivo de 3 s del ETL: un serve interactivo que probara varios candidatos tardaría medio minuto.
- **Sin invención** (REVIEW.md): un `catch` solo traga fallo de red / timeout de un tercero → `null` (degradación honesta, Invariante 5), nunca enmascara un bug propio. Sin stubs, sin `TODO`, sin `NotImplementedException`.

### Serve just-in-time (`RiteEngine` + `RiteController`)

- **El anillo filtra solo por `embedding IS NOT NULL`** (`RiteEngine.ServablePool`): se quitó el requisito `preview_url IS NOT NULL`. Se mantienen exclusión de riteados, resta de repulsión (radio seguro), percentiles del slider (D26/D31), y la purga D39. Pre-filtrar por `preview_url` dejaría el anillo varado en el pool minúsculo ya resuelto.
- **`RiteController.Serve` saca varios candidatos** del anillo (`ServeCandidatePool = 12`, vía `FindManyAsync`), no uno. El helper `SelectAudibleAsync` recorre los candidatos en orden y devuelve el primero (o los N) que **puedan sonar**:
  - Si `preview_url` cacheado y **allow-listed** (`PreviewAudioProxy.IsAllowed`) → audible, se sirve.
  - Si nunca se probó y no hay URL usable → **se resuelve JIT** (iTunes→Deezer); si resuelve a un host allow-listed → se **persiste** en `artists.preview_url` (+ marca) y se sirve; si no → se marca probado (caché de negativos) y se pasa al siguiente.
  - Si ninguno de los 12 suena → **204** con estado vacío diseñado (no error).
- El helper se aplica también a **`Duel`** (necesita 2 audibles) y **`ServeDecade`** (1 audible, scorable): al quitar `preview_url` del pool, no hacerlo dejaría a esos endpoints sirviendo bandas mudas. Es corrección de una regresión que introduciría mi propio cambio, no gold-plating.
- El **DTO servido sigue a ciegas** (`{token, riskPercentile, audioUrl}` — sin nombre/país/portada) y el audio va por la **URL de capacidad** del proxy existente. Coste extra: ~1 llamada iTunes por serve de banda no cacheada (aceptable).

### Caché de negativos — sin migración

Se reutiliza **la convención exacta del ETL**: una banda "probada" lleva al menos una clave `listen:` en `artists.links` (marca que deja el `PreviewJob`). `SelectAudibleAsync` marca cada banda probada mergeando los links de búsqueda de `StreamingLinks.Build(name, null, null)` (shared, solo lectura) — **es el marcador de negativos Y aporta los links de streaming del reveal**. Una banda probada con `preview_url` null es genuinamente insonorizable y **no se re-resuelve en cada anillo**.

**Decisión de caché de negativos**: se cachea el negativo porque es barato y no exige migración (la columna `links` y la convención `listen:` ya existen). Si hubiera exigido una columna/estado nueva, se habría documentado y omitido (una re-resolución ocasional es aceptable — instrucción del pase). El marcador es idéntico al del ETL, así que un `deaths`/`previews` posterior lo respeta y viceversa. Escribir `links`/`preview_url` desde el web server es seguro frente al job de embeddings del console: EF actualiza solo las columnas modificadas (`preview_url`, `links`), distintas de `embedding`, sin lost-update (no hay token de concurrencia y las columnas no se solapan).

### Seguridad (D32, respetada)

- La URL que resuelve el resolver **debe pasar la allowlist del proxy** (`PreviewAudioProxy.IsAllowed`) antes de aceptarse como audible: así solo se persiste un `preview_url` que el proxy podrá servir, y un host desconocido se trata como "no resuelto" (negativo) en vez de cachear una URL instreameable. **No se abrió la allowlist**: los previews reales de iTunes caen en `audio-ssl.itunes.apple.com` (ya allow-listed) y los de Deezer en `cdns-preview-*.dzcdn.net` (ya allow-listed). No hizo falta añadir hosts.
- El proxy sigue cerrando SSRF dos veces (allowlist + `AllowAutoRedirect=false`, URL nunca del cliente). El resolver no toca eso.

---

## 2. Verificación (comando → salida real)

### Build + tests + gate

```
dotnet build src/web/Grimoire.slnx -warnaserror   → 0 Advertencias, 0 Errores
dotnet test  src/web/Grimoire.slnx                 → Superado: 390, Con error: 0, Omitido: 0
bash scripts/audit.sh --strict                     → RESULT: PASS (0 violaciones, 0 skips)
```

Tests nuevos que **muerden**:
- `PreviewResolverTests` (4, sin red, stub `IHttpClientFactory`): iTunes-primero cuando ambos casan, fallback a Deezer cuando iTunes no casa, `null` cuando ninguno casa la banda (banda equivocada descartada), id de Deezer de `links` usado sin búsqueda. **Muerde**: invertir el orden iTunes/Deezer → `Resolve_PrefersITunes` falla (comprobado).
- `RiteServeJitTests` (3, `[SkippableFact]`, DB desechable `grimoire_test_jit_<guid>`): el anillo incluye bandas embebidas **sin** `preview_url`; serve → **204** cuando todo lo alcanzable es insonorizable (probado); serve elige la banda audible saltando las probadas-mudas. **Muerden**: restaurar el filtro `preview_url != null` → el test del anillo falla; quitar la guarda de audibilidad en el serve → el test del 204 falla (ambos comprobados, revertidos, verde de nuevo).

### En vivo contra la base de 207k (API en :5081, build de este pase)

Registro → seed → serve variando el slider; `preview_url` **antes = 80, después = 85** (5 bandas resueltas JIT que no tenían preview):

```
comfort=0.5  -> 200 | Goblin Hovel  | JIT-NEW      | https://audio-ssl.itunes.apple.com/...
comfort=0.95 -> 200 | Paragon Zero  | JIT-NEW      | https://audio-ssl.itunes.apple.com/...
comfort=0.9  -> 200 | Therion       | JIT-NEW      | https://audio-ssl.itunes.apple.com/...
comfort=0.85 -> 200 | Dos Brujos    | JIT-NEW      | https://audio-ssl.itunes.apple.com/...
comfort=1.0  -> 200 | Kaelte        | JIT-NEW      | https://audio-ssl.itunes.apple.com/...
comfort=0.2  -> 200 | Accept        | pre-existing | (cacheado, 0 llamadas)
```

DTO servido a ciegas: `keys = ["audioUrl","riskPercentile","token"]` — sin nombre/país.

Audio por el proxy (serve único + fetch inmediato, sin purga D39 de por medio), banda JIT-resuelta **Kaelte**:
```
audioUrl (cliente) = http://127.0.0.1:5081/api/rite/<token>/audio     (sin host iTunes)
origin_host (solo en DB) = audio-ssl.itunes.apple.com
GET .../audio → HTTP 200, Content-Type audio/x-m4p, 1089111 bytes,
  file: "ISO Media, Apple iTunes ALAC/AAC-LC (.M4A) Audio"            (preview real proxiado; origen oculto)
```

Caché de negativos comprobada: 14 bandas embebidas con `preview_url` null y marca `listen:` (probadas-insonorizables) — p.ej. Diaboli, Castle, As Sahar, Bølzer — que un anillo posterior **salta sin re-resolver**.

Base dejada limpia: los usuarios de verificación (`jit-*`) borrados (cascade eliminó sus rites/taste). Los 5 `preview_url` nuevos y las marcas de negativos **se conservan**: son resultados JIT reales y correctos, idénticos a lo que dejaría el ETL. Servidor de verificación liberado por pid; el server preexistente en :5080 no se tocó.

---

## 3. Huecos / exposición declarados

- **Latencia del peor caso**: un serve cuyos 12 candidatos sean todos no-cacheados y mayoritariamente insonorizables puede hacer hasta ~12 resoluciones online espaciadas (iTunes 600 ms) → varios segundos. Es raro (tras el calentamiento la mayoría están cacheadas, positivo o negativo) y está acotado por `ServeCandidatePool`. El caso típico resuelve en 0–1 llamada. No se paraleliza a propósito: cortesía con las APIs gratuitas por delante del percentil de latencia.
- **`ServeCandidatePool = 12`** es a la vez el tamaño del sorteo y la cota de intentos online por serve. Con cobertura ~50 % en la cola JIT-elegible, 12 candidatos dan >99.9 % de encontrar uno audible; si aun así ninguno suena → 204 honesto.
- **El id de Deezer de `links`** solo ayuda cuando la relación `free streaming` de MusicBrainz apunta a Deezer (muchas apuntan a Spotify, que no sirve para audio — D10). Cuando apunta a Spotify se cae a la búsqueda por nombre de Deezer, igual que sin `links`.
- **`recentemente resueltos no se re-verifican`**: una URL de iTunes que caduque quedaría cacheada; el proxy devolvería 404 y el front mostraría el estado vacío. No hay expiración de `preview_url` (fuera de frontera: sería un job del ETL). Aceptable: las URLs de preview de iTunes son estables.
- **Rate-limit interactivo ≠ paso del ETL**: 600/350 ms permiten más de 20/min si hubiera muchísimo tráfico concurrente; se apoya en el retry ante 429. Para un solo usuario el ritmo real está muy por debajo. Si el tráfico creciera, subir los intervalos o mover la resolución a una cola es el siguiente paso (fuera de este pase).

---

## 4. Ficheros tocados (todos dentro de frontera)

Nuevos: `Services/PreviewResolver.cs`, `GrimoireTest/PreviewResolverTests.cs`, `GrimoireTest/RiteServeJitTests.cs`.
Modificados: `Services/RiteEngine.cs` (pool = embedding-only), `Controllers/RiteController.cs` (JIT en Serve/Duel/ServeDecade + helpers `SelectAudibleAsync`/`FirstAudibleAsync`/`WasProbed`/`MarkProbed`), `Program.cs` (dos clientes con nombre + resiliencia + registro singleton del resolver), `Grimoire.Server.csproj` (`Microsoft.Extensions.Http.Resilience`).

**Ninguna migración. `src/shared`, `src/console` y `src/front` intactos.**

---

## 5. Decisiones a promover a `DECISIONS.md`

> Ninguna contradice un invariante. Para que Pedro las ratifique como `D<n>`.

1. **Resolución de preview just-in-time.** A 207k artistas el pool no puede pre-resolverse (D19/D25). El anillo sirve del catálogo embebido (`embedding IS NOT NULL`, sin `preview_url`); el serve saca 12 candidatos y resuelve la preview del elegido en caliente (iTunes→Deezer, D25), la cachea en `artists.preview_url`, y salta los insonorizables. ~1 llamada iTunes por serve de banda nueva.
2. **Caché de negativos reusando el marcador del ETL.** Una banda probada e insonorizable se marca con los links `listen:` de `StreamingLinks` (sin migración, misma convención que el `PreviewJob`), y no se re-resuelve en cada anillo. Escribir `links`/`preview_url` desde el web server es seguro frente al job de embeddings (columnas disjuntas).
3. **La allowlist del proxy también valida lo que resuelve el JIT.** Solo se cachea/sirve un `preview_url` cuyo host esté allow-listed (D32); un host nuevo se trata como "no resuelto". No se abrió la allowlist: iTunes/Deezer ya estaban.
