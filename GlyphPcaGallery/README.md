# GlyphPcaGallery

Experimental .NET runtime for the Colab glyph model.

Pipeline: `SVG vector path -> per-glyph 2D PCA canonical rotation -> 32x32 SDF -> trained PCA-4 -> nearest class prototypes`.

## Export model from Colab

Run after the notebook has built `X`, `labels`, `names`:

```python
!wget -q https://raw.githubusercontent.com/gasharva/svg-music/master/svg-centerline-colab/experiments/glyph-pca/export_dotnet_model.py

from export_dotnet_model import export_dotnet_model
from google.colab import files

model, pca4, F4 = export_dotnet_model(
    X=X,
    labels=labels,
    names=names,
    output_path="glyph-model.json",
    components=4,
    normalization_mode=NORMALIZATION,
    boundary_samples=BOUNDARY_SAMPLES,
    target_radius=TARGET_RADIUS,
    sdf_grid_size=GRID_SIZE,
    sdf_grid_extent=GRID_EXTENT,
    sdf_clip=SDF_CLIP,
    sdf_boundary_samples=1024,
)

files.download("glyph-model.json")
```

## Run

```powershell
dotnet run --project GlyphPcaGallery -- `
  --model D:\temp\glyph-model.json `
  --input D:\path\to\svg-folder `
  --output D:\temp\glyph-gallery `
  --parallelism 8
```

Open `glyph-gallery/index.html`.

Each card shows top-5 classes plus `distance`, `margin`, relative margin, absolute-distance confidence and analysis time. Border hue goes red -> green. The gallery has a confidence threshold slider and sorting by confidence, so the open-set threshold can be chosen visually against the real corpus.

`confidence` is diagnostic, not a probability. Raw distance and margin are intentionally preserved for threshold analysis.

The Colab SDF uses exact Shapely distance to its vectorized polygon. .NET approximates vector boundary distance with `sdfBoundarySamples` (default 1024). The first corpus run is also a Python/.NET parity test; if known training glyphs are unexpectedly far from their prototypes, increase this value or add an explicit parity fixture before tuning thresholds.
