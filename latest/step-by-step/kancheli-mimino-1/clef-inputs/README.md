# Clef recognizer inputs

These are the exact post-sanity-filter vector candidates sent to `IClefRecognizer`.

`Legacy IoU` is the old shape-matching baseline: bbox-normalized 64x64 binary-mask IoU plus Clipper2 vector IoU. No size or staff-position prior is used.

`Skeleton` is an experimental vector-only scanline midpoint skeleton. It is diagnostic only: no smoothing and no recognition yet.

| Candidate | P+M | Logical bbox | Vector recognizer | Legacy IoU | Shape | Skeleton |
|---|---|---|---|---|---|---|
| [001](001.txt) | P1-M1 | `X 0.91..3.19, Y -3.36..11.53` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 89.0 % | ![001](001.png) | [skeleton](001.skeleton.svg) |
| [002](002.txt) | P2-M1 | `X 0.93..2.8, Y 0.01..6.69` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 29.1 % | ![002](002.png) | [skeleton](002.skeleton.svg) |
| [003](003.txt) | P1-M5 | `X 0.98..3.42, Y -3.36..11.53` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 89.0 % | ![003](003.png) | [skeleton](003.skeleton.svg) |
| [004](004.txt) | P2-M5 | `X 1..3, Y 0.01..6.69` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 29.1 % | ![004](004.png) | [skeleton](004.skeleton.svg) |
| [005](005.txt) | P1-M9 | `X 0.86..3.02, Y -3.36..11.52` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 89.0 % | ![005](005.png) | [skeleton](005.skeleton.svg) |
| [006](006.txt) | P1-M9 | `X 11.02..11.96, Y -0.78..6.36` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 24.3 % | ![006](006.png) | [skeleton](006.skeleton.svg) |
| [007](007.txt) | P2-M9 | `X 0.88..2.65, Y 0..6.69` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 29.1 % | ![007](007.png) | [skeleton](007.skeleton.svg) |
| [008](008.txt) | P1-M13 | `X 0.93..3.27, Y -3.36..11.52` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 89.0 % | ![008](008.png) | [skeleton](008.skeleton.svg) |
| [009](009.txt) | P2-M13 | `X 0.95..2.87, Y 0..6.69` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 29.1 % | ![009](009.png) | [skeleton](009.skeleton.svg) |
| [010](010.txt) | P1-M17 | `X 0.87..3.05, Y -3.36..11.52` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 89.0 % | ![010](010.png) | [skeleton](010.skeleton.svg) |
| [011](011.txt) | P1-M17 | `X 12.01..12.95, Y -5.79..1.35` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 24.3 % | ![011](011.png) | [skeleton](011.skeleton.svg) |
| [012](012.txt) | P2-M17 | `X 0.89..2.67, Y 0..6.69` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 29.1 % | ![012](012.png) | [skeleton](012.skeleton.svg) |
| [013](013.txt) | P1-M18 | `X 10.24..11.32, Y -5.79..1.35` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 24.3 % | ![013](013.png) | [skeleton](013.skeleton.svg) |
