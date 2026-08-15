# Clef recognizer inputs

These are the exact post-sanity-filter vector candidates sent to `IClefRecognizer`.

`Legacy IoU` is the old shape-matching baseline: bbox-normalized 64x64 binary-mask IoU plus Clipper2 vector IoU. No size or staff-position prior is used.

`Skeleton` is an experimental vector-only scanline midpoint skeleton. It is diagnostic only: no smoothing and no recognition yet.

| Candidate | P+M | Logical bbox | Vector recognizer | Legacy IoU | Shape | Skeleton |
|---|---|---|---|---|---|---|
| [001](001.txt) | P1-M1 | `X 0.99..3.42, Y -3.27..11.5` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 88.5 % | ![001](001.png) | [skeleton](001.skeleton.svg) |
| [002](002.txt) | P2-M1 | `X 1.01..3.01, Y 0.01..6.66` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 29.2 % | ![002](002.png) | [skeleton](002.skeleton.svg) |
| [003](003.txt) | P1-M5 | `X 0.9..3.11, Y -3.27..11.5` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 88.5 % | ![003](003.png) | [skeleton](003.skeleton.svg) |
| [004](004.txt) | P2-M5 | `X 0.92..2.73, Y 0.01..6.66` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 29.2 % | ![004](004.png) | [skeleton](004.skeleton.svg) |
| [005](005.txt) | P1-M7 | `X 3.38..4.68, Y -3.69..2.07` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 32.5 % | ![005](005.png) | [skeleton](005.skeleton.svg) |
| [006](006.txt) | P1-M9 | `X 0.99..3.42, Y -3.27..11.5` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 88.5 % | ![006](006.png) | [skeleton](006.skeleton.svg) |
| [007](007.txt) | P2-M9 | `X 1.02..3.01, Y 0.01..6.66` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 29.2 % | ![007](007.png) | [skeleton](007.skeleton.svg) |
