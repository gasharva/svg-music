# Recognition inventory

## Structural inputs

- staves: **10**
- reusable/direct uses: **840**
- direct paths: **816**
- normalized line segments: **190**

## Semantic events

- notehead-black: **178**
- notehead-half: **36**
- rest-quarter: **3**
- rest-eighth: **2**
- clef-bass: **2**
- rest-double-whole: **1**

## Relation coverage

| Relation | Count | % of notes |
|---|---:|---:|
| stem attached | 6 | 2.8% |
| stem direction | 6 | 2.8% |
| chord members | 1 | 0.5% |
| notes touching beams | 0 | 0.0% |
| beam begin/continue/end | 0 | 0.0% |
| eighth notes | 4 | 1.9% |
| 16th notes | 0 | 0.0% |
| dotted notes | 30 | 14.0% |
| slur starts | 2 | 0.9% |
| slur stops | 2 | 0.9% |
| tie starts | 0 | 0.0% |
| tie stops | 0 | 0.0% |
| altered pitches | 3 | 1.4% |

## Missing-stem triage

Unattached noteheads: **208**
Normalized stem candidates: **120**
Broad raw vertical candidates: **131**
Raw vertical candidates not normalized: **12**

| Category | Count | Meaning |
|---|---:|---|
| normalization gap | 3 | stem-like raw path exists near the note, but no normalized line candidate does |
| attachment geometry gap | 0 | normalized stem is nearby, but current note↔stem endpoint/intersection tolerance rejects it |
| unexpected resolver gap | 0 | a normalized candidate satisfies the resolver's current attachment window but StemX is still empty |
| no stem geometry candidate | 205 | neither normalized nor broad raw vertical geometry was found near the note |

### Sample unattached notes

| category | symbol | staff | x | y | line dx sp | line y-gap sp | raw dx sp | raw y-gap sp | raw path |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| noStemGeometryCandidate | `h` | 9 | 125.39 | 934.57 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 125.39 | 846.58 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 3 | 131.84 | 349.58 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 147.46 | 922.68 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 170.51 | 910.78 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 170.51 | 843.60 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 3 | 171.25 | 346.60 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 3 | 189.84 | 346.60 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 195.30 | 907.80 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 3 | 208.19 | 343.63 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 225.79 | 843.60 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 8 | 238.92 | 685.70 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 245.37 | 846.58 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 3 | 249.09 | 343.63 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 264.95 | 846.58 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 264.96 | 755.85 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 271.64 | 752.88 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 284.29 | 849.55 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 3 | 287.01 | 346.60 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 3 | 304.62 | 346.60 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 311.31 | 849.55 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 311.31 | 940.52 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 3 | 322.46 | 349.58 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 332.87 | 928.63 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 354.68 | 916.73 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 354.68 | 852.53 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 8 | 356.92 | 685.70 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 9 | 377.49 | 913.75 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 4 | 379.22 | 367.42 |  |  |  |  | `` |
| noStemGeometryCandidate | `h` | 4 | 379.22 | 361.48 |  |  |  |  | `` |

## Warning groups

Total warnings: **230**

