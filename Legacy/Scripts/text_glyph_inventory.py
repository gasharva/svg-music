#!/usr/bin/env python3
import argparse
import hashlib
import html
import json
import math
from collections import defaultdict
from pathlib import Path


def bbox(contours):
    pts = [p for c in contours for p in c]
    if not pts:
        return None
    xs = [p["X"] for p in pts]
    ys = [p["Y"] for p in pts]
    return min(xs), min(ys), max(xs), max(ys)


def canonical_contour(points, min_x, min_y, scale):
    if not points:
        return ()
    q = []
    for p in points:
        x = round((p["X"] - min_x) / scale * 128)
        y = round((p["Y"] - min_y) / scale * 128)
        q.append((x, y))
    if len(q) > 1 and q[0] == q[-1]:
        q.pop()
    if not q:
        return ()

    # Closed outlines may start at any vertex. Canonicalize cyclic rotation and direction.
    candidates = []
    for seq in (q, list(reversed(q))):
        minimum = min(range(len(seq)), key=lambda i: seq[i:]+seq[:i])
        candidates.append(tuple(seq[minimum:] + seq[:minimum]))
    return min(candidates)


def shape_signature(geometry):
    contours = geometry.get("Contours") or []
    box = bbox(contours)
    if not box:
        return "empty"
    min_x, min_y, max_x, max_y = box
    scale = max(max_x - min_x, max_y - min_y, 1e-6)
    normalized = [canonical_contour(c, min_x, min_y, scale) for c in contours]
    normalized = tuple(sorted(c for c in normalized if c))
    return hashlib.sha1(repr(normalized).encode("utf-8")).hexdigest()[:16]


def nearest_staff_space(x, y, staves):
    if not staves:
        return 1.0
    best = min(staves, key=lambda s: abs(y - sum(s["Lines"]) / max(len(s["Lines"]), 1)))
    lines = best["Lines"]
    if len(lines) < 2:
        return 1.0
    return sum(b-a for a, b in zip(lines, lines[1:])) / (len(lines)-1)


def classification_map(items):
    result = {}
    for c in items:
        sid = c.get("SymbolId")
        if sid:
            result[sid] = c
    return result


def musicish(cls):
    if not cls:
        return False
    text = " ".join(str(cls.get(k, "")) for k in ("Kind", "ReferenceId", "MusicXmlValue")).lower()
    tokens = (
        "notehead", "clef", "accidental", "rest", "timesig", "flag", "augmentation-dot",
        "dynamic", "beam", "barline", "fermata", "articulation", "pedal"
    )
    return any(t in text for t in tokens)


def points_to_svg(contours, width=150, height=100, pad=8):
    box = bbox(contours)
    if not box:
        return ""
    min_x, min_y, max_x, max_y = box
    w = max(max_x-min_x, 1e-6)
    h = max(max_y-min_y, 1e-6)
    scale = min((width-2*pad)/w, (height-2*pad)/h)
    dx = (width - w*scale)/2 - min_x*scale
    dy = (height - h*scale)/2 - min_y*scale
    pieces = []
    for contour in contours:
        if not contour:
            continue
        coords = [(p["X"]*scale+dx, p["Y"]*scale+dy) for p in contour]
        d = [f"M {coords[0][0]:.2f} {coords[0][1]:.2f}"]
        d += [f"L {x:.2f} {y:.2f}" for x, y in coords[1:]]
        if len(coords) > 2:
            d.append("Z")
        pieces.append(f'<path d="{" ".join(d)}" fill="currentColor" fill-rule="evenodd"/>')
    return f'<svg viewBox="0 0 {width} {height}" width="{width}" height="{height}" aria-hidden="true">{"".join(pieces)}</svg>'


