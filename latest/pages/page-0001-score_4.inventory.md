# Recognition inventory

## Structural inputs

- staves: **10**
- reusable/direct uses: **840**
- direct paths: **816**
- normalized line segments: **193**

## Semantic events

- notehead-black: **178**
- notehead-half: **75**
- clef-bass: **5**
- rest-eighth: **3**
- rest-quarter: **3**
- rest-double-whole: **1**

## Relation coverage

| Relation | Count | % of notes |
|---|---:|---:|
| stem attached | 178 | 70.4% |
| stem direction | 178 | 70.4% |
| chord members | 40 | 15.8% |
| notes touching beams | 72 | 28.5% |
| beam begin/continue/end | 72 | 28.5% |
| eighth notes | 67 | 26.5% |
| 16th notes | 16 | 6.3% |
| dotted notes | 62 | 24.5% |
| slur starts | 33 | 13.0% |
| slur stops | 33 | 13.0% |
| tie starts | 16 | 6.3% |
| tie stops | 16 | 6.3% |
| altered pitches | 41 | 16.2% |

## Missing-stem triage

Unattached noteheads: **75**
Normalized stem candidates: **120**
Broad raw vertical candidates: **131**
Raw vertical candidates not normalized: **12**

| Category | Count | Meaning |
|---|---:|---|
| normalization gap | 2 | stem-like raw path exists near the note, but no normalized line candidate does |
| attachment geometry gap | 0 | normalized stem is nearby, but current note↔stem endpoint/intersection tolerance rejects it |
| unexpected resolver gap | 0 | a normalized candidate satisfies the resolver's current attachment window but StemX is still empty |
| no stem geometry candidate | 73 | neither normalized nor broad raw vertical geometry was found near the note |

### Sample unattached notes

| category | symbol | staff | x | y | line dx sp | line y-gap sp | raw dx sp | raw y-gap sp | raw path |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| noStemGeometryCandidate | `j` | 2 | 273.06 | 271.13 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 291.80 | 282.57 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 291.80 | 278.00 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 310.11 | 282.57 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 310.11 | 278.00 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 332.23 | 280.29 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 332.23 | 275.71 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 351.87 | 280.29 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 351.87 | 275.71 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 372.84 | 278.00 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 372.84 | 273.42 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 409.07 | 278.00 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 409.07 | 273.42 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 430.81 | 278.00 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 430.81 | 273.42 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 435.95 | 275.71 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 435.95 | 271.13 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 452.54 | 278.00 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 452.55 | 273.42 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 457.69 | 275.71 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 457.69 | 271.13 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 474.28 | 278.00 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 474.28 | 273.42 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 474.28 | 268.84 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 479.42 | 275.71 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 479.42 | 271.13 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 495.83 | 278.00 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 495.83 | 273.42 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 495.83 | 268.84 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 2 | 500.97 | 275.71 |  |  |  |  | `` |

## Warning groups

Total warnings: **197**

- low confidence: **188**
- Не удалось привязать accidental-sharp в x=176.9: **1**
- Не удалось привязать accidental-sharp в x=316.9: **1**
- Не удалось привязать accidental-sharp в x=394.3: **1**
- Не удалось привязать accidental-flat в x=287.0: **1**
- Не удалось привязать accidental-sharp в x=189.1: **1**
- Не удалось привязать accidental-sharp в x=457.2: **1**
- Не удалось привязать точку в x=236.0: **1**
- Не удалось привязать точку в x=382.9: **1**
- Не удалось привязать точку в x=481.8: **1**

## High-value suspicious symbols near staves

These are frequent staff-local glyphs that are still semantically unknown or have low classification confidence.

| id | uses | kind | reference | score | width sp | height sp | reason |
|---|---:|---|---|---:|---:|---:|---|
| `A` | 7 | smufl-unknown | uniE241 | 0.753 | 1.014 | 3.127 | unknown semantic kind |
| `x` | 4 | smufl-unknown | uniE051 | 0.421 | 2.947 | 8.343 | unknown semantic kind |
| `E` | 4 | smufl-unknown | uniE113 | 0.805 | 0.744 | 0.966 | unknown semantic kind |
| `z` | 4 | smufl-unknown | uniE517 | 0.479 | 3.124 | 1.980 | unknown semantic kind |
| `p` | 3 | <unclassified> |  |  |  |  | no classification |
| `u` | 3 | smufl-unknown | uniE4D2 | 0.666 | 3.346 | 1.765 | unknown semantic kind |
| `F` | 3 | smufl-unknown | uniE113 | 0.808 | 0.843 | 0.966 | unknown semantic kind |
| `I` | 3 | smufl-unknown | uniE581 | 0.787 | 0.761 | 0.932 | unknown semantic kind |
| `m` | 2 | smufl-unknown | uniE1C1 | 0.825 | 0.819 | 1.035 | unknown semantic kind |
| `o` | 2 | smufl-unknown | uniE113 | 0.894 | 0.994 | 1.123 | unknown semantic kind |
| `D` | 2 | smufl-unknown | uniE5F6 | 0.694 | 0.857 | 1.325 | unknown semantic kind |
| `G` | 2 | <unclassified> |  |  |  |  | no classification |
| `bm` | 2 | smufl-unknown | uniE594 | 0.618 | 0.546 | 1.540 | unknown semantic kind |
| `bo` | 2 | smufl-unknown | uniE1C1 | 0.795 | 1.137 | 1.239 | unknown semantic kind |
| `bz` | 2 | smufl-unknown | uniE12F | 0.685 | 1.052 | 1.704 | unknown semantic kind |
| `b` | 1 | smufl-unknown | uniE051 | 0.421 | 2.947 | 8.343 | unknown semantic kind |
| `k` | 1 | smufl-unknown | uniE127 | 0.751 | 1.291 | 1.495 | unknown semantic kind |
| `l` | 1 | smufl-unknown | uniE1C1 | 0.801 | 1.222 | 1.100 | unknown semantic kind |
| `n` | 1 | smufl-unknown | uniE139 | 0.739 | 0.751 | 1.451 | unknown semantic kind |
| `q` | 1 | smufl-unknown | uniE562 | 0.799 | 1.581 | 2.977 | unknown semantic kind |
| `t` | 1 | smufl-unknown | uniE096 | 0.679 | 0.936 | 1.598 | unknown semantic kind |
| `K` | 1 | smufl-unknown | uniE1C3 | 0.773 | 0.690 | 0.879 | unknown semantic kind |
| `L` | 1 | smufl-unknown | uniE1C1 | 0.769 | 0.850 | 1.021 | unknown semantic kind |
| `M` | 1 | smufl-unknown | uniE127 | 0.738 | 0.959 | 1.349 | unknown semantic kind |
| `N` | 1 | smufl-unknown | uniE504 | 0.380 | 3.497 | 3.407 | unknown semantic kind |
| `P` | 1 | smufl-unknown | uniE4A6 | 0.718 | 0.611 | 1.253 | unknown semantic kind |
| `Q` | 1 | smufl-unknown | uniE1BD | 0.856 | 1.533 | 1.021 | unknown semantic kind |
| `S` | 1 | smufl-unknown | uniE243 | 0.640 | 1.208 | 3.568 | unknown semantic kind |
| `T` | 1 | smufl-unknown | uniE137 | 0.799 | 1.210 | 1.390 | unknown semantic kind |
| `bn` | 1 | smufl-unknown | uniE0F4 | 0.673 | 1.329 | 1.711 | unknown semantic kind |
