#!/usr/bin/env python3
import argparse
import json
from pathlib import Path


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def dump(path: Path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("pages_dir")
    parser.add_argument("output_prefix")
    args = parser.parse_args()

    pages_dir = Path(args.pages_dir)
    prefix = Path(args.output_prefix)

    analyses = sorted(pages_dir.glob("*.analysis.json"))
    if not analyses:
        raise SystemExit(f"No per-page analysis files found in {pages_dir}")

    pages = []
    merged_events = []
    merged_directions = []
    merged_warnings = []
    totals = {
        "staves": 0,
        "events": 0,
        "notes": 0,
        "rests": 0,
        "directions": 0,
        "warnings": 0,
    }

    for index, analysis_path in enumerate(analyses, 1):
        stem = analysis_path.name.removesuffix(".analysis.json")
        classification_path = pages_dir / f"{stem}.classification.json"
        performance_path = pages_dir / f"{stem}.performance.json"
        analysis = load(analysis_path)
        classification = load(classification_path) if classification_path.exists() else None
        performance = load(performance_path) if performance_path.exists() else None

        events = analysis.get("Events", [])
        directions = analysis.get("Directions", [])
        warnings = analysis.get("Warnings", [])
        notes = sum(1 for event in events if event.get("Step") is not None)
        rests = sum(1 for event in events if str(event.get("Kind", "")).lower().startswith("rest-"))

        page = {
            "page": index,
            "stem": stem,
            "analysis": analysis_path.name,
            "classification": classification_path.name if classification_path.exists() else None,
            "performance": performance_path.name if performance_path.exists() else None,
            "staves": len(analysis.get("Staves", [])),
            "events": len(events),
            "notes": notes,
            "rests": rests,
            "directions": len(directions),
            "warnings": len(warnings),
            "classificationSymbols": len((classification or {}).get("Symbols", [])),
            "totalMs": (performance or {}).get("TotalMs"),
        }
        pages.append(page)

        totals["staves"] += page["staves"]
        totals["events"] += page["events"]
        totals["notes"] += page["notes"]
        totals["rests"] += page["rests"]
        totals["directions"] += page["directions"]
        totals["warnings"] += page["warnings"]

        for event in events:
            merged_events.append({"Page": index, **event})
        for direction in directions:
            merged_directions.append({"Page": index, **direction})
        for warning in warnings:
            merged_warnings.append({"Page": index, "Message": warning})

    aggregate = {
        "PageCount": len(pages),
        "Pages": pages,
        "Totals": totals,
        "Events": merged_events,
        "Directions": merged_directions,
        "Warnings": merged_warnings,
    }
    dump(prefix.with_suffix(".analysis.json"), aggregate)

    md = ["# Multi-page recognition inventory", "", f"Pages: **{len(pages)}**", ""]
    md += [
        "| Page | Source | Staves | Notes | Rests | Directions | Warnings | ms |",
        "|---:|---|---:|---:|---:|---:|---:|---:|",
    ]
    for page in pages:
        ms = page["totalMs"]
        ms_text = f"{ms:.1f}" if isinstance(ms, (int, float)) else ""
        md.append(
            f"| {page['page']} | `{page['stem']}` | {page['staves']} | {page['notes']} | "
            f"{page['rests']} | {page['directions']} | {page['warnings']} | {ms_text} |"
        )
    md += ["", "## Totals", ""]
    for key, value in totals.items():
        md.append(f"- {key}: **{value}**")
    prefix.with_suffix(".inventory.md").write_text("\n".join(md) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
