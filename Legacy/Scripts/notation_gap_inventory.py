#!/usr/bin/env python3
import argparse
import collections
import json
import math
from pathlib import Path


def f(v):
    return float(v or 0)


def bbox(item):
    contours = ((item.get("Geometry") or {}).get("Contours") or [])
    pts = [p for c in contours for p in c]
    if not pts:
        return None
    xs = [f(p.get("X")) for p in pts]
    ys = [f(p.get("Y")) for p in pts]
    return min(xs), min(ys), max(xs), max(ys), sum(len(c) for c in contours), len(contours)


def area(contour):
    if len(contour) < 3:
        return 0.0
    total = 0.0
    for i, a in enumerate(contour):
        b = contour[(i + 1) % len(contour)]
        total += f(a.get("X")) * f(b.get("Y")) - f(b.get("X")) * f(a.get("Y"))
    return abs(total) / 2.0


def nearest_staff(x, y, staves):
    candidates = []
    for s in staves:
        sp = f(s.get("Space")) or 1.0
        if x < f(s.get("Left")) - 3 * sp or x > f(s.get("Right")) + 3 * sp:
            continue
        candidates.append((abs(y - f(s.get("Center"))) / sp, s))
    return min(candidates, default=(999, None), key=lambda z: z[0])[1]


