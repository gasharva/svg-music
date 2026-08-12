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
| altered pitches | 25 | 16.4% |

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

Total warnings: **164**

- low confidence: **137**
- Не удалось привязать точку в x=309.3: **1**
- Не удалось привязать точку в x=95.3: **1**
- acc-probe #T staff=0 x=212.8 y=163.7 row=4 -> A4 x=220.6 y=166.0 row=3 pd=1 dy=2.30 dx=7.82: **1**
- acc-probe #T staff=0 x=317.5 y=163.7 row=4 -> A4 x=325.3 y=166.0 row=3 pd=1 dy=2.30 dx=7.82: **1**
- acc-probe #U staff=0 x=316.9 y=177.5 row=-2 -> C4 x=326.0 y=177.5 row=-2 pd=0 dy=0.00 dx=9.06: **1**
- acc-probe #U staff=2 x=77.1 y=307.5 row=1 -> F4 x=85.5 y=307.5 row=1 pd=0 dy=0.00 dx=8.39: **1**
- acc-probe #U staff=2 x=139.7 y=307.5 row=1 -> F4 x=148.1 y=307.5 row=1 pd=0 dy=0.00 dx=8.39: **1**
- acc-probe #T staff=2 x=211.7 y=300.6 row=4 -> B4 x=219.5 y=300.6 row=4 pd=0 dy=0.00 dx=7.82: **1**
- acc-probe #T staff=3 x=200.4 y=340.4 row=9 -> A3 x=208.2 y=342.7 row=8 pd=1 dy=2.30 dx=7.82: **1**
- acc-probe #T staff=3 x=296.4 y=340.4 row=9 -> B3 x=304.9 y=340.4 row=9 pd=0 dy=0.00 dx=8.49: **1**
- acc-probe #T staff=3 x=477.1 y=340.4 row=9 -> B3 x=484.9 y=340.4 row=9 pd=0 dy=0.00 dx=7.82: **1**
- acc-probe #T staff=4 x=198.6 y=437.4 row=4 -> A4 x=206.5 y=439.7 row=3 pd=1 dy=2.30 dx=7.82: **1**
- acc-probe #T staff=4 x=308.1 y=437.4 row=4 -> A4 x=315.9 y=439.7 row=3 pd=1 dy=2.30 dx=7.82: **1**
- acc-probe #U staff=4 x=307.5 y=451.2 row=-2 -> C4 x=316.6 y=451.2 row=-2 pd=0 dy=0.00 dx=9.06: **1**
- acc-probe #U staff=6 x=77.1 y=581.2 row=1 -> F4 x=85.5 y=581.2 row=1 pd=0 dy=0.00 dx=8.39: **1**
- acc-probe #U staff=6 x=143.5 y=581.2 row=1 -> F4 x=151.9 y=581.2 row=1 pd=0 dy=0.00 dx=8.38: **1**
- acc-probe #T staff=6 x=212.8 y=574.3 row=4 -> B4 x=220.6 y=574.3 row=4 pd=0 dy=0.00 dx=7.82: **1**
- acc-probe #T staff=6 x=295.5 y=574.3 row=4 -> B4 x=303.3 y=574.3 row=4 pd=0 dy=0.00 dx=7.82: **1**
- acc-probe #T staff=7 x=295.5 y=614.1 row=9 -> B3 x=304.0 y=614.1 row=9 pd=0 dy=0.00 dx=8.49: **1**
- acc-probe #T staff=8 x=77.1 y=720.3 row=0 -> E4 x=85.6 y=720.3 row=0 pd=0 dy=0.00 dx=8.49: **1**
- acc-probe #T staff=9 x=82.5 y=750.9 row=9 -> B3 x=91.0 y=750.9 row=9 pd=0 dy=0.00 dx=8.49: **1**
- acc-probe #T staff=8 x=197.5 y=720.3 row=0 -> E4 x=206.0 y=720.3 row=0 pd=0 dy=0.00 dx=8.47: **1**
- acc-probe #T staff=9 x=202.8 y=760.1 row=5 -> E3 x=211.3 y=760.1 row=5 pd=0 dy=0.00 dx=8.49: **1**
- acc-probe #T staff=8 x=318.6 y=704.2 row=7 -> E5 x=326.5 y=704.2 row=7 pd=0 dy=0.00 dx=7.82: **1**
- acc-probe #ad staff=8 x=423.3 y=711.1 row=4 -> B4 x=431.8 y=711.1 row=4 pd=0 dy=0.00 dx=8.49: **1**
- acc-probe #ad staff=8 x=423.3 y=727.2 row=-3 -> B3 x=431.8 y=727.2 row=-3 pd=0 dy=0.00 dx=8.49: **1**
- acc-probe #U staff=9 x=454.0 y=757.8 row=6 -> F3 x=462.3 y=757.8 row=6 pd=0 dy=0.00 dx=8.39: **1**

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