def candidate_score(family):
    # Only a ranking hint. Nothing is discarded: every painted family stays in the report.
    score = 0
    if not family["musicish"]:
        score += 5
    if family["height_sp"] <= 5.0:
        score += 2
    if family["width_sp"] <= 8.0:
        score += 1
    if family["count"] > 1:
        score += 1
    if family["source_kinds"] == {"path"}:
        score += 1
    return score


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("analysis")
    ap.add_argument("classification")
    ap.add_argument("output_html")
    ap.add_argument("--json", dest="output_json")
    args = ap.parse_args()

    analysis = json.loads(Path(args.analysis).read_text(encoding="utf-8-sig"))
    classifications = json.loads(Path(args.classification).read_text(encoding="utf-8-sig"))
    classes = classification_map(classifications.get("Symbols", classifications) if isinstance(classifications, dict) else classifications)
    staves = analysis.get("Staves", [])

    families = {}
    instances_by_sig = defaultdict(list)
    for item in analysis.get("PageGeometry", []):
        geometry = item.get("Geometry") or {}
        contours = geometry.get("Contours") or []
        box = bbox(contours)
        if not box:
            continue
        sig = shape_signature(geometry)
        min_x, min_y, max_x, max_y = box
        space = nearest_staff_space(item.get("X", (min_x+max_x)/2), item.get("Y", (min_y+max_y)/2), staves)
        cls = classes.get(item.get("SourceSymbolId"))
        instances_by_sig[sig].append({
            "instance_id": item.get("InstanceId"),
            "source_symbol_id": item.get("SourceSymbolId"),
            "source_kind": item.get("SourceKind"),
            "x": item.get("X"), "y": item.get("Y"),
            "width": max_x-min_x, "height": max_y-min_y,
            "width_sp": (max_x-min_x)/max(space, 1e-6),
            "height_sp": (max_y-min_y)/max(space, 1e-6),
            "classification": cls,
        })
        if sig not in families:
            families[sig] = {
                "signature": sig,
                "geometry": contours,
                "width_sp": (max_x-min_x)/max(space, 1e-6),
                "height_sp": (max_y-min_y)/max(space, 1e-6),
            }

    rows = []
    for sig, fam in families.items():
        inst = instances_by_sig[sig]
        symbol_ids = sorted({x["source_symbol_id"] for x in inst if x["source_symbol_id"]})
        source_kinds = {x["source_kind"] for x in inst if x["source_kind"]}
        labels = []
        is_music = False
        for x in inst:
            cls = x["classification"]
            if cls:
                label = cls.get("Kind") or cls.get("ReferenceId") or cls.get("MusicXmlValue")
                if label:
                    labels.append(str(label))
                is_music = is_music or musicish(cls)
        fam.update({
            "count": len(inst),
            "symbol_ids": symbol_ids,
            "source_kinds": source_kinds,
            "labels": sorted(set(labels)),
            "musicish": is_music,
            "instances": inst,
        })
        fam["score"] = candidate_score(fam)
        rows.append(fam)

    rows.sort(key=lambda r: (-r["score"], r["musicish"], -r["count"], r["signature"]))

    if args.output_json:
        serializable = []
        for r in rows:
            copy = dict(r)
            copy.pop("geometry", None)
            copy["source_kinds"] = sorted(copy["source_kinds"])
            serializable.append(copy)
        Path(args.output_json).write_text(json.dumps({
            "page_geometry_instances": sum(len(v) for v in instances_by_sig.values()),
            "shape_families": len(rows),
            "families": serializable,
        }, ensure_ascii=False, indent=2), encoding="utf-8")

    cards = []
    for r in rows:
        ids = ", ".join(r["symbol_ids"]) or "—"
        labels = ", ".join(r["labels"]) or "unknown"
        kinds = ", ".join(sorted(r["source_kinds"])) or "—"
        sample_instances = r["instances"][:8]
        locations = "; ".join(f'{x["instance_id"]}@({x["x"]:.1f},{x["y"]:.1f})' for x in sample_instances if x["x"] is not None and x["y"] is not None)
        extra = "" if len(r["instances"]) <= 8 else f"; +{len(r['instances'])-8} more"
        cards.append(f'''
        <article class="card" data-music="{str(r['musicish']).lower()}" data-source="{html.escape(kinds)}" data-count="{r['count']}">
          <div class="preview">{points_to_svg(r['geometry'])}</div>
          <div class="meta">
            <div><b>shape</b> <code>{r['signature']}</code> · <b>instances</b> {r['count']}</div>
            <div><b>source</b> {html.escape(kinds)} · <b>symbol id</b> {html.escape(ids)}</div>
            <div><b>classified</b> {html.escape(labels)} · <b>music-like</b> {r['musicish']}</div>
            <div><b>size</b> {r['width_sp']:.2f} × {r['height_sp']:.2f} staff spaces</div>
            <details><summary>instances</summary><small>{html.escape(locations+extra)}</small></details>
          </div>
        </article>''')

    document = f'''<!doctype html>
<html><head><meta charset="utf-8"><title>Vector text glyph inventory</title>
<style>
body {{ font: 14px system-ui, sans-serif; margin: 24px; background:#fafafa; color:#222 }}
h1 {{ margin-bottom:6px }} .hint {{ max-width:980px; line-height:1.45 }}
.controls {{ position:sticky; top:0; padding:10px 0; background:#fafafaeF; z-index:2; display:flex; gap:14px; align-items:center; flex-wrap:wrap }}
.grid {{ display:grid; grid-template-columns:repeat(auto-fill,minmax(330px,1fr)); gap:12px }}
.card {{ display:flex; gap:12px; background:white; border:1px solid #ddd; border-radius:8px; padding:10px; min-height:120px }}
.preview {{ width:150px; min-width:150px; height:100px; display:flex; align-items:center; justify-content:center; color:#111; border:1px solid #eee; background:#fff }}
.meta {{ min-width:0; line-height:1.45 }} code {{ font-size:12px }} small {{ overflow-wrap:anywhere }}
.hidden {{ display:none }}
</style></head><body>
<h1>Vector geometry / text-glyph inventory</h1>
<p class="hint">Every instantiated painted path from <code>PageGeometry</code> is included — both reusable <code>&lt;use&gt;</code> geometry and standalone <code>&lt;path&gt;</code> geometry. Repetition is used only to cluster identical shapes for presentation; singleton shapes are never discarded. This intentionally lets us inspect exporters that outline every letter separately without symbols/reuse.</p>
<p><b>{sum(len(v) for v in instances_by_sig.values())}</b> painted instances grouped into <b>{len(rows)}</b> normalized shape families.</p>
<div class="controls">
<label><input id="hideMusic" type="checkbox" checked> hide already-classified music shapes</label>
<label><input id="singletons" type="checkbox"> only singleton shapes</label>
<label>source <select id="source"><option value="">all</option><option value="use">use</option><option value="path">path</option></select></label>
</div>
<div id="grid" class="grid">{''.join(cards)}</div>
<script>
function apply() {{
  const hideMusic=document.querySelector('#hideMusic').checked;
  const singleton=document.querySelector('#singletons').checked;
  const source=document.querySelector('#source').value;
  for (const c of document.querySelectorAll('.card')) {{
    const hidden=(hideMusic && c.dataset.music==='true') || (singleton && c.dataset.count!=='1') || (source && !c.dataset.source.split(', ').includes(source));
    c.classList.toggle('hidden', hidden);
  }}
}}
for (const id of ['hideMusic','singletons','source']) document.querySelector('#'+id).addEventListener('change',apply);
apply();
</script></body></html>'''
    Path(args.output_html).write_text(document, encoding="utf-8")


if __name__ == "__main__":
    main()
