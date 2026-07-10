#!/usr/bin/env python3
"""
Grimoire spike v2 — dos preguntas, un muestreo honesto.

El spike v1 muestreó MusicBrainz por TAG, y las bandas oscuras no tienen tags.
Resultado: 140/226 con 15+ lanzamientos. Un censo de bandas conocidas.

v2 muestrea por SELLO UNDERGROUND, que es donde vive lo oscuro de verdad.

  Q_A  ¿tienen preview de audio (iTunes/Deezer)?      -> viabilidad de The Rite en ranks raros
  Q_B  ¿tienen tags y/o abstract (señal de texto)?    -> ¿es ruido su embedding? ¿hace falta audio?

Sin API keys. MusicBrainz 1 req/s, iTunes 1 req/3.5s, Deezer 1 req/0.2s.
"""
import json, time, urllib.parse, urllib.request, urllib.error
from collections import defaultdict

UA = "Grimoire-Spike/0.2 ( pmanso@go2chain.es )"
BASE = "/tmp/claude-1000/-home-drheavymetal-projects/f55c26d5-70bb-431a-aa16-dd3ce7b7f3fd/scratchpad"
OUT = f"{BASE}/spike2_results.json"

LABELS = [
    "Nuclear War Now! Productions",
    "Iron Bonehead Productions",
    "Amor Fati Productions",
    "Fallen Empire Records",
    "Hells Headbangers Records",
    "Sepulchral Voice Records",
]
RELEASES_PER_LABEL = 100
MAX_ARTISTS = 200


def get(url, pause, method="GET", retries=3):
    for attempt in range(retries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=30) as r:
                data = json.load(r)
            time.sleep(pause)
            return data
        except urllib.error.HTTPError as e:
            if e.code in (429, 503):
                time.sleep(6 * (attempt + 1))
                continue
            time.sleep(pause)
            return None
        except Exception:
            time.sleep(pause)
            return None
    return None


def label_mbid(name):
    q = urllib.parse.quote(f'label:"{name}"')
    d = get(f"https://musicbrainz.org/ws/2/label?query={q}&limit=1&fmt=json", 1.1)
    if not d or not d.get("labels"):
        return None
    return d["labels"][0]["id"], d["labels"][0]["name"]


def artists_of_label(mbid):
    """Artistas acreditados en los releases de un sello."""
    out = {}
    url = (f"https://musicbrainz.org/ws/2/release?label={mbid}"
           f"&limit={RELEASES_PER_LABEL}&inc=artist-credits&fmt=json")
    d = get(url, 1.1)
    if not d:
        return out
    for rel in d.get("releases", []):
        for ac in rel.get("artist-credit", []):
            a = ac.get("artist")
            if a and a.get("id"):
                out[a["id"]] = a["name"]
    return out


def artist_signal(mbid):
    """-> (n_tags, tiene_wikidata, tiene_wikipedia)"""
    url = f"https://musicbrainz.org/ws/2/artist/{mbid}?inc=tags+url-rels&fmt=json"
    d = get(url, 1.1)
    if d is None:
        return None, None, None
    tags = len(d.get("tags") or [])
    wd = wp = False
    for rel in d.get("relations") or []:
        t = rel.get("type", "")
        target = (rel.get("url") or {}).get("resource", "")
        if t == "wikidata" or "wikidata.org" in target:
            wd = True
        if t == "wikipedia" or "wikipedia.org" in target:
            wp = True
    return tags, wd, wp


def itunes_preview(name):
    q = urllib.parse.quote(name)
    url = (f"https://itunes.apple.com/search?term={q}&entity=musicTrack"
           f"&attribute=artistTerm&limit=5")
    d = get(url, 3.5)
    if not d:
        return False
    low = name.strip().lower()
    return any(r.get("artistName", "").strip().lower() == low and r.get("previewUrl")
               for r in d.get("results", []))