def point_interval_distance(y, top, bottom):
    if top <= y <= bottom:
        return 0.0
    return min(abs(y - top), abs(y - bottom))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("analysis")
    ap.add_argument("classification")
    ap.add_argument("output")
    args = ap.parse_args()

    analysis = json.loads(Path(args.analysis).read_text(encoding="utf-8-sig"))
    classification = json.loads(Path(args.classification).read_text(encoding="utf-8-sig"))
    staves = analysis.get("Staves", [])
    page = analysis.get("PageGeometry", [])
    events = analysis.get("Events", [])
    lines = analysis.get("LineSegments", [])
    classes = {x.get("SymbolId"): x for x in classification.get("Symbols", [])}

    notes = [x for x in events if str(x.get("Kind", "")).startswith("notehead-")]
    black = [x for x in notes if x.get("Kind") == "notehead-black"]

    strict_beams = []
    relaxed_beams = []
    strict_arcs = []
    relaxed_arcs = []

    for item in page:
        b = bbox(item)
        if not b:
            continue
        l, t, r, bot, points, contours = b
        w, h = r - l, bot - t
        staff = nearest_staff((l + r) / 2, (t + bot) / 2, staves)
        if not staff:
            continue
        sp = f(staff.get("Space")) or 1.0
        aspect = w / max(h, sp * .03)

        if w >= sp * 1.4 and sp * .08 <= h <= sp * .95 and aspect >= 2.2 and points <= 14:
            strict_beams.append(item.get("InstanceId"))
        if w >= sp * 1.15 and sp * .04 <= h <= sp * 1.45 and aspect >= 1.5 and points <= 24:
            relaxed_beams.append((item.get("InstanceId"), w / sp, h / sp, points))

        if sp * 2.0 <= w <= sp * 18 and sp * .35 <= h <= sp * 2.6 and aspect >= 2.0 and points >= 16:
            strict_arcs.append(item.get("InstanceId"))
        if sp * 1.5 <= w <= sp * 22 and sp * .18 <= h <= sp * 4.0 and aspect >= 1.4 and points >= 8:
            relaxed_arcs.append((item.get("InstanceId"), w / sp, h / sp, points))

    # Candidate hollow heads: compact reusable shapes near staves with more than one contour.
    hollow = collections.defaultdict(lambda: {"uses": 0, "samples": [], "kind": "", "score": None})
    for item in page:
        sid = item.get("SourceSymbolId")
        if not sid or item.get("SourceKind") != "use":
            continue
        b = bbox(item)
        if not b:
            continue
        l, t, r, bot, points, contours = b
        x, y = (l + r) / 2, (t + bot) / 2
        staff = nearest_staff(x, y, staves)
        if not staff:
            continue
        sp = f(staff.get("Space")) or 1.0
        w, h = (r - l) / sp, (bot - t) / sp
        if not (.70 <= w <= 1.65 and .45 <= h <= 1.35):
            continue
        if contours < 2:
            continue
        cls = classes.get(sid) or {}
        row = hollow[sid]
        row["uses"] += 1
        row["kind"] = cls.get("Kind", "<unclassified>")
        row["score"] = cls.get("Score")
        if len(row["samples"]) < 3:
            row["samples"].append((round(x, 2), round(y, 2), round(w, 2), round(h, 2), contours))

    # Candidate flags: compact reusable shapes near a free stem end of unbeamed black notes.
    flag_candidates = collections.defaultdict(lambda: {"hits": 0, "kind": "", "reference": "", "samples": []})
    by_symbol_instances = collections.defaultdict(list)
    for item in page:
        if item.get("SourceKind") == "use" and item.get("SourceSymbolId"):
            by_symbol_instances[item.get("SourceSymbolId")].append(item)

    for note in black:
        if int(note.get("BeamCount") or 0) > 0 or note.get("StemX") is None:
            continue
        si = int(note.get("StaffIndex", -1))
        if si < 0 or si >= len(staves):
            continue
        staff = staves[si]
        sp = f(staff.get("Space")) or 1.0
        stemx = f(note.get("StemX"))
        nearby_lines = []
        for line in lines:
            cx = (f(line.get("X1")) + f(line.get("X2"))) / 2
            top = min(f(line.get("Y1")), f(line.get("Y2")))
            bot = max(f(line.get("Y1")), f(line.get("Y2")))
            height = bot - top
            if abs(cx - stemx) <= sp * .22 and sp * 1.1 <= height <= sp * 11:
                if top <= f(note.get("Y")) + sp * .9 and bot >= f(note.get("Y")) - sp * .9:
                    nearby_lines.append((abs(cx - stemx), top, bot))
        if not nearby_lines:
            continue
        _, top, bot = min(nearby_lines)
        free_y = top if note.get("StemDirection") == "up" else bot

        for sid, instances in by_symbol_instances.items():
            cls = classes.get(sid) or {}
            for item in instances:
                b = bbox(item)
                if not b:
                    continue
                l, t, r, bb, points, contours = b
                x, y = (l + r) / 2, (t + bb) / 2
                w, h = (r - l) / sp, (bb - t) / sp
                if not (.30 <= w <= 2.2 and .50 <= h <= 3.8):
                    continue
                dx = abs(x - stemx) / sp
                dy = abs(y - free_y) / sp
                if dx > 1.8 or dy > 2.6:
                    continue
                row = flag_candidates[sid]
                row["hits"] += 1
                row["kind"] = cls.get("Kind", "<unclassified>")
                row["reference"] = cls.get("ReferenceId", "")
                if len(row["samples"]) < 3:
                    row["samples"].append((round(dx, 2), round(dy, 2), round(w, 2), round(h, 2)))

    unattached = [n for n in notes if n.get("StemX") is None]

    out = ["# Notation gap inventory", ""]
    out += ["## Notehead sanity", "", f"Recognized noteheads: **{len(notes)}**", f"Unattached noteheads: **{len(unattached)}**", ""]
    for n in unattached:
        out.append(f"- `{n.get('SourceSymbolId')}` {n.get('Kind')} staff={n.get('StaffIndex')} at ({f(n.get('X')):.2f}, {f(n.get('Y')):.2f})")

    out += ["", "## Beam geometry", "", f"Strict current beam shapes: **{len(strict_beams)}**", f"Relaxed beam-like shapes: **{len(relaxed_beams)}**", "", "| path | width sp | height sp | points |", "|---|---:|---:|---:|"]
    for pid, w, h, points in relaxed_beams[:40]:
        out.append(f"| `{pid}` | {w:.2f} | {h:.2f} | {points} |")

    out += ["", "## Arc geometry", "", f"Strict current arc shapes: **{len(strict_arcs)}**", f"Relaxed arc-like shapes: **{len(relaxed_arcs)}**", "", "| path | width sp | height sp | points |", "|---|---:|---:|---:|"]
    for pid, w, h, points in relaxed_arcs[:40]:
        out.append(f"| `{pid}` | {w:.2f} | {h:.2f} | {points} |")

    out += ["", "## Hollow-head candidates", "", "Compact multi-contour reusable glyphs near staves; this is topology evidence, not yet classification.", "", "| symbol | uses | current kind | score | samples (x,y,w,h,contours) |", "|---|---:|---|---:|---|"]
    for sid, row in sorted(hollow.items(), key=lambda z: -z[1]["uses"]):
        score = "" if row["score"] is None else f"{float(row['score']):.3f}"
        out.append(f"| `{sid}` | {row['uses']} | {row['kind']} | {score} | `{row['samples']}` |")

    out += ["", "## Standalone-flag candidates", "", "Compact reusable glyphs repeatedly found near free ends of currently unbeamed stems.", "", "| symbol | hits | current kind | reference | samples (dx,dy,w,h sp) |", "|---|---:|---|---|---|"]
    for sid, row in sorted(flag_candidates.items(), key=lambda z: -z[1]["hits"])[:40]:
        out.append(f"| `{sid}` | {row['hits']} | {row['kind']} | {row['reference']} | `{row['samples']}` |")

    Path(args.output).write_text("\n".join(out) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
