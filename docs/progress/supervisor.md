# Progreso — supervisor

Rol: garantía de calidad transversal. Convierte el mandato de Pedro («sin espacios sin implementar ni cosas solo estéticas») en verificación mecánica + checklist de revisor. No toca `src/`.

---

## [2026-07-10] Ola 0 — la máquina de auditoría

### Qué se construyó

- **`scripts/audit.sh`** — puerta de calidad ejecutable desde la raíz. 7 checks, salida `path:línea` por violación, exit ≠ 0 si hay violaciones. Modos: `--fast` (sin builds), `--strict` (un SKIPPED cuenta como fallo — el modo pre-commit del orquestador). Escape controlado `// audit-ok: <razón>` para los checks 1, 2, 5 y 6; **cada excepción en vigor se lista en cada ejecución** para que nunca se esconda. El check 4 (Invariante 6) no tiene escape: relajarlo exige entrada en `DECISIONS.md`.
- **`docs/REVIEW.md`** — el mandato como criterios comprobables, límites honestos del script, checklist de revisor específico de Grimoire y la distinción gap aceptable / inaceptable.

### Verificación hecha

- Probado contra un **repo de fixtures** con violaciones plantadas de los 7 checks: las 16 violaciones detectadas, las 2 excepciones `audit-ok` respetadas y listadas, y una página limpia cableada a `core/` **no** marcada (sin falso positivo).
- Probado el camino de fallo del gate 7 con un `.slnx` roto: imprime las últimas 40 líneas del build y falla.
- `-warnaserror` verificado como switch real de MSBuild (`dotnet msbuild -h`) antes de usarlo, no asumido.
- Detectado y corregido un bug propio: los contadores de violaciones se perdían en subshells de pipeline (sumario decía 1 cuando había 16). Arreglado con sustitución de proceso.
- El script prefiere GNU grep explícitamente: en esta máquina `grep` del PATH resuelve a ugrep.

### Salida real sobre el repo actual (`bash scripts/audit.sh --fast`)

```
Grimoire audit — root: /home/drheavymetal/projects/Grimoire
mode: --fast (no builds)

=== 1/7 Forbidden markers in src/ (TODO, FIXME, HACK, XXX, NotImplementedException, throw new NotSupportedException) ===
OK         no forbidden markers

=== 2/7 Empty/comment-only catch blocks in C# ===
OK         no swallowed exceptions

=== 3/7 console.log in src/front/src/ ===
SKIPPED    check 3: src/front/src/ does not exist yet

=== 4/7 Invariant 6: core/ is DOM-free and does not import ui/ or platform/ ===
SKIPPED    check 4: src/front/src/core/ does not exist yet

=== 5/7 Aesthetic-only components in src/front/src/ui/ (heuristic) ===
SKIPPED    check 5: src/front/src/ui/ does not exist yet

=== 6/7 Hardcoded UI strings bypassing i18next (heuristic) ===
SKIPPED    check 6: src/front/src/ does not exist yet

=== 7/7 Build and test gates ===
(--fast: build/test gates not run)

================ AUDIT SUMMARY ================
Violations        : 0
Skipped checks    : 4
audit-ok in force : 0  (listed above as AUDIT-OK — verify each reason still holds)
RESULT: PASS
EXIT=0
```

(En el momento de la ejecución el agente de esqueleto solo había aterrizado `src/shared/GrimoireLibrary/` — los checks 1-2 corren sobre esos `.cs` y están limpios.)

### Valoración honesta: qué es robusto y qué es heurístico

**Robustos** (falso positivo raro):
- Check 1 (marcadores), check 3 (`console.log`), check 7 (builds/tests: o compilan o no).
- Check 2 (catch vacíos): la regex no ve bloques con llaves anidadas, pero esos nunca son vacíos — el límite no genera falsos positivos, solo un falso negativo imposible por construcción.

**Robustos con roce**:
- Check 1: `\bXXX\b` puede saltar en un literal legítimo (improbable en este dominio); un comentario en `src/` que mencione «TODO» conversacionalmente salta — el código va en inglés y sin TODOs, así que el roce es aceptable.
- Check 4: `\bdocument\b` en un **comentario** de `core/` salta (falso positivo). Sin escape a propósito: el coste es reescribir el comentario, y prefiero eso a un agujero en un invariante. `navigator` también saltaría en un string cualquiera que contenga la palabra.

**Heurísticos declarados** (falsos positivos esperados, documentados en `REVIEW.md` §3):
- Check 5a: saltará con listas legítimamente estáticas (opciones de un select, nombres de movimientos) → se declaran con `audit-ok`. No verá mocks importados desde fuera de `ui/`, mocks en objetos no-array, ni componentes que llaman al hook y descartan el dato.
- Check 5b: saltará con páginas genuinamente estáticas (legal, about) → `audit-ok`. No verá una página que importa de `core/` sin usar lo importado.
- Check 6: solo ve una línea; texto JSX multilínea se escapa. Falsos positivos posibles en expresiones con `>` y `<` en la misma línea de JSX. No verifica que la clave exista en ambos catálogos es/en.

La conclusión que importa: **el script caza las formas baratas de hueco; la revisión de §4 de REVIEW.md caza las caras**. Ninguno de los dos solo basta.

### Recomendación al orquestador

Antes del primer commit de código de una ola: `scripts/audit.sh --strict` en verde + checklist §4 de `REVIEW.md` pasada por un revisor (los puntos de datos-reales y tests-que-muerden como mínimo). Mientras el esqueleto no tenga `src/web/Grimoire.slnx` y `src/front/`, usar el modo por defecto y tratar cada SKIPPED como deuda visible.

— supervisor
