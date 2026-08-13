# Notation gap inventory

## Notehead sanity

Recognized noteheads: **110**
Unattached noteheads: **0**


## Beam geometry

Strict current beam shapes: **0**
Relaxed beam-like shapes: **6**

| path | width sp | height sp | points |
|---|---:|---:|---:|
| `path:000004` | 21.09 | 0.96 | 4 |
| `path:000028` | 16.89 | 1.00 | 4 |
| `path:000039` | 1.79 | 1.00 | 4 |
| `path:000060` | 20.89 | 1.00 | 4 |
| `path:000094` | 45.16 | 0.96 | 4 |
| `path:000106` | 7.69 | 1.30 | 8 |

## Arc geometry

Strict current arc shapes: **2**
Relaxed arc-like shapes: **9**

| path | width sp | height sp | points |
|---|---:|---:|---:|
| `use:000055` | 2.43 | 0.93 | 334 |
| `use:000117` | 3.49 | 1.55 | 967 |
| `use:000133` | 3.30 | 1.88 | 848 |
| `use:000222` | 5.26 | 3.09 | 1368 |
| `path:000012` | 5.94 | 1.75 | 8 |
| `path:000043` | 6.65 | 1.75 | 8 |
| `path:000073` | 6.52 | 1.75 | 8 |
| `path:000098` | 9.90 | 1.75 | 8 |
| `path:000106` | 7.69 | 1.30 | 8 |

## Hollow-head candidates

Compact multi-contour reusable glyphs near staves; this is topology evidence, not yet classification.

| symbol | uses | current kind | score | samples (x,y,w,h,contours) |
|---|---:|---|---:|---|
| `i` | 57 | notehead-half | 0.784 | `[(125.62, 79.87, 1.29, 0.79, 2), (120.32, 82.14, 1.29, 0.79, 2), (125.62, 88.95, 1.29, 0.79, 2)]` |
| `u` | 3 | smufl-unknown | 0.796 | `[(308.45, 464.1, 0.98, 1.19, 2), (318.55, 464.1, 0.98, 1.19, 2), (332.69, 464.1, 0.98, 1.19, 2)]` |
| `A` | 1 | smufl-unknown | 0.841 | `[(349.11, 464.38, 1.09, 1.14, 2)]` |

## Standalone-flag candidates

Compact reusable glyphs repeatedly found near free ends of currently unbeamed stems.

| symbol | hits | current kind | reference | samples (dx,dy,w,h sp) |
|---|---:|---|---|---|
| `s` | 3 | smufl-unknown | uniE241 | `[(0.47, 1.43, 0.98, 3.15), (0.47, 1.42, 0.98, 3.15), (0.47, 1.42, 0.98, 3.14)]` |
| `f` | 1 | notehead-black | uniE0FB | `[(0.19, 2.3, 0.67, 0.79)]` |
| `g` | 1 | accidental-natural | accidentalNatural | `[(0.87, 2.51, 0.66, 3.02)]` |
| `o` | 1 | smufl-unknown | uniE06C | `[(0.97, 1.99, 2.09, 2.81)]` |
