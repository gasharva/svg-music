#!/usr/bin/env python3
import argparse
import collections
import json
from pathlib import Path


def pct(value, total):
    return round(value * 100.0 / total, 1) if total else 0.0


def point_value(point, name):
    return float(point.get(name, 0) or 0)


def contour_box(contour):
    if not contour:
        return None
    xs = [point_value(p, "X") for p in contour]
    ys = [point_value(p, "Y") for p in contour]
    return {
        "left": min(xs),
        "right": max(xs),
        "top": min(ys),
        "bottom": max(ys),
    }


def vertical_gap(y, top, bottom):
    if y < top:
        return top - y
    if y > bottom:
        return y - bottom
    return 0.0


def main():
    parser = argparse.ArgumentParser(description="Summarize recognition-layer health from conversion artifacts")
    parser.add_argument("analysis")
    parser.add_argument("classification")
    parser.add_argument("output_prefix")
    args = parser.parse_args()

    analysis = json.loads(Path(args.analysis).read_text(encoding="utf-8-sig"))
    classification = json.loads(Path(args.classification).read_text(encoding="utf-8-sig"))

    staves = analysis.get("Staves", [])
    uses = analysis.get("Uses", [])
    direct_paths = analysis.get("DirectPaths", [])
    line_segments = analysis.get("LineSegments", [])
    events = analysis.get("Events", [])
    warnings = analysis.get("Warnings", [])
    classes = {x.get("SymbolId"): x for x in classification.get("Symbols", [])}

    notes = [x for x in events if str(x.get("Kind", "")).startswith("notehead-")]

    def count_notes(predicate):
        return sum(1 for note in notes if predicate(note))

    relation_counts = {
        "notes": len(notes),
        "withStem": count_notes(lambda x: x.get("StemX") is not None),
        "withStemDirection": count_notes(lambda x: x.get("StemDirection") is not None),
        "chordMembers": count_notes(lambda x: bool(x.get("Chord"))),
        "withBeam": count_notes(lambda x: int(x.get("BeamCount") or 0) > 0),
        "withBeamValue": count_notes(lambda x: x.get("BeamValue") is not None),
        "eighth": count_notes(lambda x: x.get("Type") == "eighth"),
        "sixteenth": count_notes(lambda x: x.get("Type") == "16th"),
        "dotted": count_notes(lambda x: bool(x.get("Dotted"))),
        "slurStarts": count_notes(lambda x: bool(x.get("SlurStart"))),
        "slurStops": count_notes(lambda x: bool(x.get("SlurStop"))),
        "tieStarts": count_notes(lambda x: bool(x.get("TieStart"))),
        "tieStops": count_notes(lambda x: bool(x.get("TieStop"))),
        "altered": count_notes(lambda x: x.get("Alter") not in (None, 0)),
    }

    event_kinds = collections.Counter(x.get("Kind", "<null>") for x in events)

    def near_staff(use):
        for staff in staves:
            space = staff.get("Space") or 1.0
            if (use.get("X", 0) >= staff.get("Left", 0) - 2 * space and
                use.get("X", 0) <= staff.get("Right", 0) + 2 * space and
                use.get("Y", 0) >= staff.get("Top", 0) - 5 * space and
                use.get("Y", 0) <= staff.get("Bottom", 0) + 5 * space):
                return True
        return False

    near_counts = collections.Counter(
        use.get("SymbolId") for use in uses if near_staff(use) and use.get("SymbolId"))

    suspicious = []
    for symbol_id, count in near_counts.most_common():
        item = classes.get(symbol_id)
        if item is None:
            suspicious.append({
                "symbolId": symbol_id,
                "usesNearStaff": count,
                "kind": "<unclassified>",
                "referenceId": None,
                "score": None,
                "widthInSpaces": None,
                "heightInSpaces": None,
                "reason": "no classification",
            })
            continue

        kind = item.get("Kind") or ""
        score = item.get("Score")
        is_suspicious = kind == "smufl-unknown" or (score is not None and score < 0.60)
        if not is_suspicious:
            continue
        suspicious.append({
            "symbolId": symbol_id,
            "usesNearStaff": count,
            "kind": kind,
            "referenceId": item.get("ReferenceId"),
            "score": score,
            "widthInSpaces": item.get("WidthInSpaces"),
            "heightInSpaces": item.get("HeightInSpaces"),
            "reason": "unknown semantic kind" if kind == "smufl-unknown" else "low confidence",
        })

    warning_groups = collections.Counter()
    for warning in warnings:
        text = str(warning)
        if text.startswith("Низкая уверенность"):
            warning_groups["low confidence"] += 1
        else:
            warning_groups[text.split(":", 1)[0]] += 1

    # Missing-stem triage. This deliberately mirrors the production resolver closely enough to
    # tell us which layer starved it, but remains diagnostic-only: it never changes conversion.
    staff_by_index = {int(x.get("Index", -1)): x for x in staves}
    average_space = (sum(float(x.get("Space") or 0) for x in staves) / len(staves)) if staves else 1.0

    normalized_stems = []
    for line in line_segments:
        x1 = float(line.get("X1") or 0)
        x2 = float(line.get("X2") or 0)
        y1 = float(line.get("Y1") or 0)
        y2 = float(line.get("Y2") or 0)
        width = abs(x2 - x1)
        height = abs(y2 - y1)
        if width > average_space * .16:
            continue
        if height < average_space * 1.35 or height > average_space * 7.0:
            continue
        normalized_stems.append({
            "x": (x1 + x2) / 2,
            "top": min(y1, y2),
            "bottom": max(y1, y2),
            "sourceKind": line.get("SourceKind"),
        })

    raw_verticals = []
    for path in direct_paths:
        geometry = path.get("Geometry") or {}
        for contour_index, contour in enumerate(geometry.get("Contours") or []):
            box = contour_box(contour)
            if box is None:
                continue
            width = box["right"] - box["left"]
            height = box["bottom"] - box["top"]
            # Broader than SvgParser.ReadLineSegments on purpose. These are shapes worth inspecting
            # when normalization missed a stem, not shapes that are safe enough to use in conversion.
            if height < average_space * 1.05 or height > average_space * 7.5:
                continue
            if width > max(average_space * .38, height * .20):
                continue
            raw_verticals.append({
                "x": (box["left"] + box["right"]) / 2,
                "top": box["top"],
                "bottom": box["bottom"],
                "width": width,
                "height": height,
                "pathId": path.get("SymbolId"),
                "contour": contour_index,
            })

    def same_as_normalized(raw):
        return any(
            abs(line["x"] - raw["x"]) <= average_space * .12 and
            abs(line["top"] - raw["top"]) <= average_space * .20 and
            abs(line["bottom"] - raw["bottom"]) <= average_space * .20
            for line in normalized_stems)

    raw_only_verticals = [x for x in raw_verticals if not same_as_normalized(x)]
    missing_stem_notes = [x for x in notes if x.get("StemX") is None]
    stem_gap_samples = []
    stem_gap_counts = collections.Counter()

    for note in missing_stem_notes:
        staff = staff_by_index.get(int(note.get("StaffIndex", -1)))
        space = float((staff or {}).get("Space") or average_space or 1.0)
        x = float(note.get("X") or 0)
        y = float(note.get("Y") or 0)

        nearby_lines = [
            line for line in normalized_stems
            if abs(line["x"] - x) <= space * 1.12 and
               vertical_gap(y, line["top"], line["bottom"]) <= space * 1.20
        ]
        exact_lines = [
            line for line in nearby_lines
            if line["top"] <= y + space * .65 and line["bottom"] >= y - space * .65
        ]
        nearby_raw_only = [
            raw for raw in raw_only_verticals
            if abs(raw["x"] - x) <= space * 1.25 and
               vertical_gap(y, raw["top"], raw["bottom"]) <= space * 1.30
        ]

        if exact_lines:
            category = "resolverUnexpectedGap"
        elif nearby_lines:
            category = "attachmentGeometryGap"
        elif nearby_raw_only:
            category = "normalizationGap"
        else:
            category = "noStemGeometryCandidate"
        stem_gap_counts[category] += 1

        if len(stem_gap_samples) < 30:
            nearest_line = min(
                nearby_lines,
                key=lambda line: (abs(line["x"] - x), vertical_gap(y, line["top"], line["bottom"])),
                default=None)
            nearest_raw = min(
                nearby_raw_only,
                key=lambda raw: (abs(raw["x"] - x), vertical_gap(y, raw["top"], raw["bottom"])),
                default=None)
            stem_gap_samples.append({
                "category": category,
                "symbolId": note.get("SourceSymbolId"),
                "staff": note.get("StaffIndex"),
                "x": x,
                "y": y,
                "nearestNormalizedDxSpaces": None if nearest_line is None else round(abs(nearest_line["x"] - x) / space, 3),
                "nearestNormalizedYGapSpaces": None if nearest_line is None else round(vertical_gap(y, nearest_line["top"], nearest_line["bottom"]) / space, 3),
                "nearestRawOnlyDxSpaces": None if nearest_raw is None else round(abs(nearest_raw["x"] - x) / space, 3),
                "nearestRawOnlyYGapSpaces": None if nearest_raw is None else round(vertical_gap(y, nearest_raw["top"], nearest_raw["bottom"]) / space, 3),
                "rawPathId": None if nearest_raw is None else nearest_raw.get("pathId"),
                "rawContour": None if nearest_raw is None else nearest_raw.get("contour"),
            })

    stem_gap_inventory = {
        "unattachedNotes": len(missing_stem_notes),
        "normalizedStemCandidates": len(normalized_stems),
        "broadRawVerticalCandidates": len(raw_verticals),
        "rawVerticalCandidatesNotNormalized": len(raw_only_verticals),
        "categories": dict(stem_gap_counts),
        "samples": stem_gap_samples,
    }

    inventory = {
        "structuralInputs": {
            "staves": len(staves),
            "uses": len(uses),
            "directPaths": len(direct_paths),
            "lineSegments": len(line_segments),
        },
        "eventKinds": dict(event_kinds.most_common()),
        "relations": relation_counts,
        "relationCoveragePercent": {
            key: pct(value, len(notes))
            for key, value in relation_counts.items()
            if key != "notes"
        },
        "stemGapTriage": stem_gap_inventory,
        "warnings": {
            "total": len(warnings),
            "groups": dict(warning_groups.most_common()),
        },
        "suspiciousNearStaffSymbols": suspicious[:30],
    }

    prefix = Path(args.output_prefix)
    prefix.with_suffix(".inventory.json").write_text(
        json.dumps(inventory, ensure_ascii=False, indent=2), encoding="utf-8")

    lines = [
        "# Recognition inventory",
        "",
        "## Structural inputs",
        "",
        f"- staves: **{len(staves)}**",
        f"- reusable/direct uses: **{len(uses)}**",
        f"- direct paths: **{len(direct_paths)}**",
        f"- normalized line segments: **{len(line_segments)}**",
        "",
        "## Semantic events",
        "",
    ]
    for kind, count in event_kinds.most_common():
        lines.append(f"- {kind}: **{count}**")

    lines += ["", "## Relation coverage", "", "| Relation | Count | % of notes |", "|---|---:|---:|"]
    relation_labels = [
        ("withStem", "stem attached"),
        ("withStemDirection", "stem direction"),
        ("chordMembers", "chord members"),
        ("withBeam", "notes touching beams"),
        ("withBeamValue", "beam begin/continue/end"),
        ("eighth", "eighth notes"),
        ("sixteenth", "16th notes"),
        ("dotted", "dotted notes"),
        ("slurStarts", "slur starts"),
        ("slurStops", "slur stops"),
        ("tieStarts", "tie starts"),
        ("tieStops", "tie stops"),
        ("altered", "altered pitches"),
    ]
    for key, label in relation_labels:
        value = relation_counts[key]
        lines.append(f"| {label} | {value} | {pct(value, len(notes)):.1f}% |")

    lines += [
        "",
        "## Missing-stem triage",
        "",
        f"Unattached noteheads: **{len(missing_stem_notes)}**",
        f"Normalized stem candidates: **{len(normalized_stems)}**",
        f"Broad raw vertical candidates: **{len(raw_verticals)}**",
        f"Raw vertical candidates not normalized: **{len(raw_only_verticals)}**",
        "",
        "| Category | Count | Meaning |",
        "|---|---:|---|",
        f"| normalization gap | {stem_gap_counts['normalizationGap']} | stem-like raw path exists near the note, but no normalized line candidate does |",
        f"| attachment geometry gap | {stem_gap_counts['attachmentGeometryGap']} | normalized stem is nearby, but current note↔stem endpoint/intersection tolerance rejects it |",
        f"| unexpected resolver gap | {stem_gap_counts['resolverUnexpectedGap']} | a normalized candidate satisfies the resolver's current attachment window but StemX is still empty |",
        f"| no stem geometry candidate | {stem_gap_counts['noStemGeometryCandidate']} | neither normalized nor broad raw vertical geometry was found near the note |",
        "",
        "### Sample unattached notes",
        "",
        "| category | symbol | staff | x | y | line dx sp | line y-gap sp | raw dx sp | raw y-gap sp | raw path |",
        "|---|---|---:|---:|---:|---:|---:|---:|---:|---|",
    ]
    for item in stem_gap_samples:
        def fmt(value):
            return "" if value is None else f"{value:.3f}"
        raw_path = "" if item["rawPathId"] is None else f'{item["rawPathId"]}#{item["rawContour"]}'
        lines.append(
            f'| {item["category"]} | `{item["symbolId"] or ""}` | {item["staff"]} | '
            f'{item["x"]:.2f} | {item["y"]:.2f} | {fmt(item["nearestNormalizedDxSpaces"])} | '
            f'{fmt(item["nearestNormalizedYGapSpaces"])} | {fmt(item["nearestRawOnlyDxSpaces"])} | '
            f'{fmt(item["nearestRawOnlyYGapSpaces"])} | `{raw_path}` |')

    lines += [
        "",
        "## Warning groups",
        "",
        f"Total warnings: **{len(warnings)}**",
        "",
    ]
    for name, count in warning_groups.most_common():
        lines.append(f"- {name}: **{count}**")

    lines += [
        "",
        "## High-value suspicious symbols near staves",
        "",
        "These are frequent staff-local glyphs that are still semantically unknown or have low classification confidence.",
        "",
        "| id | uses | kind | reference | score | width sp | height sp | reason |",
        "|---|---:|---|---|---:|---:|---:|---|",
    ]
    for item in suspicious[:30]:
        score = "" if item["score"] is None else f'{item["score"]:.3f}'
        width = "" if item["widthInSpaces"] is None else f'{item["widthInSpaces"]:.3f}'
        height = "" if item["heightInSpaces"] is None else f'{item["heightInSpaces"]:.3f}'
        lines.append(
            f'| `{item["symbolId"]}` | {item["usesNearStaff"]} | {item["kind"]} | '
            f'{item["referenceId"] or ""} | {score} | {width} | {height} | {item["reason"]} |')

    prefix.with_suffix(".inventory.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
