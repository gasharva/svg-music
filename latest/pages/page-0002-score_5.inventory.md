# Recognition inventory

## Structural inputs

- staves: **8**
- reusable/direct uses: **453**
- direct paths: **452**
- normalized line segments: **143**

## Semantic events

- notehead-black: **92**
- notehead-half: **68**
- rest-quarter: **6**
- clef-treble: **5**
- clef-bass: **3**
- rest-eighth: **2**

## Relation coverage

| Relation | Count | % of notes |
|---|---:|---:|
| stem attached | 153 | 95.6% |
| stem direction | 153 | 95.6% |
| chord members | 42 | 26.2% |
| notes touching beams | 67 | 41.9% |
| beam begin/continue/end | 67 | 41.9% |
| eighth notes | 75 | 46.9% |
| 16th notes | 0 | 0.0% |
| dotted notes | 56 | 35.0% |
| slur starts | 21 | 13.1% |
| slur stops | 21 | 13.1% |
| tie starts | 15 | 9.4% |
| tie stops | 15 | 9.4% |
| altered pitches | 11 | 6.9% |

## Missing-stem triage

Unattached noteheads: **7**
Normalized stem candidates: **107**
Broad raw vertical candidates: **110**
Raw vertical candidates not normalized: **6**

| Category | Count | Meaning |
|---|---:|---|
| normalization gap | 7 | stem-like raw path exists near the note, but no normalized line candidate does |
| attachment geometry gap | 0 | normalized stem is nearby, but current note↔stem endpoint/intersection tolerance rejects it |
| unexpected resolver gap | 0 | a normalized candidate satisfies the resolver's current attachment window but StemX is still empty |
| no stem geometry candidate | 0 | neither normalized nor broad raw vertical geometry was found near the note |

### Sample unattached notes

| category | symbol | staff | x | y | line dx sp | line y-gap sp | raw dx sp | raw y-gap sp | raw path |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| normalizationGap | `g` | 1 | 152.51 | 194.08 |  |  | 0.608 | 0.000 | `path:000021#0` |
| normalizationGap | `g` | 1 | 152.51 | 184.56 |  |  | 0.608 | 0.000 | `path:000021#0` |
| normalizationGap | `g` | 1 | 246.30 | 194.08 |  |  | 0.608 | 0.000 | `path:000030#0` |
| normalizationGap | `g` | 1 | 246.30 | 184.56 |  |  | 0.608 | 0.000 | `path:000030#0` |
| normalizationGap | `g` | 3 | 309.56 | 324.55 |  |  | 0.547 | 0.000 | `path:000069#0` |
| normalizationGap | `g` | 5 | 83.51 | 476.44 |  |  | 0.610 | 0.000 | `path:000092#0` |
| normalizationGap | `g` | 5 | 83.51 | 466.93 |  |  | 0.610 | 0.000 | `path:000092#0` |

## Warning groups

Total warnings: **146**

- low confidence: **144**
- Не удалось привязать accidental-sharp в x=419.1: **1**
- Не удалось привязать точку в x=463.4: **1**

## High-value suspicious symbols near staves

These are frequent staff-local glyphs that are still semantically unknown or have low classification confidence.

| id | uses | kind | reference | score | width sp | height sp | reason |
|---|---:|---|---|---:|---:|---:|---|
| `s` | 6 | smufl-unknown | uniE251 | 0.735 | 1.077 | 2.449 | unknown semantic kind |
| `p` | 4 | clef-treble | uniE058 | 0.596 | 2.708 | 6.734 | low confidence |
| `C` | 2 | smufl-unknown | uniE0CF | 0.518 | 3.126 | 2.276 | unknown semantic kind |
| `b` | 1 | clef-treble | uniE058 | 0.596 | 2.708 | 6.734 | low confidence |
| `d` | 1 | smufl-unknown | uniE135 | 0.760 | 1.095 | 1.484 | unknown semantic kind |
| `k` | 1 | smufl-unknown | uniE00B | 0.451 | 0.364 | 1.389 | unknown semantic kind |
| `o` | 1 | smufl-unknown | uniE110 | 0.573 | 3.428 | 1.898 | unknown semantic kind |
| `u` | 1 | smufl-unknown | uniE045 | 0.468 | 4.669 | 1.767 | unknown semantic kind |
| `w` | 1 | <unclassified> |  |  |  |  | no classification |
| `x` | 1 | smufl-unknown | uniE4A6 | 0.724 | 0.611 | 1.221 | unknown semantic kind |
| `y` | 1 | smufl-unknown | uniE5F5 | 0.808 | 0.762 | 0.965 | unknown semantic kind |
| `z` | 1 | smufl-unknown | uniE1BD | 0.877 | 1.533 | 0.968 | unknown semantic kind |
| `B` | 1 | smufl-unknown | uniE113 | 0.735 | 0.742 | 0.962 | unknown semantic kind |
| `E` | 1 | smufl-unknown | uniE241 | 0.801 | 1.126 | 3.273 | unknown semantic kind |
| `F` | 1 | smufl-unknown | uniE047 | 0.544 | 2.139 | 4.505 | unknown semantic kind |
| `path:000000` | 1 | smufl-unknown | uniE11A | 0.520 | 121.604 | 142.755 | unknown semantic kind |
| `path:000001` | 1 | rest-1024th | rest1024th | 0.037 | 0.000 | 15.791 | low confidence |
| `path:000002` | 1 | smufl-unknown | uniE540 | 0.002 | 102.082 | 15.791 | unknown semantic kind |
| `path:000003` | 1 | smufl-unknown | uniE4B7 | 0.640 | 1.000 | 7.916 | unknown semantic kind |
| `path:000004` | 1 | smufl-unknown | uniE4B7 | 0.640 | 1.000 | 7.875 | unknown semantic kind |
| `path:000005` | 1 | smufl-unknown | uniE009 | 0.005 | 53.749 | 0.000 | unknown semantic kind |
| `path:000006` | 1 | smufl-unknown | uniE009 | 0.012 | 51.583 | 24.208 | unknown semantic kind |
| `path:000007` | 1 | smufl-unknown | uniE1FE | 0.205 | 28.333 | 14.958 | unknown semantic kind |
| `path:000008` | 1 | rest-1024th | rest1024th | 0.037 | 0.000 | 15.791 | low confidence |
| `path:000009` | 1 | rest-1024th | rest1024th | 0.037 | 0.000 | 15.791 | low confidence |
| `path:000010` | 1 | rest-1024th | rest1024th | 0.037 | 0.000 | 15.791 | low confidence |
| `path:000011` | 1 | rest-1024th | rest1024th | 0.037 | 0.000 | 15.791 | low confidence |
| `path:000012` | 1 | rest-1024th | rest1024th | 0.037 | 0.000 | 15.791 | low confidence |
| `path:000013` | 1 | rest-1024th | rest1024th | 0.037 | 0.000 | 15.791 | low confidence |
| `path:000014` | 1 | smufl-unknown | uniE220 | 0.900 | 29.895 | 0.750 | unknown semantic kind |
