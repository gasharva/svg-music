# Notation gap inventory

## Notehead sanity

Recognized noteheads: **154**
Unattached noteheads: **2**

- `P` notehead-half staff=0 at (162.26, 140.19)
- `R` notehead-half staff=0 at (176.09, 140.19)

## Beam geometry

Strict current beam shapes: **0**
Relaxed beam-like shapes: **10**

| path | width sp | height sp | points |
|---|---:|---:|---:|
| `path:000007` | 5.77 | 1.28 | 8 |
| `path:000010` | 1.79 | 1.00 | 4 |
| `path:000032` | 19.06 | 1.00 | 4 |
| `path:000038` | 19.12 | 0.96 | 4 |
| `path:000039` | 21.25 | 0.96 | 4 |
| `path:000064` | 14.06 | 1.00 | 4 |
| `path:000094` | 19.98 | 1.00 | 4 |
| `path:000100` | 17.09 | 0.96 | 4 |
| `path:000125` | 12.81 | 1.00 | 4 |
| `path:000139` | 1.79 | 1.00 | 4 |

## Arc geometry

Strict current arc shapes: **1**
Relaxed arc-like shapes: **14**

| path | width sp | height sp | points |
|---|---:|---:|---:|
| `use:000094` | 3.33 | 1.96 | 896 |
| `use:000205` | 3.33 | 1.96 | 896 |
| `use:000346` | 1.80 | 1.20 | 287 |
| `use:000351` | 1.80 | 1.20 | 287 |
| `use:000353` | 2.38 | 1.69 | 162 |
| `use:000355` | 1.80 | 1.20 | 287 |
| `use:000363` | 1.80 | 1.20 | 287 |
| `use:000405` | 2.24 | 1.35 | 148 |
| `path:000007` | 5.77 | 1.28 | 8 |
| `path:000015` | 6.06 | 1.75 | 8 |
| `path:000047` | 7.60 | 1.75 | 8 |
| `path:000077` | 6.35 | 1.75 | 8 |
| `path:000108` | 6.60 | 1.75 | 8 |
| `path:000152` | 12.46 | 2.42 | 25 |

## Hollow-head candidates

Compact multi-contour reusable glyphs near staves; this is topology evidence, not yet classification.

| symbol | uses | current kind | score | samples (x,y,w,h,contours) |
|---|---:|---|---:|---|
| `D` | 78 | notehead-half | 0.818 | `[(159.66, 165.88, 1.27, 0.78, 2), (121.77, 181.95, 1.27, 0.78, 2), (116.41, 184.25, 1.27, 0.78, 2)]` |
| `av` | 6 | smufl-unknown | 0.690 | `[(246.22, 62.72, 0.9, 1.2, 2), (260.36, 62.72, 0.9, 1.2, 2), (211.77, 77.1, 0.9, 1.2, 2)]` |
| `aN` | 5 | smufl-unknown | 0.620 | `[(246.42, 77.14, 1.11, 1.18, 2), (269.05, 77.14, 1.11, 1.18, 2), (277.67, 77.14, 1.11, 1.18, 2)]` |
| `aA` | 4 | smufl-unknown | 0.808 | `[(275.71, 62.72, 1.03, 1.2, 2), (329.58, 62.72, 1.03, 1.2, 2), (216.8, 77.1, 1.03, 1.2, 2)]` |
| `F` | 2 | smufl-unknown | 0.807 | `[(111.38, 137.78, 1.34, 1.12, 2), (126.27, 137.78, 1.34, 1.12, 2)]` |
| `aZ` | 2 | smufl-unknown | 0.795 | `[(413.59, 121.75, 1.13, 1.23, 2), (430.08, 121.75, 1.13, 1.23, 2)]` |
| `b` | 1 | smufl-unknown | 0.698 | `[(40.32, 816.26, 0.8, 1.28, 2)]` |
| `c` | 1 | smufl-unknown | 0.653 | `[(245.8, 807.16, 1.0, 1.01, 3)]` |
| `L` | 1 | smufl-unknown | 0.786 | `[(142.42, 137.76, 0.89, 1.13, 2)]` |
| `P` | 1 | notehead-half | 0.756 | `[(162.26, 137.4, 1.04, 0.61, 2)]` |
| `Q` | 1 | smufl-unknown | 0.786 | `[(172.9, 138.1, 1.06, 1.14, 2)]` |
| `aB` | 1 | smufl-unknown | 0.647 | `[(293.78, 58.17, 0.83, 0.61, 2)]` |
| `aF` | 1 | smufl-unknown | 0.641 | `[(334.44, 58.21, 0.83, 0.6, 2)]` |
| `bf` | 1 | smufl-unknown | 0.789 | `[(453.76, 121.8, 1.02, 1.21, 2)]` |

## Standalone-flag candidates

Compact reusable glyphs repeatedly found near free ends of currently unbeamed stems.

| symbol | hits | current kind | reference | samples (dx,dy,w,h sp) |
|---|---:|---|---|---|
| `C` | 5 | notehead-black | uniE113 | `[(0.67, 2.55, 1.02, 0.88), (0.67, 0.21, 1.02, 0.88), (0.67, 2.55, 1.02, 0.88)]` |
| `ac` | 4 | notehead-black | uniE581 | `[(0.42, 1.96, 0.57, 0.53), (0.87, 2.46, 0.57, 0.53), (1.71, 1.67, 0.57, 0.53)]` |
| `ab` | 3 | smufl-unknown | uniE06C | `[(1.02, 1.39, 2.18, 3.57), (1.01, 1.39, 2.18, 3.57), (1.01, 1.39, 2.18, 3.57)]` |
| `Z` | 2 | smufl-unknown | uniE251 | `[(0.48, 0.96, 1.1, 2.72), (0.48, 0.96, 1.1, 2.72)]` |
| `D` | 2 | notehead-half | uniE0FB | `[(0.6, 0.63, 1.27, 0.78), (0.57, 1.13, 1.27, 0.78)]` |
| `W` | 1 | notehead-black | uniE113 | `[(0.67, 2.55, 1.02, 0.88)]` |
