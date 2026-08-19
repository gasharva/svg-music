# SmuflDatasetBuilder

First step for building a labeled music-symbol dataset from SMuFL fonts.

At this stage the tool intentionally does **not** extract glyph outlines yet. It first downloads the official W3C SMuFL metadata and generates an inventory so we can decide which symbols are useful training classes.

## Run

```bash
dotnet run --project SmuflDatasetBuilder
```

Generated under `bin/.../output/`:

- `smufl-inventory.html` — searchable browser view of all glyph names and SMuFL classes/groups.
- `smufl-glyphs.csv` — canonical glyph name, codepoint, description and SMuFL groups.
- `smufl-classes.csv` — SMuFL group name and its glyph members.

For the future ML dataset, canonical glyph names such as `gClef`, `fClef`, `accidentalFlat`, etc. are candidates for labels. SMuFL `classes.json` groups are useful for navigation/filtering but are not automatically the model labels.

## Planned engraved font pool

Initial non-handwritten font set to evaluate after choosing the glyph subset:

- Bravura
- Leland
- Emmentaler
- Gootville / Gonville style
- Finale Maestro
- Sebastian
- Leipzig

The next stage will download/open these fonts, calculate glyph coverage for the selected SMuFL names, and export normalized SVG outlines only for the selected training labels.
