# Recognition inventory

## Structural inputs

- staves: **10**
- reusable/direct uses: **594**
- direct paths: **577**
- normalized line segments: **144**

## Semantic events

- notehead-half: **78**
- notehead-black: **74**
- clef-treble: **5**
- clef-bass: **5**
- rest-eighth: **4**
- rest-quarter: **2**

## Relation coverage

| Relation | Count | % of notes |
|---|---:|---:|
| stem attached | 152 | 100.0% |
| stem direction | 152 | 100.0% |
| chord members | 48 | 31.6% |
| notes touching beams | 32 | 21.1% |
| beam begin/continue/end | 32 | 21.1% |
| eighth notes | 29 | 19.1% |
| 16th notes | 10 | 6.6% |
| dotted notes | 71 | 46.7% |
| slur starts | 12 | 7.9% |
| slur stops | 12 | 7.9% |
| tie starts | 3 | 2.0% |
| tie stops | 3 | 2.0% |
| altered pitches | 26 | 17.1% |

## Missing-stem triage

Unattached noteheads: **0**
Normalized stem candidates: **105**
Broad raw vertical candidates: **108**
Raw vertical candidates not normalized: **9**

| Category | Count | Meaning |
|---|---:|---|
| normalization gap | 0 | stem-like raw path exists near the note, but no normalized line candidate does |
| attachment geometry gap | 0 | normalized stem is nearby, but current note↔stem endpoint/intersection tolerance rejects it |
| unexpected resolver gap | 0 | a normalized candidate satisfies the resolver's current attachment window but StemX is still empty |
| no stem geometry candidate | 0 | neither normalized nor broad raw vertical geometry was found near the note |

### Sample unattached notes

| category | symbol | staff | x | y | line dx sp | line y-gap sp | raw dx sp | raw y-gap sp | raw path |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|

## Warning groups

Total warnings: **139**

- low confidence: **137**
- Не удалось привязать точку в x=309.3: **1**
- Не удалось привязать точку в x=95.3: **1**

## High-value suspicious symbols near staves

These are frequent staff-local glyphs that are still semantically unknown or have low classification confidence.

| id | uses | kind | reference | score | width sp | height sp | reason |
|---|---:|---|---|---:|---:|---:|---|
| `T` | 16 | accidental-flat | uniE2DE | 0.555 | 0.644 | 2.487 | low confidence |
| `w` | 5 | clef-treble | uniE0F0 | 0.571 | 3.183 | 6.636 | low confidence |
| `x` | 5 | clef-bass | uniE06C | 0.509 | 2.208 | 2.266 | low confidence |
| `A` | 3 | smufl-unknown | uniE1C9 | 0.532 | 3.019 | 2.585 | unknown semantic kind |
| `ab` | 3 | smufl-unknown | uniE06C | 0.639 | 2.184 | 3.571 | unknown semantic kind |
| `z` | 2 | time-signature-digit | timeSig3 | 0.546 | 2.024 | 1.942 | low confidence |
| `F` | 2 | smufl-unknown | uniE1AF | 0.807 | 1.340 | 1.119 | unknown semantic kind |
| `V` | 2 | smufl-unknown | uniE110 | 0.689 | 3.334 | 1.965 | unknown semantic kind |
| `X` | 2 | smufl-unknown | uniE4D2 | 0.652 | 3.408 | 2.561 | unknown semantic kind |
| `ad` | 2 | accidental-flat | uniE2DE | 0.555 | 0.644 | 2.487 | low confidence |
| `E` | 1 | smufl-unknown | uniE127 | 0.818 | 1.466 | 1.612 | unknown semantic kind |
| `G` | 1 | smufl-unknown | uniE1C1 | 0.809 | 1.167 | 1.095 | unknown semantic kind |
| `H` | 1 | smufl-unknown | uniE27A | 0.690 | 0.714 | 1.466 | unknown semantic kind |
| `I` | 1 | smufl-unknown | uniE127 | 0.733 | 1.153 | 1.578 | unknown semantic kind |
| `J` | 1 | smufl-unknown | uniE0F0 | 0.761 | 0.575 | 1.528 | unknown semantic kind |
| `K` | 1 | smufl-unknown | uniE4A6 | 0.747 | 0.575 | 1.544 | unknown semantic kind |
| `L` | 1 | smufl-unknown | uniE5F5 | 0.786 | 0.891 | 1.129 | unknown semantic kind |
| `M` | 1 | <unclassified> |  |  |  |  | no classification |
| `N` | 1 | smufl-unknown | uniE4CE | 0.794 | 0.598 | 0.878 | unknown semantic kind |
| `Q` | 1 | smufl-unknown | uniE135 | 0.786 | 1.055 | 1.144 | unknown semantic kind |
| `S` | 1 | smufl-unknown | uniE59D | 0.668 | 0.633 | 1.075 | unknown semantic kind |
| `Z` | 1 | smufl-unknown | uniE251 | 0.732 | 1.095 | 2.718 | unknown semantic kind |
| `path:000000` | 1 | rest-1024th | rest1024th | 0.040 | 0.000 | 15.166 | low confidence |
| `path:000001` | 1 | smufl-unknown | uniE540 | 0.002 | 97.366 | 15.166 | unknown semantic kind |
| `path:000002` | 1 | smufl-unknown | uniE000 | 0.656 | 1.016 | 15.166 | unknown semantic kind |
| `path:000003` | 1 | smufl-unknown | uniE1FE | 0.119 | 18.832 | 15.916 | unknown semantic kind |
| `path:000004` | 1 | smufl-unknown | uniE090 | 0.100 | 1.791 | 0.000 | unknown semantic kind |
| `path:000005` | 1 | clef-bass | fClef | 0.100 | 0.000 | 3.583 | low confidence |
| `path:000006` | 1 | clef-bass | fClef | 0.100 | 0.000 | 3.583 | low confidence |
| `path:000007` | 1 | smufl-unknown | uniE2E0 | 0.660 | 5.771 | 1.283 | unknown semantic kind |