- low confidence: **188**
- Не удалось привязать accidental-flat в x=142.0: **1**
- Не удалось привязать accidental-flat в x=246.5: **1**
- Не удалось привязать accidental-flat в x=223.3: **1**
- Не удалось привязать accidental-flat в x=457.0: **1**
- Не удалось привязать accidental-natural в x=457.2: **1**
- Не удалось привязать accidental-flat в x=152.0: **1**
- Не удалось привязать accidental-flat в x=93.2: **1**
- Не удалось привязать accidental-flat в x=183.4: **1**
- Не удалось привязать accidental-sharp в x=182.8: **1**
- Не удалось привязать accidental-sharp в x=176.9: **1**
- Не удалось привязать accidental-sharp в x=283.0: **1**
- Не удалось привязать accidental-sharp в x=322.8: **1**
- Не удалось привязать accidental-sharp в x=316.9: **1**
- Не удалось привязать accidental-sharp в x=364.0: **1**
- Не удалось привязать accidental-sharp в x=264.1: **1**
- Не удалось привязать accidental-sharp в x=400.2: **1**
- Не удалось привязать accidental-sharp в x=394.3: **1**
- Не удалось привязать accidental-sharp в x=422.0: **1**
- Не удалось привязать accidental-sharp в x=465.4: **1**
- Не удалось привязать accidental-sharp в x=508.3: **1**
- Не удалось привязать accidental-flat в x=98.4: **1**
- Не удалось привязать accidental-natural в x=93.2: **1**
- Не удалось привязать accidental-flat в x=132.7: **1**
- Не удалось привязать accidental-natural в x=98.6: **1**
- Не удалось привязать accidental-flat в x=199.0: **1**
- Не удалось привязать accidental-natural в x=292.5: **1**
- Не удалось привязать accidental-flat в x=287.0: **1**
- Не удалось привязать accidental-sharp в x=488.9: **1**
- Не удалось привязать accidental-natural в x=503.8: **1**
- Не удалось привязать accidental-sharp в x=175.0: **1**
- Не удалось привязать accidental-sharp в x=195.1: **1**
- Не удалось привязать accidental-sharp в x=189.1: **1**
- Не удалось привязать accidental-sharp в x=296.1: **1**
- Не удалось привязать accidental-sharp в x=409.6: **1**
- Не удалось привязать accidental-sharp в x=364.2: **1**
- Не удалось привязать accidental-natural в x=443.5: **1**
- Не удалось привязать accidental-sharp в x=463.1: **1**
- Не удалось привязать accidental-sharp в x=457.2: **1**
- Не удалось привязать точку в x=307.1: **1**
- Не удалось привязать точку в x=498.0: **1**
- Не удалось привязать точку в x=255.3: **1**
- Не удалось привязать точку в x=132.3: **1**

## High-value suspicious symbols near staves

These are frequent staff-local glyphs that are still semantically unknown or have low classification confidence.

| id | uses | kind | reference | score | width sp | height sp | reason |
|---|---:|---|---|---:|---:|---:|---|
| `A` | 5 | smufl-unknown | uniE241 | 0.753 | 1.014 | 3.127 | unknown semantic kind |
| `E` | 4 | smufl-unknown | uniE113 | 0.805 | 0.744 | 0.966 | unknown semantic kind |
| `p` | 3 | <unclassified> |  |  |  |  | no classification |
| `u` | 3 | smufl-unknown | uniE4D2 | 0.666 | 3.346 | 1.765 | unknown semantic kind |
| `x` | 3 | smufl-unknown | uniE051 | 0.421 | 2.947 | 8.343 | unknown semantic kind |
| `F` | 3 | smufl-unknown | uniE113 | 0.808 | 0.843 | 0.966 | unknown semantic kind |
| `I` | 3 | smufl-unknown | uniE581 | 0.787 | 0.761 | 0.932 | unknown semantic kind |
| `m` | 2 | smufl-unknown | uniE1C1 | 0.825 | 0.819 | 1.035 | unknown semantic kind |
| `o` | 2 | smufl-unknown | uniE113 | 0.894 | 0.994 | 1.123 | unknown semantic kind |
| `z` | 2 | smufl-unknown | uniE517 | 0.479 | 3.124 | 1.980 | unknown semantic kind |
| `D` | 2 | smufl-unknown | uniE5F6 | 0.694 | 0.857 | 1.325 | unknown semantic kind |
| `G` | 2 | <unclassified> |  |  |  |  | no classification |
| `aY` | 2 | time-signature-digit | timeSig9 | 0.582 | 1.044 | 1.895 | low confidence |
| `bb` | 2 | smufl-unknown | uniE523 | 0.620 | 1.117 | 1.189 | unknown semantic kind |
| `aM` | 2 | smufl-unknown | uniE1A0 | 0.808 | 1.031 | 1.205 | unknown semantic kind |
| `aH` | 2 | smufl-unknown | uniE19D | 0.690 | 0.905 | 1.205 | unknown semantic kind |
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
| `aW` | 1 | smufl-unknown | uniE520 | 0.605 | 1.448 | 1.700 | unknown semantic kind |
| `aZ` | 1 | smufl-unknown | uniE595 | 0.404 | 1.332 | 1.875 | unknown semantic kind |
