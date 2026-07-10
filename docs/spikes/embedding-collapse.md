# Spike — ¿funciona la búsqueda en anillo donde vive la app?

**El origen** (riesgo R8, abierto por el spike v2): el 72 % de las bandas underground no tiene Wikidata, luego no tiene abstract. Su embedding se construye **solo con tags**.

Eso no es ruido. Es **señal de baja dimensión**, que es peor porque no se nota. Si cincuenta bandas noruegas tienen como única señal el tag `black metal`, sus vectores son casi idénticos, y la búsqueda en anillo —que es todo el motor (D4)— degenera justo en el underground.

---

## v3 — 2026-07-10 · el miedo era el equivocado

182 bandas underground (del spike v2) contra un control de 100 bandas con 15+ lanzamientos. Texto de embedding: `nombre. tipo from país. Genres: tags.` Modelo `nomic-embed-text` (768 dims) en Ollama local.

```
                        underground     control
textos únicos            182/182        100/100
pares casi idénticos       0.0%           0.0%
distancia p05..p95      0.204–0.348    0.180–0.281
anillos vacíos              0              0
vecinos en el anillo    177 de 181      99 de 99
```

**No hay colapso.** Cero vectores duplicados, cero anillos vacíos. El underground está incluso **más disperso** que las bandas conocidas (rango 0.144 frente a 0.100) — probablemente porque las famosas comparten los mismos cuatro tags canónicos mientras que las oscuras tienen etiquetas raras.

**Pero el espacio es una cáscara fina.** Fíjate en la última fila: la mediana de vecinos dentro del anillo `[0.15, 0.35]` es de **177 sobre 181**. El anillo contiene el catálogo entero.

Es una propiedad conocida de los embeddings de frases cortas y formulaicas: se apiñan en un cono estrecho. **El slider Comfort ↔ Abyss, tal como lo especificaba D4, no movería nada.**

---

## v3b — cómo se arregla, medido

Cuatro variantes del texto y del tratamiento. `sep(p10→p70)` es la métrica que importa: **la distancia real entre un vecino cercano y uno lejano de la misma banda**, es decir, lo que el slider tiene que recorrer.

### Underground (n=182)

```
variante                     p05      p50      p95     rango   sep(p10→p70)
A  nombre+plantilla+tags    0.204    0.268    0.348   0.1439      0.058
B  solo tags                0.005    0.197    0.387   0.3817      0.164
C  A centrado               0.827    1.013    1.154   0.3277      0.187
D  B centrado               0.061    1.030    1.502   1.4409      0.678
```

### Control (n=100)

```
A  nombre+plantilla+tags    0.180    0.233    0.281   0.1003      0.050
B  solo tags                0.082    0.191    0.294   0.2117      0.096
C  A centrado               0.844    1.018    1.147   0.3032      0.181
D  B centrado               0.563    1.049    1.314   0.7513      0.479
```

### Lectura

**Centrar** —restar el vector medio del corpus antes de indexar— multiplica la separación por **3.2** sin efectos secundarios. Tres líneas en el ETL. El vector medio se persiste y se aplica también al vector de consulta.

**Quitar el nombre y la plantilla** (D) da el número espectacular, ×11.6. Pero mira el `p05` de B y D: **0.005 y 0.061**. Ese es el colapso que se fue a buscar y no aparecía — y surge *solo* cuando el texto se reduce a los tags: dos bandas con el mismo conjunto de etiquetas tienen el mismo texto y el mismo vector. El 17 % sin tags acabaría entero en un punto.

**D cambia una cáscara fina por colisiones. Se rechaza.**

**Se adopta C.** Ver D26.

---

## Defectos declarados

- **«Textos únicos 182/182» es una métrica vacía.** El texto incluía el nombre de la banda, luego era único por construcción. La conclusión de «no hay colapso» se sostiene por la distribución de distancias y por los anillos, no por esa fila.
- **Se usaron tags de MusicBrainz, no de Last.fm.** Producción usará Last.fm, más rico. Esto es una cota **pesimista** sobre la señal de tags.
- **La columna `anillo_p60_80` del v3b no informa**: se pidió el 20 % central por construcción y sale el 20 %. Quedó como comprobación de cordura.
- **El v3 se cayó en el primer intento** por un bug del propio script: MusicBrainz devuelve `"area": null` en muchas bandas underground, y `.get("area", {})` devuelve `None`, no un diccionario vacío. Corregido, y las respuestas de MB se cachean a disco para que un reintento no vuelva a castigar a su servidor.
