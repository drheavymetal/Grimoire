# Spike — ¿tienen audio las bandas oscuras?

Un *spike* es un experimento acotado cuyo único producto es una respuesta. No se integra, no se mantiene, se tira cuando ha contestado.

**La pregunta** (riesgo R1): The Rite sirve 45 s de audio sacados de iTunes y Deezer. El rank `Nameless` son bandas de menos de 500 oyentes. Si esas bandas no tienen preview, **el tier más raro es insonorizable** y la mecánica de rareza entera se cae.

---

## v1 — 2026-07-10 · **INCONCLUYENTE**

Muestra: 226 artistas de MusicBrainz, obtenidos consultando `tag:"black metal"`, `tag:"death metal"`, `tag:"doom metal"`, `tag:"funeral doom"`, `tag:"sludge metal"`, `tag:"heavy metal"`, `tag:"thrash metal"`, `tag:"raw black metal"`.

Oscuridad medida por número de release-groups en MusicBrainz — deliberadamente **independiente** de las plataformas de streaming (usar los fans de Deezer habría sido circular).

```
bucket (nº releases en MB)   n     iTunes  Deezer  alguno
A: 15+ releases            140      74%     86%     94%
B: 7-14                     18      67%     28%     83%
C: 3-6                      16      69%     12%     69%
D: 1-2                       4       0%      0%      0%
desconocido                 48      58%     58%     73%
-----------------------------------------------------------
TOTAL                      226      69%     69%     85%
```

### Por qué no vale

**El muestreo estaba sesgado y el sesgo era estructural, no accidental.** Muestrear MusicBrainz *por tag* devuelve bandas que alguien se molestó en etiquetar — y nadie etiqueta al sludge finlandés de 300 oyentes. Resultado: **140 de 226 tienen quince o más lanzamientos**. Es un censo de bandas conocidas. Salieron Slayer, KISS, Motörhead. Salió Toto.

**El único cubo que se parece a una banda oscura (D, 1–2 lanzamientos) tiene n=4 y da 0 % de cobertura.** No significa nada estadísticamente. Es el único dato que apunta hacia donde importa, y apunta mal.

**48 filas (21 %) quedaron sin dato** de recuento de lanzamientos: MusicBrainz devolvió errores, probablemente rate-limit.

**El emparejado por nombre coló ruido**: el «Death» con 2 457 fans de Deezer no es el de Chuck Schuldiner.

### Dos cifras que no deben citarse nunca

- **85 % de cobertura global** — es el promedio de una muestra sesgada hacia lo popular.
- **82 % de las bandas con menos de 500 fans de Deezer tienen preview** — es **circular**: para tener contador de fans hay que *estar* en Deezer. Las bandas que ni siquiera están indexadas allí, que son las de peor cobertura, quedan fuera del cálculo por construcción.

---

## v2 — 2026-07-10 · **CONTESTADO**

Muestra: **182 artistas** publicados por sellos underground (Nuclear War Now! Productions, Iron Bonehead Productions, Fallen Empire Records). Se enumeran por sus lanzamientos en MusicBrainz, no por tags — así el muestreo no premia a quien alguien se molestó en etiquetar.

```
Q_A — ¿pueden sonar?              Q_B — ¿su embedding es ruido?  (n=181)
  iTunes             41%            cero tags                  17%
  Deezer             19%            sin wikidata               72%
  alguno             52%            cero tags Y sin wikidata   16%   <- el agujero
  NINGUNO            48%            sin texto pero con audio    7%   <- lo que el audio rescata
```

### Lo que decide

**R1 contestado**: el 52 % del underground puede sonar. El pool de The Rite se filtra por `preview IS NOT NULL`, y casi la mitad de las bandas oscuras existen en la app pero no se pueden invocar. Encaja con la tesis: las bandas verdaderamente innombrables son inaudibles.

**C19 (eje del timbre) degradado**: el audio rescataría al 7 %. No justifica descargar y analizar audio. Sobrevive como feature tardía y casi gratis, porque el preview ya se descarga para servir The Rite.

**Corrección de un error**: se había escrito que Deezer resolvía el catálogo en ~8 h y sería la fuente masiva. **iTunes cubre el doble que Deezer** (41 % vs 19 %, solape de 8 puntos). El límite de 20 req/min de iTunes vuelve a mandar. La cifra de «8 horas» no debe volver a citarse. Ver D25.

### Cavéats

- **52 % es cota inferior.** El emparejado va por nombre exacto: diacríticas y nombres estilizados convierten fallos de emparejado en falsos «sin preview».
- **No se midió la correlación entre cobertura y oscuridad *dentro* del underground**, porque sin key de Last.fm no hay `listeners`.
- De seis sellos buscados, **tres no aparecen en MusicBrainz** con el nombre dado (Amor Fati, Hells Headbangers, Sepulchral Voice). Si no es un problema de nomenclatura, el catálogo de sellos de MB también adelgaza en el underground, y C11 (escenas) y C13 (filtro por sello) heredan el problema. **Sin verificar.**

### Lo que abrió

El 72 % no tiene Wikidata, luego su embedding se construye solo con tags. Eso no es ruido, es **señal de baja dimensión**. Si muchas bandas comparten un puñado de tags, sus vectores colapsan y la búsqueda en anillo degenera justo donde vive la app. → spike v3, `embedding-collapse.md`.

---
