# GlyphPcaGallery

Experimental .NET runtime for the Colab glyph PCA models.

Pipeline: `SVG vector path -> per-glyph 2D PCA canonical rotation -> 32x32 SDF -> trained PCA-4 -> nearest class prototypes`.

## Model bundle

`GlyphPcaGallery` now expects a single `glyph-models.zip` instead of a standalone `glyph-model.json`.

The ZIP is expected to contain specialized models plus a manifest:

```text
glyph-models.zip
├── glyph-model-all.json
├── glyph-model-clefs.json
├── glyph-model-meters.json
├── glyph-model-tuplets.json
├── glyph-model-accidentals.json
├── glyph-model-rests.json
├── glyph-model-flags.json
├── glyph-model-dynamics.json
├── glyph-model-pedals.json
├── glyph-model-ornaments.json
└── glyph-models-manifest.json
```

Manifest example:

```json
{
  "models": {
    "all": "glyph-model-all.json",
    "clefs": "glyph-model-clefs.json",
    "meters": "glyph-model-meters.json",
    "accidentals": "glyph-model-accidentals.json"
  }
}
```

If the manifest is absent, the loader falls back to discovering `glyph-model-*.json` entries in the ZIP.

Each model is deserialized independently and keeps its own PCA basis and calibration. The gallery deliberately does not merge prototypes from different families into one classifier.

## Run

```powershell
dotnet run --project GlyphPcaGallery -- `
  --models D:\temp\glyph-models.zip `
  --input D:\path\to\svg-folder `
  --output D:\temp\glyph-gallery `
  --parallelism 8
```

Open `glyph-gallery/index.html`.

The root page lists every model contained in the bundle and links to its own diagnostic gallery:

```text
glyph-gallery/
├── index.html
├── all/index.html
├── clefs/index.html
├── meters/index.html
├── accidentals/index.html
└── ...
```

For diagnostics every input SVG is run through every available specialized model. This is intentional: the page lets us inspect which families reject or accidentally accept the same real glyph. Runtime resolvers should still select the appropriate family first and then classify only inside that model.

Each per-model card shows top matches plus `distance`, normalized class distance, ratio, risk and analysis time. The threshold multiplier slider remains diagnostic and affects only the rendered decision.
