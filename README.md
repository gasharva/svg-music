# svg-music

Current development is focused on reconstructing musical score structure directly from SVG geometry.

## Step-by-step status

[**Open the latest SvgStructure step-by-step report**](https://github.com/gasharva/svg-music/tree/ci-output/latest/step-by-step)

Every push to `master` runs all SVG files from `Samples/step-by-step` through the current structure pipeline and publishes the latest overlays, structure JSON and report there.

## Active projects

- `SvgStructure` — detects staff systems, parts, measures and classifies raw SVG primitives.
- `SvgSymbols` — experiments with vector symbol normalization and recognition.

Shared input/reference data stays in `Samples` and `References`.

## Legacy

The previous SVG → MusicXML implementation, its tests, tools, scripts and golden assets are kept under `Legacy` for reference only. It is intentionally excluded from the active solution.

Open `SvgToMusicXmlPoc.sln` for current work. The old solution is under `Legacy/SvgToMusicXmlPoc.Legacy.sln`.
