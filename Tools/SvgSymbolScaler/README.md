# SvgSymbolScaler

Small standalone utility that makes compact SVG notation objects larger while keeping their centers fixed.

It accepts either one SVG file or a directory of SVG files. Staff lines and other line-like objects are left unchanged.

## Single file

```powershell
dotnet run --project Tools/SvgSymbolScaler -- score.svg
```

Creates `score.scaled.svg` next to the source file.

```powershell
dotnet run --project Tools/SvgSymbolScaler -- score.svg readable.svg --scale 1.5
```

## Directory

```powershell
dotnet run --project Tools/SvgSymbolScaler -- .\pages .\pages-readable --recursive
```

## Options

```text
--scale       Scale factor. Default: 1.5
--max-size    Maximum compact-object width and height in SVG units. Default: 120
--max-aspect  Maximum aspect ratio before an object is considered line-like. Default: 12
--recursive   Include subdirectories
```

The current compactness rule is deliberately simple and configurable:

- supported elements: `path`, `use`, `circle`, `ellipse`, `rect`, `polygon`, `polyline`;
- elements inside `defs` and `symbol` are not changed;
- objects larger than `max-size` are skipped;
- very elongated objects are skipped;
- `StaffLines`, `BarLine`, `Stem`, `Beam` and open stroke-only polylines are skipped.

The scaler preserves an element's existing `transform` and adds a nested center-preserving transform:

```xml
<g transform="original transform">
  <g transform="translate(cx cy) scale(1.5) translate(-cx -cy)">
    <!-- original element -->
  </g>
</g>
```
