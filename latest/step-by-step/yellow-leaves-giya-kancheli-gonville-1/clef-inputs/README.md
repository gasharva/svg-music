# Clef recognizer inputs

These are the exact post-sanity-filter vector candidates sent to `IClefRecognizer`.

`Legacy IoU` is the old shape-matching baseline: bbox-normalized 64x64 binary-mask IoU plus Clipper2 vector IoU. No size or staff-position prior is used.

`Skeleton` is the raw scanline-midpoint graph. `lines` traces that graph into chains and simplifies each chain with Ramer-Douglas-Peucker; no smoothing yet.

| Candidate | P+M | Logical bbox | Vector recognizer | Legacy IoU | Shape | Skeleton |
|---|---|---|---|---|---|---|
| [001](001.txt) | P1-M1 | `X 0.79..3.03, Y -2.64..11.06` | G 98.3 % | G 76.3 % | ![001](001.png) | [raw](001.skeleton.svg) · [lines](001.skeleton-lines.svg) |
| [002](002.txt) | P1-M1 | `X 4.06..5.44, Y 0.13..7.88` | none (no result) | G 36.3 % | ![002](002.png) | [raw](002.skeleton.svg) · [lines](002.skeleton-lines.svg) |
| [003](003.txt) | P2-M1 | `X 0.79..2.75, Y -0.03..6.37` | F 97.1 % | F 74.1 % | ![003](003.png) | [raw](003.skeleton.svg) · [lines](003.skeleton-lines.svg) |
| [004](004.txt) | P2-M1 | `X 4.06..5.44, Y 0.13..7.88` | none (no result) | G 36.3 % | ![004](004.png) | [raw](004.skeleton.svg) · [lines](004.skeleton-lines.svg) |
| [005](005.txt) | P1-M5 | `X 0.81..3.1, Y -2.64..11.06` | G 98.3 % | G 76.3 % | ![005](005.png) | [raw](005.skeleton.svg) · [lines](005.skeleton-lines.svg) |
| [006](006.txt) | P2-M5 | `X 0.81..2.82, Y -0.03..6.37` | F 97.1 % | F 74.1 % | ![006](006.png) | [raw](006.skeleton.svg) · [lines](006.skeleton-lines.svg) |
| [007](007.txt) | P2-M6 | `X 8.66..10.04, Y -7.61..0.08` | none (no result) | F 31.0 % | ![007](007.png) | [raw](007.skeleton.svg) · [lines](007.skeleton-lines.svg) |
| [008](008.txt) | P1-M9 | `X 0.86..3.32, Y -2.64..11.06` | G 98.3 % | G 76.3 % | ![008](008.png) | [raw](008.skeleton.svg) · [lines](008.skeleton-lines.svg) |
| [009](009.txt) | P1-M9 | `X 11.93..16.13, Y -0.11..8.08` | none (no result) | F 15.3 % | ![009](009.png) | [raw](009.skeleton.svg) · [lines](009.skeleton-lines.svg) |
| [010](010.txt) | P1-M9 | `X 13.05..16.13, Y -0.11..7.04` | none (no result) | F 16.1 % | ![010](010.png) | [raw](010.skeleton.svg) · [lines](010.skeleton-lines.svg) |
| [011](011.txt) | P2-M9 | `X 0.86..3.01, Y -0.03..6.37` | F 97.1 % | F 74.1 % | ![011](011.png) | [raw](011.skeleton.svg) · [lines](011.skeleton-lines.svg) |
| [012](012.txt) | P1-M13 | `X 0.79..3.03, Y -2.64..11.06` | G 98.3 % | G 76.3 % | ![012](012.png) | [raw](012.skeleton.svg) · [lines](012.skeleton-lines.svg) |
| [013](013.txt) | P2-M13 | `X 0.79..2.75, Y -0.03..6.37` | F 97.1 % | F 74.1 % | ![013](013.png) | [raw](013.skeleton.svg) · [lines](013.skeleton-lines.svg) |
| [014](014.txt) | P1-M17 | `X 0.76..2.9, Y -2.64..11.06` | G 98.3 % | G 76.3 % | ![014](014.png) | [raw](014.skeleton.svg) · [lines](014.skeleton-lines.svg) |
| [015](015.txt) | P1-M17 | `X 11.97..13.92, Y -4.61..3.08` | none (no result) | G 23.6 % | ![015](015.png) | [raw](015.skeleton.svg) · [lines](015.skeleton-lines.svg) |
| [016](016.txt) | P1-M17 | `X 12.95..13.92, Y -4.61..2.54` | none (no result) | F 23.9 % | ![016](016.png) | [raw](016.skeleton.svg) · [lines](016.skeleton-lines.svg) |
| [017](017.txt) | P2-M17 | `X 0.76..2.63, Y -0.03..6.37` | F 97.1 % | F 74.1 % | ![017](017.png) | [raw](017.skeleton.svg) · [lines](017.skeleton-lines.svg) |
| [018](018.txt) | P1-M18 | `X 9.77..12.08, Y -4.61..3.08` | none (no result) | G 23.6 % | ![018](018.png) | [raw](018.skeleton.svg) · [lines](018.skeleton-lines.svg) |
| [019](019.txt) | P1-M18 | `X 10.93..12.08, Y -4.61..2.54` | none (no result) | F 23.9 % | ![019](019.png) | [raw](019.skeleton.svg) · [lines](019.skeleton-lines.svg) |
