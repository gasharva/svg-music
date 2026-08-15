# Clef recognizer inputs

These are the exact post-sanity-filter vector candidates sent to `IClefRecognizer`.

`Legacy IoU` is the old shape-matching baseline: bbox-normalized 64x64 binary-mask IoU plus Clipper2 vector IoU. No size or staff-position prior is used.

| Candidate | P+M | Logical bbox | Vector recognizer | Legacy IoU | Shape |
|---|---|---|---|---|---|
| [001](001.txt) | P1-M1 | `X 1.1..3.78, Y -3.27..11.5` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 88.5 % | ![001](001.png) |
| [002](002.txt) | P2-M1 | `X 1.12..3.32, Y 0.01..6.66` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 29.2 % | ![002](002.png) |
| [003](003.txt) | P1-M2 | `X 7.82..8.72, Y -5.69..0.07` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 32.5 % | ![003](003.png) |
| [004](004.txt) | P2-M3 | `X 5.79..7, Y 7.1..13.65` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 24.3 % | ![004](004.png) |
| [005](005.txt) | P1-M4 | `X 1.16..4.01, Y -3.27..11.5` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 88.5 % | ![005](005.png) |
| [006](006.txt) | P2-M4 | `X 1.19..3.52, Y 0.01..6.66` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 29.2 % | ![006](006.png) |
| [007](007.txt) | P1-M6 | `X 1.92..2.8, Y -5.69..0.07` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 32.5 % | ![007](007.png) |
| [008](008.txt) | P2-M6 | `X 16.99..17.87, Y -5.7..0.07` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 32.5 % | ![008](008.png) |
| [009](009.txt) | P1-M7 | `X 1.01..3.48, Y -3.27..11.5` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 88.5 % | ![009](009.png) |
| [010](010.txt) | P1-M7 | `X 19.27..20.47, Y 8.3..14.07` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 32.5 % | ![010](010.png) |
| [011](011.txt) | P1-M7 | `X 21.2..22.22, Y 8.1..14.64` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 24.2 % | ![011](011.png) |
| [012](012.txt) | P2-M7 | `X 1.03..3.06, Y 0.01..6.66` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | F 29.2 % | ![012](012.png) |
| [013](013.txt) | P2-M9 | `X 29.55..31.52, Y -0.95..10.13` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 88.4 % | ![013](013.png) |
| [014](014.txt) | P1-M10 | `X 0.51..1.76, Y -3.27..11.5` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 88.5 % | ![014](014.png) |
| [015](015.txt) | P2-M10 | `X 0.51..1.76, Y -3.27..11.5` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 88.5 % | ![015](015.png) |
| [016](016.txt) | P1-M12 | `X 0.18..0.62, Y -3.27..11.5` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 88.5 % | ![016](016.png) |
| [017](017.txt) | P2-M12 | `X 0.18..0.62, Y -3.27..11.5` | none (Open-set rejection: nearest/ref=0.918 (max 0.72), margin/ref=0.541 (min 0.2).) | G 88.5 % | ![017](017.png) |
