# Grimoire — Dirección visual

> **Q1 y Q2 siguen sin respuesta de Pedro** (`docs/DECISIONS.md`, sección «Preguntas abiertas»). Todo lo que sigue es la dirección **propuesta** a partir de D14, no una decisión ratificada. Se expande aquí para poder discutirla con algo concreto delante, no para darla por cerrada.

---

## 1. La idea: generación de copia

El metal no nace del vinilo. Nace de la **cinta de demo** y del **flyer fotocopiado**. Una demo se graba en cuatro pistas, se copia en cinta a cinta hasta la enésima generación, y cada copia pierde algo — hiss, agudos, un semitono de velocidad. Un flyer se fotocopia sobre una fotocopia hasta que el tóner se come el contraste y el papel se ensucia.

Grimoire hace lo mismo con datos: busca en la **periferia**, donde la señal se degrada — bandas con tres tags, sin bio, sin portada, con un MBID y nada más. La app entera es un ejercicio de pérdida de generación. El lenguaje visual debería decir eso antes de que el usuario lea una palabra.

De ahí las dos piezas centrales: **dos artefactos reales** (no un tema y su variante oscura) y **una tipografía que se corroe con la rareza**, no como decoración sino como dato.

---

## 2. Modo claro y modo oscuro: dos objetos, no un interruptor

- **Modo claro = el flyer fotocopiado.** Papel sucio, grano de tóner, semitono. Textura, ruido, imperfección deliberada.
- **Modo oscuro = la cinta.** El vacío limpio. Sin grano, sin textura — la cinta no tiene ruido visual, tiene silencio.

La asimetría es **deliberada**: no son el mismo diseño con los colores invertidos. Cada modo es un objeto físico distinto con sus propias reglas. El semitono (halftone) existe **solo en modo claro** — el vacío no tiene grano, y forzarlo en oscuro rompería la metáfora de la cinta limpia.

Esto es una propuesta de tratamiento visual, condicionada a Q2: si Pedro prefiere un modo claro neutro y limpio para fichas largas (más legible, menos fatiga en sesiones de lectura), la textura de fotocopia se retira o se limita a superficies concretas (splash, reveal) en vez de aplicarse globalmente.

---

## 3. La firma: `Redaction` y la corrosión por rango

La pieza de identidad de la app es tipográfica, no cromática.

- **Display**: `Redaction` (SIL OFL), una familia con cortes de corrosión progresiva, de `10` (casi ilegible) a `100` (nítida).
- **El corte no lo elige el diseño, lo elige el rank de la banda.** Metallica (`Known`) se lee en un corte alto, casi perfecto. Una banda `Nameless` se renderiza en un corte bajo, al borde de la ilegibilidad.
- **La tipografía es el dato.** No hay una barra de progreso ni una etiqueta que diga «esto es oscuro»: el nombre mismo, tal como se lee, comunica cuánta gente lo conoce. Es coherente con el principio de D14: nada de decoración que no cargue información.

**Riesgo abierto, sin verificar**: no está confirmado que `Redaction` esté distribuida en npm/fontsource (`@fontsource/redaction` u opción equivalente). No se afirma que exista. Si no está disponible como paquete instalable, el plan de respaldo es usar `Archivo` también como display, perdiendo el efecto de corrosión progresiva hasta encontrar o construir una alternativa. Este hueco queda registrado aquí y debe cerrarse antes de comprometer la identidad visual a `Redaction` en la implementación.

### 3.1 El reveal de The Rite

El reveal no es un volteo de carta. Es un **revelado fotográfico**: el nombre de la banda empieza en `Redaction 100` (máxima corrosión, casi ilegible) y se resuelve progresivamente hasta el corte propio de esa banda (según su rank) a lo largo de **600 ms**, como una foto emergiendo en el líquido revelador.

Para una banda `Known`, el revelado llega casi a la nitidez total. Para una banda `Nameless`, el revelado **nunca termina de resolverse del todo** — se detiene en un corte bajo. La animación en sí misma comunica la rareza antes de que el usuario procese el nombre.

`prefers-reduced-motion`: la animación de 600 ms se desactiva y se muestra directamente el nombre en su corte final. Ver §5.

---

## 4. Roles tipográficos

| Rol | Tipo | Uso |
|---|---|---|
| Display | `Redaction` | Nombres de banda, títulos de sección, el reveal |
| Cuerpo | `Archivo` | Texto de lectura: bios, descripciones, UI general. Fallback de display si `Redaction` no está disponible (ver §3) |
| Utilidad | `Courier Prime` | Créditos, MBIDs, números de catálogo, años — la vernácula de una J-card de cassette mecanografiada |

La tercera fuente no es un capricho retro: una J-card real se escribía a máquina o con Letraset monoespaciado para los créditos. `Courier Prime` traslada esa convención a MBIDs y fechas sin que se lea como un adorno.

---

## 5. Color

Grimoire es **monocroma**. Un único acento: **azufre** `#D6C34A`.

- **Por qué azufre y no verde ácido**: el verde ácido es el color por defecto de casi cualquier app de metal — es el ruido visual que todo el mundo espera y nadie recuerda. El azufre es igual de «metal» en connotación (portadas, pirotecnia, química de escenario) sin ser el cliché.
- **Oxblood** (rojo oscuro, sangre seca) se reserva exclusivamente para `Banish`. Es la única otra nota de color con significado — nunca decorativa, siempre ligada al rechazo.
- Fuera de azufre y oxblood, la paleta es escala de grises (papel sucio en claro, vacío limpio en oscuro).

---

## 6. La ficha de artista no tiene héroe fotográfico

Restricción convertida en principio: Grimoire no tiene derechos sobre fotografías de bandas, y usar imágenes de terceros sin licencia no es una opción (coste operativo cero, D6, y honestidad hacia las fuentes).

**El Gantt (B7, Lineup Timeline) es el héroe de la ficha.** Ocupa el espacio que en cualquier otra app de música ocuparía una foto de cabecera. Es coherente con el resto de la dirección: la app no vende una imagen de la banda, vende su estructura en el tiempo.

---

## 7. Accesibilidad — suelo mínimo, no negociable

- **Foco de teclado visible en azufre.** Todo elemento interactivo tiene un estado de foco claramente distinguible, con el color de acento — nunca el `outline` por defecto del navegador, pero nunca invisible tampoco.
- **`prefers-reduced-motion` desactiva el reveal.** Con la preferencia activa, el nombre se muestra directamente resuelto a su corte final; no hay animación de 600 ms ni transición de corrosión.
- **Responsive a móvil.** No hay app nativa en v1 (D12), pero el diseño web se construye responsive desde el primer commit — no como un ajuste posterior.

---

## 8. Estado de la propuesta

Esta dirección se apoya en D14 y la desarrolla, pero **no sustituye la confirmación de Pedro sobre Q1 y Q2**. Hasta que se resuelvan:

- Q1 sin responder → la corrosión tipográfica por rango queda como firma propuesta, no implementada como compromiso de producto.
- Q2 sin responder → el tratamiento de fotocopia en modo claro es la propuesta por defecto, pero un modo claro neutro sigue sobre la mesa para fichas de lectura larga.

Cualquier implementación que dependa de esta dirección debe tratar `Redaction` como no verificado hasta confirmar su disponibilidad como paquete.
