#!/usr/bin/env python3
"""
Grimoire spike v3 — ¿colapsa el espacio de embeddings en el underground?

El spike v2 dijo: 72% de las bandas underground no tienen Wikidata (luego no
tienen abstract). Su embedding se construye SOLO con tags.

Eso no es ruido. Es señal de baja dimensión. Y si muchas bandas comparten el
mismo puñado de tags, sus vectores colapsan al mismo punto y la búsqueda en
anillo (D4, todo el motor) degenera: todo cae a la misma distancia y el slider
Comfort <-> Abyss no mueve nada.

Mide tres cosas, contra un grupo de control de bandas bien documentadas:

  1. TEXTOS IDÉNTICOS  — bandas cuyo texto de embedding es literalmente el mismo.
                          La métrica asesina. Si es alta, no hay motor.
  2. DISTANCIA ENTRE PARES — distribución de la distancia coseno.
                          Si se estrecha, el anillo no discrimina.
  3. VIABILIDAD DEL ANILLO — para cada banda, cuántas caen dentro de un anillo
                          típico [0.15, 0.35]. Si es todo o nada, no sirve.

Sin descargar audio. Ollama local + MusicBrainz (1 req/s).
"""
import json, math, time, urllib.request, urllib.error
from collections import Counter

BASE = "/tmp/claude-1000/-home-drheavymetal-projects/f55c26d5-70bb-431a-aa16-dd3ce7b7f3fd/scratchpad"
UA = "Grimoire-Spike/0.3 ( pmanso@go2chain.es )"
OLLAMA = "http://localhost:11434/api/embeddings"
MODEL = "nomic-embed-text"
CONTROL_N = 100

RING_MIN, RING_MAX = 0.15, 0.35


import os
CACHE = f"{BASE}/mb_cache"
os.makedirs(CACHE, exist_ok=True)


def mb_artist(mbid):
    cached = f"{CACHE}/{mbid}.json"
    if os.path.exists(cached):
        try:
            return json.load(open(cached))
        except Exception:
            pass
    url = f"https://musicbrainz.org/ws/2/artist/{mbid}?inc=tags&fmt=json"
    for attempt in range(3):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=25) as r:
                d = json.load(r)
            json.dump(d, open(cached, "w"))
            time.sleep(1.1)
            return d
        except urllib.error.HTTPError as e:
            if e.code in (429, 503):
                time.sleep(5 * (attempt + 1))
                continue
            time.sleep(1.1)
            return None
        except Exception:
            time.sleep(1.1)
            return None
    return None


def embed_text(artist):
    """El texto que producción construiría para esta banda.
    tags de MB + país + tipo. Sin abstract, porque el 72% no lo tiene."""
    tags = sorted(t["name"] for t in (artist.get("tags") or []) if t.get("count", 0) > 0)
    country = artist.get("country") or (artist.get("area") or {}).get("name") or "unknown"
    kind = artist.get("type") or "unknown"
    tag_str = ", ".join(tags) if tags else "no tags"
    return f"search_document: {artist['name']}. {kind} from {country}. Genres: {tag_str}."


def embed(text):
    body = json.dumps({"model": MODEL, "prompt": text}).encode()
    req = urllib.request.Request(OLLAMA, data=body,
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)["embedding"]


def cosine_dist(a, b):
    dot = sum(x * y for x, y in zip(a, b))
    na = math.sqrt(sum(x * x for x in a))
    nb = math.sqrt(sum(y * y for y in b))
    return 1.0 - dot / (na * nb)


def pctl(sorted_vals, p):
    if not sorted_vals:
        return float("nan")
    i = min(len(sorted_vals) - 1, int(p * len(sorted_vals)))
    return sorted_vals[i]


