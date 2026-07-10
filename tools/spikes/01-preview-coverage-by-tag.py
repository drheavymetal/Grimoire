#!/usr/bin/env python3
"""
Grimoire spike: ¿tienen preview de audio las bandas oscuras?

Mide cobertura de preview (iTunes + Deezer) estratificada por una medida de
oscuridad INDEPENDIENTE del streaming: nº de release-groups en MusicBrainz.

Sin API keys. Rate limits respetados:
  MusicBrainz 1 req/s   (User-Agent identificable, obligatorio)
  iTunes      1 req/3.5s (~17/min, bajo el límite blando de 20/min)
  Deezer      1 req/0.2s
"""
import json, time, sys, urllib.parse, urllib.request, urllib.error
from collections import defaultdict

UA = "Grimoire-Spike/0.1 ( pmanso@go2chain.es )"
OUT = "/tmp/claude-1000/-home-drheavymetal-projects/f55c26d5-70bb-431a-aa16-dd3ce7b7f3fd/scratchpad/spike_results.json"

TAGS = [
    "black metal", "death metal", "doom metal", "funeral doom",
    "sludge metal", "heavy metal", "thrash metal", "raw black metal",
]
PER_TAG = 30


def get(url, pause, retries=3):
    for attempt in range(retries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=25) as r:
                data = json.load(r)
            time.sleep(pause)
            return data
        except urllib.error.HTTPError as e:
            if e.code in (429, 503):
                time.sleep(5 * (attempt + 1))
                continue
            time.sleep(pause)
            return None
        except Exception:
            time.sleep(pause)
            return None
    return None


def mb_artists():
    """Artistas de MB por tag. Devuelve dict mbid -> {name, tag}."""
    found = {}
    for tag in TAGS:
        q = urllib.parse.quote(f'tag:"{tag}" AND type:group')
        url = f"https://musicbrainz.org/ws/2/artist?query={q}&limit={PER_TAG}&fmt=json"
        d = get(url, 1.1)
        if not d:
            print(f"  ! fallo tag {tag}", flush=True)
            continue
        for a in d.get("artists", []):
            found.setdefault(a["id"], {"name": a["name"], "tag": tag,
                                       "country": a.get("country")})
        print(f"  {tag}: {len(d.get('artists', []))}  (total {len(found)})", flush=True)
    return found


def mb_release_count(mbid):
    url = f"https://musicbrainz.org/ws/2/release-group?artist={mbid}&limit=1&fmt=json"
    d = get(url, 1.1)
    if d is None:
        return None
    return d.get("release-group-count", 0)


def itunes_preview(name):
    q = urllib.parse.quote(name)
    url = (f"https://itunes.apple.com/search?term={q}&entity=musicTrack"
           f"&attribute=artistTerm&limit=5")
    d = get(url, 3.5)
    if not d:
        return None
    low = name.strip().lower()
    for r in d.get("results", []):
        if r.get("artistName", "").strip().lower() == low and r.get("previewUrl"):
            return r["previewUrl"]
    return None


def deezer(name):
    """-> (nb_fan|None, preview|None). Match exacto de nombre."""
    q = urllib.parse.quote(name)
    d = get(f"https://api.deezer.com/search/artist?q={q}&limit=5", 0.25)
    if not d:
        return None, None
    low = name.strip().lower()
    art = next((a for a in d.get("data", [])
                if a.get("name", "").strip().lower() == low), None)
    if not art:
        return None, None
    info = get(f"https://api.deezer.com/artist/{art['id']}", 0.25) or {}
    top = get(f"https://api.deezer.com/artist/{art['id']}/top?limit=1", 0.25) or {}
    prev = None
    for t in top.get("data", []):
        if t.get("preview"):
            prev = t["preview"]
            break
    return info.get("nb_fan"), prev


def bucket(n):
    if n is None:
        return "desconocido"
    if n >= 15:
        return "A: 15+ releases"
    if n >= 7:
        return "B: 7-14"
    if n >= 3:
        return "C: 3-6"
    if n >= 1:
        return "D: 1-2"
    return "E: 0 releases"


def main():
    print("== 1. Artistas de MusicBrainz ==", flush=True)
    artists = mb_artists()
    print(f"\n{len(artists)} artistas únicos\n", flush=True)

    rows = []
    print("== 2. Releases + previews ==", flush=True)
    for i, (mbid, meta) in enumerate(artists.items(), 1):
        rc = mb_release_count(mbid)
        it = itunes_preview(meta["name"])
        fans, dz = deezer(meta["name"])
        row = {"mbid": mbid, "name": meta["name"], "tag": meta["tag"],
               "country": meta["country"], "releases": rc,
               "itunes": bool(it), "deezer": bool(dz), "deezer_fans": fans}
        rows.append(row)
        mark = "♪" if (it or dz) else "·"
        print(f"  [{i:3}/{len(artists)}] {mark} {meta['name'][:34]:36} "
              f"rg={rc if rc is not None else '?':>3}  "
              f"it={'Y' if it else '-'} dz={'Y' if dz else '-'} "
              f"fans={fans if fans is not None else '-'}", flush=True)
        if i % 20 == 0:
            json.dump(rows, open(OUT, "w"), indent=1)

    json.dump(rows, open(OUT, "w"), indent=1)

    print("\n== 3. COBERTURA POR OSCURIDAD (nº release-groups en MB) ==", flush=True)
    agg = defaultdict(lambda: {"n": 0, "it": 0, "dz": 0, "any": 0})
    for r in rows:
        b = agg[bucket(r["releases"])]
        b["n"] += 1
        b["it"] += r["itunes"]
        b["dz"] += r["deezer"]
        b["any"] += (r["itunes"] or r["deezer"])

    print(f"\n{'bucket':22} {'n':>4} {'iTunes':>8} {'Deezer':>8} {'alguno':>8}")
    print("-" * 54)
    for k in sorted(agg):
        v = agg[k]
        n = v["n"]
        print(f"{k:22} {n:>4} {v['it']/n:>7.0%} {v['dz']/n:>7.0%} {v['any']/n:>7.0%}")

    tot = len(rows)
    anyc = sum(r["itunes"] or r["deezer"] for r in rows)
    print("-" * 54)
    print(f"{'TOTAL':22} {tot:>4} "
          f"{sum(r['itunes'] for r in rows)/tot:>7.0%} "
          f"{sum(r['deezer'] for r in rows)/tot:>7.0%} "
          f"{anyc/tot:>7.0%}")

    fans = [r["deezer_fans"] for r in rows if r["deezer_fans"] is not None]
    if fans:
        fans.sort()
        print(f"\nDeezer nb_fan (n={len(fans)}): "
              f"min={fans[0]} p25={fans[len(fans)//4]} med={fans[len(fans)//2]} "
              f"p75={fans[3*len(fans)//4]} max={fans[-1]}")
        low = [r for r in rows if r["deezer_fans"] is not None and r["deezer_fans"] < 500]
        if low:
            cov = sum(r["itunes"] or r["deezer"] for r in low) / len(low)
            print(f"Artistas con <500 fans en Deezer: {len(low)}, con preview: {cov:.0%}")

    print(f"\nResultados crudos: {OUT}", flush=True)


if __name__ == "__main__":
    main()
