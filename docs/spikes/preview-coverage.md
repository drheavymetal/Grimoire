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

## v2 — en curso

Corrige el muestreo: en lugar de tags, se enumeran los artistas publicados por **sellos underground concretos** (Nuclear War Now! Productions, Iron Bonehead Productions, Fallen Empire Records), que es donde vive lo oscuro de verdad. 182 artistas.

Contesta dos preguntas de una vez:

- **Q_A** — ¿tienen preview de audio? → viabilidad de The Rite en los ranks raros (R1).
- **Q_B** — ¿tienen tags y/o abstract de Wikidata? → si el agujero de texto es grande, **su embedding es ruido**, y el eje del timbre (C19, D19) deja de ser un capricho y pasa a ser necesario. Si es pequeño, C19 se tacha.

**Hallazgo lateral, ya visible**: de seis sellos underground buscados, **tres no existen en MusicBrainz con el nombre dado** (Amor Fati Productions, Hells Headbangers Records, Sepulchral Voice Records). Si se confirma que no están catalogados —y no que figuran con otro nombre—, el catálogo de sellos de MusicBrainz **también adelgaza en el underground**, y C11 (escenas) y C13 (filtro por sello) heredan el problema.

Resultados al terminar.
