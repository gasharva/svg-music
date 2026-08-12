#!/usr/bin/env python3
import argparse
import json
import math
from pathlib import Path


def f(v):
    return float(v or 0)


def box_of_geometry(item):
    contours = ((item.get("Geometry") or {}).get("Contours") or [])
    pts = [p for c in contours for p in c]
    if not pts:
        return None
    xs = [f(p.get("X")) for p in pts]
    ys = [f(p.get("Y")) for p in pts]
    return min(xs), min(ys), max(xs), max(ys)


def point_box_distance(x, y, box):
    l, t, r, b = box
    dx = 0 if l <= x <= r else min(abs(x-l), abs(x-r))
    dy = 0 if t <= y <= b else min(abs(y-t), abs(y-b))
    return math.hypot(dx, dy)


def vertical_edges(item, min_len):
    out = []
    contours = ((item.get("Geometry") or {}).get("Contours") or [])
    for ci, contour in enumerate(contours):
        for i in range(1, len(contour)):
            a, b = contour[i-1], contour[i]
            x1, y1 = f(a.get("X")), f(a.get("Y"))
            x2, y2 = f(b.get("X")), f(b.get("Y"))
            dy = abs(y2-y1)
            dx = abs(x2-x1)
            if dy < min_len or dx > max(.20, dy * .08):
                continue
            out.append((ci, (x1+x2)/2, min(y1,y2), max(y1,y2), dx, dy))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("analysis")
    ap.add_argument("output")
    args = ap.parse_args()
    data = json.loads(Path(args.analysis).read_text(encoding="utf-8-sig"))
    staves = {int(s.get("Index", -1)): s for s in data.get("Staves", [])}
    page = data.get("PageGeometry", [])
    notes = [e for e in data.get("Events", []) if str(e.get("Kind", "")).startswith("notehead-") and e.get("StemX") is None]

    # Focus on the first real-score system where the inventory showed many no-stem cases.
    targets = []
    wanted = [(144.48,170.62), (218.18,166.03), (257.97,168.33), (322.86,166.03)]
    for wx, wy in wanted:
        if not notes:
            continue
        note = min(notes, key=lambda n: math.hypot(f(n.get("X"))-wx, f(n.get("Y"))-wy))
        if note not in targets:
            targets.append(note)

    lines = ["# Missing stem page-geometry probe", ""]
    for note in targets:
        staff = staves.get(int(note.get("StaffIndex", -1)), {})
        space = f(staff.get("Space")) or 1.0
        x, y = f(note.get("X")), f(note.get("Y"))
        lines += [
            f"## `{note.get('SourceSymbolId')}` staff {note.get('StaffIndex')} at ({x:.3f}, {y:.3f})",
            "",
            f"staff space: {space:.3f}",
            "",
            "| rank | instance | source | kind | bbox (sp) | bbox distance (sp) | vertical edges near note |",
            "|---:|---|---|---|---|---:|---|",
        ]
        candidates = []
        for item in page:
            box = box_of_geometry(item)
            if box is None:
                continue
            d = point_box_distance(x, y, box) / space
            # Keep a generous local window; we want to see the actual painted neighbours.
            if d > 2.5:
                continue
            edges = vertical_edges(item, space * .65)
            near_edges = [e for e in edges if abs(e[1]-x) <= space*1.25 and not (e[3] < y-space*1.5 or e[2] > y+space*1.5)]
            candidates.append((d, item, box, near_edges))
        candidates.sort(key=lambda z: (z[0], abs(f(z[1].get("X"))-x), abs(f(z[1].get("Y"))-y)))
        for rank, (d, item, box, edges) in enumerate(candidates[:12], 1):
            l,t,r,b = box
            edge_text = "; ".join(f"x={e[1]:.2f}, y={e[2]:.2f}..{e[3]:.2f}, len={e[5]/space:.2f}sp" for e in edges[:4])
            lines.append(
                f"| {rank} | `{item.get('InstanceId')}` | `{item.get('SourceSymbolId') or ''}` | {item.get('SourceKind')} | "
                f"{(r-l)/space:.2f}×{(b-t)/space:.2f} | {d:.3f} | {edge_text} |")
        lines.append("")

    Path(args.output).write_text("\n".join(lines)+"\n", encoding="utf-8")


if __name__ == "__main__":
    main()
