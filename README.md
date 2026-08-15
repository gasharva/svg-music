# svg-music

Current development is focused on reconstructing musical score structure directly from SVG geometry.

## Active projects

- `SvgStructure` — detects staff systems, parts, measures and classifies raw SVG primitives.
- `SvgSymbols` — experiments with vector symbol normalization and recognition.

Shared input/reference data stays in `Samples` and `References`.

## Legacy

The previous SVG → MusicXML implementation, its tests, tools, scripts and golden assets are kept under `Legacy` for reference only. It is intentionally excluded from the active solution.

Open `SvgToMusicXmlPoc.sln` for current work. The old solution is under `Legacy/SvgToMusicXmlPoc.Legacy.sln`.