def deezer_preview(name):
    q = urllib.parse.quote(name)
    d = get(f"https://api.deezer.com/search/artist?q={q}&limit=5", 0.25)
    if not d:
        return False, None
    low = name.strip().lower()
    art = next((a for a in d.get("data", []) if a.get("name", "").strip().lower() == low), None)
    if not art:
        return False, None
    top = get(f"https://api.deezer.com/artist/{art['id']}/top?limit=1", 0.25) or {}
    prev = any(t.get("preview") for t in top.get("data", []))
    return prev, art.get("nb_fan")


def pct(a, b):
    return f"{a/b:>6.0%}" if b else "     —"


def main():
    print("== 1. Sellos underground -> artistas ==", flush=True)
    artists = {}
    for name in LABELS:
        res = label_mbid(name)
        if not res:
            print(f"  ! sello no encontrado: {name}", flush=True)
            continue
        mbid, real = res
        found = artists_of_label(mbid)
        for k, v in found.items():
            artists.setdefault(k, v)
        print(f"  {real}: {len(found)} artistas (total {len(artists)})", flush=True)
        if len(artists) >= MAX_ARTISTS:
            break

    items = list(artists.items())[:MAX_ARTISTS]
    print(f"\n{len(items)} artistas underground únicos\n", flush=True)

    rows = []
    print("== 2. Señal de texto + previews ==", flush=True)
    for i, (mbid, name) in enumerate(items, 1):
        tags, wd, wp = artist_signal(mbid)
        it = itunes_preview(name)
        dz, fans = deezer_preview(name)
        row = {"mbid": mbid, "name": name, "tags": tags, "wikidata": wd,
               "wikipedia": wp, "itunes": it, "deezer": dz, "deezer_fans": fans}
        rows.append(row)
        audio = "♪" if (it or dz) else "·"
        text = "T" if (tags or 0) > 0 else "-"
        text += "W" if wd else "-"
        print(f"  [{i:3}/{len(items)}] {audio} {name[:32]:34} "
              f"tags={tags if tags is not None else '?':>2} {text}  "
              f"it={'Y' if it else '-'} dz={'Y' if dz else '-'}", flush=True)
        if i % 20 == 0:
            json.dump(rows, open(OUT, "w"), indent=1)

    json.dump(rows, open(OUT, "w"), indent=1)
    n = len(rows)
    ok = [r for r in rows if r["tags"] is not None]

    print("\n" + "=" * 62, flush=True)
    print("Q_A — ¿PUEDEN SONAR? (cobertura de preview, bandas underground)")
    print("=" * 62)
    ait = sum(r["itunes"] for r in rows)
    adz = sum(r["deezer"] for r in rows)
    aany = sum(r["itunes"] or r["deezer"] for r in rows)
    print(f"  n = {n}")
    print(f"  iTunes            {pct(ait, n)}")
    print(f"  Deezer            {pct(adz, n)}")
    print(f"  alguno de los dos {pct(aany, n)}   <- pool servible de The Rite")
    print(f"  NINGUNO           {pct(n - aany, n)}   <- insonorizables")

    print("\n" + "=" * 62, flush=True)
    print("Q_B — ¿SU EMBEDDING ES RUIDO? (señal de texto)")
    print("=" * 62)
    if ok:
        m = len(ok)
        no_tags = sum(1 for r in ok if r["tags"] == 0)
        no_wd = sum(1 for r in ok if not r["wikidata"])
        hole = sum(1 for r in ok if r["tags"] == 0 and not r["wikidata"])
        print(f"  n = {m} (con datos de MB)")
        print(f"  cero tags                    {pct(no_tags, m)}")
        print(f"  sin wikidata                 {pct(no_wd, m)}")
        print(f"  cero tags Y sin wikidata     {pct(hole, m)}   <- EL AGUJERO")
        print(f"\n  Si el agujero es grande, su vector de texto es ruido")
        print(f"  y el eje del timbre deja de ser un capricho.")

        both = [r for r in ok if (r["itunes"] or r["deezer"])
                and r["tags"] == 0 and not r["wikidata"]]
        print(f"\n  Sin texto PERO con audio: {len(both)} "
              f"({len(both)/m:.0%}) <- las que el audio rescataría")

    print(f"\nCrudo: {OUT}", flush=True)


if __name__ == "__main__":
    main()
