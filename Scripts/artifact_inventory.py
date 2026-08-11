#!/usr/bin/env python3
import argparse
import collections
import json
from pathlib import Path


def pct(value, total):
    return round(value * 100.0 / total, 1) if total else 0.0


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