def analyse(label, rows):
    print(f"\n{'=' * 64}")
    print(f"{label}  (n={len(rows)})")
    print("=" * 64)

    texts = [r["text"] for r in rows]
    dupes = Counter(texts)
    n_dup_groups = sum(1 for t, c in dupes.items() if c > 1)
    n_in_dupes = sum(c for c in dupes.values() if c > 1)
    print(f"  textos únicos            {len(dupes):>4} / {len(rows)}")
    print(f"  bandas con texto repetido{n_in_dupes:>4}  ({n_in_dupes/len(rows):.0%})"
          f"  en {n_dup_groups} grupos")
    if n_dup_groups:
        worst = dupes.most_common(3)
        print("  peores colisiones:")
        for t, c in worst:
            if c > 1:
                short = t.replace("search_document: ", "")
                short = short[:76] + ("…" if len(short) > 76 else "")
                print(f"    {c:>3}x  {short}")

    embs = [r["emb"] for r in rows]
    dists = []
    for i in range(len(embs)):
        for j in range(i + 1, len(embs)):
            dists.append(cosine_dist(embs[i], embs[j]))
    dists.sort()

    print(f"\n  distancia coseno entre pares ({len(dists)} pares)")
    print(f"    p05 {pctl(dists,0.05):.4f}   p25 {pctl(dists,0.25):.4f}   "
          f"p50 {pctl(dists,0.50):.4f}   p75 {pctl(dists,0.75):.4f}   "
          f"p95 {pctl(dists,0.95):.4f}")
    near = sum(1 for d in dists if d < 0.02)
    print(f"    pares casi idénticos (d<0.02): {near} ({near/len(dists):.1%})")
    spread = pctl(dists, 0.95) - pctl(dists, 0.05)
    print(f"    RANGO p05..p95 = {spread:.4f}   <- si es pequeño, el anillo no discrimina")

    ring_counts = []
    for i in range(len(embs)):
        c = sum(1 for j in range(len(embs))
                if i != j and RING_MIN <= cosine_dist(embs[i], embs[j]) <= RING_MAX)
        ring_counts.append(c)
    ring_counts.sort()
    print(f"\n  vecinos dentro del anillo [{RING_MIN}, {RING_MAX}]")
    print(f"    p10 {pctl(ring_counts,0.10):>4}   mediana {pctl(ring_counts,0.5):>4}   "
          f"p90 {pctl(ring_counts,0.90):>4}   (de {len(rows)-1} posibles)")
    empty = sum(1 for c in ring_counts if c == 0)
    print(f"    bandas con anillo VACÍO: {empty} ({empty/len(rows):.0%})"
          f"   <- The Rite no tendría qué servirles")

    return {"unique_texts": len(dupes), "n": len(rows), "spread": spread,
            "empty_ring": empty, "median_ring": pctl(ring_counts, 0.5)}


def load(path, key_filter=None, limit=None):
    rows = json.load(open(path))
    if key_filter:
        rows = [r for r in rows if key_filter(r)]
    return rows[:limit] if limit else rows


def build(rows, label):
    out = []
    print(f"\n-- {label}: pidiendo tags a MusicBrainz y embebiendo --", flush=True)
    for i, r in enumerate(rows, 1):
        a = mb_artist(r["mbid"])
        if not a:
            continue
        text = embed_text(a)
        try:
            e = embed(text)
        except Exception as ex:
            print(f"  ! ollama falló en {r['name']}: {ex}", flush=True)
            continue
        out.append({"name": r["name"], "text": text, "emb": e})
        if i % 25 == 0:
            print(f"  {i}/{len(rows)}", flush=True)
    return out


def main():
    print("Comprobando Ollama...", flush=True)
    v = embed("search_document: test")
    print(f"  ok, {len(v)} dimensiones\n", flush=True)

    under_src = load(f"{BASE}/spike2_results.json")
    ctrl_src = load(f"{BASE}/spike_results.json",
                    key_filter=lambda r: (r.get("releases") or 0) >= 15,
                    limit=CONTROL_N)

    under = build(under_src, f"UNDERGROUND ({len(under_src)})")
    ctrl = build(ctrl_src, f"CONTROL, bandas documentadas ({len(ctrl_src)})")

    a = analyse("UNDERGROUND — sellos oscuros, mayoría sin Wikidata", under)
    b = analyse("CONTROL — bandas con 15+ lanzamientos", ctrl)

    print(f"\n{'=' * 64}")
    print("VEREDICTO")
    print("=" * 64)
    print(f"  textos únicos:   underground {a['unique_texts']}/{a['n']}"
          f"   ·   control {b['unique_texts']}/{b['n']}")
    print(f"  rango p05..p95:  underground {a['spread']:.4f}"
          f"   ·   control {b['spread']:.4f}")
    print(f"  anillo vacío:    underground {a['empty_ring']}/{a['n']}"
          f"   ·   control {b['empty_ring']}/{b['n']}")
    print(f"  vecinos medianos en anillo: underground {a['median_ring']}"
          f"   ·   control {b['median_ring']}")
    print("\n  Si el underground tiene muchos textos repetidos, rango estrecho")
    print("  o anillos vacíos, la búsqueda en anillo (D4) degenera justo donde")
    print("  vive la app, y hace falta otra señal.")

    json.dump({"underground": a, "control": b}, open(f"{BASE}/spike3_results.json", "w"), indent=1)


if __name__ == "__main__":
    main()
