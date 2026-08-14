# Notation gap inventory

## Notehead sanity

Recognized noteheads: **160**
Unattached noteheads: **1**

- `g` notehead-half staff=3 at (309.56, 324.55)

## Beam geometry

Strict current beam shapes: **7**
Relaxed beam-like shapes: **17**

| path | width sp | height sp | points |
|---|---:|---:|---:|
| `path:000014` | 29.90 | 0.75 | 4 |
| `path:000028` | 16.19 | 0.50 | 5 |
| `path:000038` | 17.44 | 1.00 | 4 |
| `path:000052` | 4.81 | 1.00 | 4 |
| `path:000059` | 4.44 | 1.00 | 4 |
| `path:000065` | 4.52 | 1.00 | 4 |
| `path:000073` | 4.65 | 1.00 | 4 |
| `path:000082` | 18.00 | 0.92 | 4 |
| `path:000083` | 18.62 | 0.92 | 4 |
| `path:000090` | 18.10 | 1.00 | 4 |
| `path:000093` | 1.79 | 1.00 | 4 |
| `path:000107` | 17.98 | 0.50 | 5 |
| `path:000124` | 51.67 | 0.92 | 4 |
| `path:000133` | 18.02 | 0.50 | 5 |
| `path:000139` | 1.79 | 1.00 | 4 |
| `path:000146` | 12.48 | 1.00 | 4 |
| `path:000156` | 15.27 | 1.00 | 4 |

## Arc geometry

Strict current arc shapes: **1**
Relaxed arc-like shapes: **4**

| path | width sp | height sp | points |
|---|---:|---:|---:|
| `use:000067` | 3.43 | 1.90 | 548 |
| `use:000084` | 4.67 | 1.77 | 798 |
| `use:000088` | 1.53 | 0.97 | 319 |
| `path:000020` | 12.06 | 1.75 | 8 |

## Hollow-head candidates

Compact multi-contour reusable glyphs near staves; this is topology evidence, not yet classification.

| symbol | uses | current kind | score | samples (x,y,w,h,contours) |
|---|---:|---|---:|---|
| `g` | 68 | notehead-half | 0.792 | `[(152.51, 168.25, 1.25, 0.89, 2), (152.51, 173.01, 1.25, 0.89, 2), (146.95, 175.39, 1.25, 0.89, 2)]` |
| `v` | 4 | notehead-half | 0.812 | `[(78.58, 247.17, 1.11, 0.97, 2), (255.46, 523.39, 1.11, 0.97, 2), (265.51, 523.39, 1.11, 0.97, 2)]` |
| `l` | 2 | notehead-half | 0.772 | `[(350.98, 107.96, 0.86, 0.9, 2), (359.97, 107.96, 0.86, 0.9, 2)]` |
| `B` | 2 | smufl-unknown | 0.735 | `[(105.9, 247.22, 0.74, 0.96, 2), (294.16, 523.44, 0.74, 0.96, 2)]` |
| `a` | 1 | smufl-unknown | 0.691 | `[(40.32, 816.26, 0.78, 1.23, 2)]` |
| `y` | 1 | smufl-unknown | 0.808 | `[(88.0, 247.23, 0.76, 0.97, 2)]` |
| `A` | 1 | time-signature-digit | 0.749 | `[(99.87, 248.25, 0.73, 1.34, 2)]` |
| `I` | 1 | smufl-unknown | 0.774 | `[(274.36, 524.22, 1.02, 1.3, 3)]` |
| `K` | 1 | smufl-unknown | 0.723 | `[(288.89, 522.53, 0.96, 1.35, 2)]` |

## Standalone-flag candidates

Compact reusable glyphs repeatedly found near free ends of currently unbeamed stems.

| symbol | hits | current kind | reference | samples (dx,dy,w,h sp) |
|---|---:|---|---|---|
| `s` | 8 | smufl-unknown | uniE251 | `[(0.47, 1.07, 1.08, 2.45), (0.47, 1.07, 1.08, 2.45), (0.47, 1.07, 1.08, 2.45)]` |
| `E` | 2 | smufl-unknown | uniE241 | `[(0.5, 1.48, 1.13, 3.27), (0.5, 1.48, 1.13, 3.27)]` |
