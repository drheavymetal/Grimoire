# Spikes

Un *spike* es un experimento acotado cuyo único producto es **una respuesta**. No se integra, no se testea, no se mantiene. Se guarda aquí porque las preguntas vuelven.

Ninguno necesita API keys. Todos respetan los límites de MusicBrainz (1 req/s, User-Agent identificable) y de iTunes (~20 req/min). Los resultados y su lectura están en `docs/spikes/`.

| Script | Pregunta | Respuesta | Registro |
|---|---|---|---|
| `01-preview-coverage-by-tag.py` | ¿Tienen audio las bandas oscuras? | **Inconcluyente.** Muestrear MusicBrainz por *tag* devuelve bandas que alguien se molestó en etiquetar: 140 de 226 tenían 15+ lanzamientos. Un censo de bandas conocidas | D22 |
| `02-preview-coverage-by-label.py` | Lo mismo, muestreando por **sellos underground** | **52 %** puede sonar. iTunes 41 %, Deezer 19 % — al revés de lo que se había escrito. Y el agujero de texto es del 16 %, no del 60 % | D25 |
| `03-embedding-collapse.py` | ¿Colapsan los vectores del underground? | **No.** Pero el espacio es una cáscara fina: el anillo `[0.15, 0.35]` contiene 177 de 181 vecinos | D26 |
| `04-centering-and-text-variants.py` | ¿Cómo se arregla? | **Centrar el corpus** (×3.2 de separación). Reducir el texto a tags da ×11.6 pero reintroduce colisiones (`p05 = 0.005`) | D26 |

## Volver a correrlos

`03` y `04` se hicieron con **307 artistas**. La conclusión de D26 —que el anillo debe definirse en percentiles y no en radios— **depende del tamaño del corpus**. Cuando el catálogo crezca a decenas de miles, hay que repetirlos: la cáscara puede ensancharse, o no.

`04` necesita Ollama con `nomic-embed-text` y la caché de MusicBrainz que deja `03` en el scratchpad. `03` la reconstruye solo si no está.

## Defectos conocidos, declarados

- `03` metía el nombre de la banda en el texto, así que «textos únicos 182/182» es una métrica vacía: eran únicos por construcción. La conclusión se sostiene por la distribución de distancias, no por esa fila.
- `01` y `02` emparejan artistas con iTunes y Deezer **por nombre exacto**. Las diacríticas y los nombres estilizados producen falsos «sin preview», así que las coberturas son **cotas inferiores**.
- `01` coló ruido: salió «Toto», y el «Death» con más fans en Deezer no es el de Chuck Schuldiner.
