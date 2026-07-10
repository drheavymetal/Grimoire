#!/usr/bin/env python3
"""
Grimoire spike v3b — ¿se puede arreglar la cáscara fina?

v3 encontró: no hay colapso de vectores, pero TODAS las distancias caen entre
0.18 y 0.35. El anillo [0.15,0.35] contiene 177 de 181 vecinos. Los radios
absolutos no discriminan nada, luego el slider Comfort<->Abyss (D4) no movería
nada tal como está especificado.

Prueba cuatro variantes del texto/tratamiento y mide, para cada una, si el
anillo por PERCENTILES separa de verdad:

  A  nombre + plantilla + tags   (lo que hizo v3)
  B  solo tags                   (sin nombre: ¿el nombre mete ruido?)
  C  A, centrado                 (resta el vector medio: fix de anisotropía)
  D  B, centrado

Métrica que importa: si tomo el anillo entre el percentil 60 y el 80 de las
distancias a una banda, ¿cuántas bandas caen dentro y cuán distintas son de
las del percentil 0-20? Si el anillo por percentiles funciona, D4 se salva
cambiando radios por cuantiles.

Sin red salvo Ollama local: MusicBrainz viene de caché.
"""
import json, math, os, urllib.request
from statistics import mean

BASE = "/tmp/claude-1000/-home-drheavymetal-projects/f55c26d5-70bb-431a-aa16-dd3ce7b7f3fd/scratchpad"
CACHE = f"{BASE}/mb_cache"
OLLAMA = "http://localhost:11434/api/embeddings"
MODEL = "nomic-embed-text"


def embed(text):
    body = json.dumps({"model": MODEL, "prompt": text}).encode()
    req = urllib.request.Request(OLLAMA, data=body, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)["embedding"]


def load_cached(rows):
    out = []
    for r in rows:
        p = f"{CACHE}/{r['mbid']}.json"
        if os.path.exists(p):
            out.append(json.load(open(p)))
    return out


def text_a(a):
    tags = sorted(t["name"] for t in (a.get("tags") or []) if t.get("count", 0) > 0)
    country = a.get("country") or (a.get("area") or {}).get("name") or "unknown"
    kind = a.get("type") or "unknown"
    return f"search_document: {a['name']}. {kind} from {country}. Genres: {', '.join(tags) or 'no tags'}."


def text_b(a):
    tags = sorted(t["name"] for t in (a.get("tags") or []) if t.get("count", 0) > 0)
    return f"search_document: {', '.join(tags) or 'unknown genre'}"


def center(vs):
    n, d = len(vs), len(vs[0])
    mu = [sum(v[i] for v in vs) / n for i in range(d)]
    return [[v[i] - mu[i] for i in range(d)] for v in vs]


def cos_d(a, b):
    dot = sum(x * y for x, y in zip(a, b))
    na = math.sqrt(sum(x * x for x in a)) or 1e-9
    nb = math.sqrt(sum(y * y for y in b)) or 1e-9
    return 1.0 - dot / (na * nb)


def pctl(sv, p):
    return sv[min(len(sv) - 1, int(p * len(sv)))]


def report(label, embs, names):
    n = len(embs)
    all_d = []
    # anillo por percentiles: para cada banda, ordena vecinos y mide
    # cuán separadas están la banda del p10 y la del p70.
    sep = []
    ring_sizes = []
    for i in range(n):
        ds = sorted((cos_d(embs[i], embs[j]), j) for j in range(n) if j != i)
        all_d.extend(d for d, _ in ds)
        d10 = ds[int(0.10 * len(ds))][0]
        d70 = ds[int(0.70 * len(ds))][0]
        sep.append(d70 - d10)
        # ¿cuántos caen entre p60 y p80? (por construcción ~20%, sirve de sanity)
        lo, hi = ds[int(0.60 * len(ds))][0], ds[int(0.80 * len(ds))][0]
        ring_sizes.append(sum(1 for d, _ in ds if lo <= d <= hi))
    all_d.sort()
    spread = pctl(all_d, 0.95) - pctl(all_d, 0.05)
    print(f"{label:34} p05={pctl(all_d,0.05):.4f} p50={pctl(all_d,0.50):.4f} "
          f"p95={pctl(all_d,0.95):.4f} rango={spread:.4f}  "
          f"sep(p10→p70)={mean(sep):.4f}  anillo_p60_80≈{mean(ring_sizes):.0f}")
    return spread, mean(sep)


def main():
    under_src = json.load(open(f"{BASE}/spike2_results.json"))
    ctrl_src = [r for r in json.load(open(f"{BASE}/spike_results.json"))
                if (r.get("releases") or 0) >= 15][:100]

    for group, src in (("UNDERGROUND", under_src), ("CONTROL", ctrl_src)):
        arts = load_cached(src)
        if not arts:
            print(f"{group}: sin caché, saltando")
            continue
        names = [a["name"] for a in arts]
        print(f"\n{'=' * 100}\n{group}  (n={len(arts)})\n{'=' * 100}")

        ea = [embed(text_a(a)) for a in arts]
        eb = [embed(text_b(a)) for a in arts]

        print(f"{'variante':34} {'distribución de distancias':^46} {'utilidad del anillo'}")
        sa, _ = report("A  nombre+plantilla+tags", ea, names)
        sb, _ = report("B  solo tags", eb, names)
        sc, _ = report("C  A centrado", center(ea), names)
        sd, _ = report("D  B centrado", center(eb), names)

        best = max([("A", sa), ("B", sb), ("C", sc), ("D", sd)], key=lambda x: x[1])
        print(f"\n  mayor dispersión: {best[0]} (rango {best[1]:.4f})"
              f"   ·   A como referencia: {sa:.4f}"
              f"   ·   mejora ×{best[1]/sa:.2f}")

    print("\nLectura: 'rango' grande = las distancias se separan = los radios significan algo.")
    print("'sep(p10→p70)' = cuánta distancia real hay entre un vecino cercano y uno lejano")
    print("para la MISMA banda. Es lo que el slider Comfort<->Abyss tiene que recorrer.")


if __name__ == "__main__":
    main()
