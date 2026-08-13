# Recognition inventory

## Structural inputs

- staves: **8**
- reusable/direct uses: **339**
- direct paths: **338**
- normalized line segments: **107**

## Semantic events

- notehead-half: **57**
- notehead-black: **53**
- rest-eighth: **6**
- clef-bass: **4**
- clef-treble: **4**

## Relation coverage

| Relation | Count | % of notes |
|---|---:|---:|
| stem attached | 110 | 100.0% |
| stem direction | 110 | 100.0% |
| chord members | 29 | 26.4% |
| notes touching beams | 22 | 20.0% |
| beam begin/continue/end | 22 | 20.0% |
| eighth notes | 20 | 18.2% |
| 16th notes | 5 | 4.5% |
| dotted notes | 51 | 46.4% |
| slur starts | 8 | 7.3% |
| slur stops | 8 | 7.3% |
| tie starts | 2 | 1.8% |
| tie stops | 2 | 1.8% |
| altered pitches | 17 | 15.5% |

## Missing-stem triage

Unattached noteheads: **0**
Normalized stem candidates: **81**
Broad raw vertical candidates: **82**
Raw vertical candidates not normalized: **4**

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

Total warnings: **109**

- low confidence: **103**
- Не удалось привязать accidental-sharp в x=105.6: **1**
- Не удалось привязать accidental-sharp в x=99.7: **1**
- Recovered clef-treble at staff 0 from staff-left geometry (score 0.409).: **1**
- Recovered clef-treble at staff 2 from staff-left geometry (score 0.409).: **1**
- Recovered clef-treble at staff 4 from staff-left geometry (score 0.409).: **1**
- Recovered clef-treble at staff 6 from staff-left geometry (score 0.409).: **1**

## High-value suspicious symbols near staves

These are frequent staff-local glyphs that are still semantically unknown or have low classification confidence.

| id | uses | kind | reference | score | width sp | height sp | reason |
|---|---:|---|---|---:|---:|---:|---|
| `c` | 4 | clef-treble | uniE053 | 0.409 | 3.281 | 6.519 | low confidence |
| `d` | 4 | clef-bass | uniE500 | 0.483 | 2.674 | 2.714 | low confidence |
| `s` | 3 | smufl-unknown | uniE241 | 0.583 | 0.978 | 3.145 | unknown semantic kind |
| `u` | 3 | smufl-unknown | uniE1C1 | 0.796 | 0.980 | 1.187 | unknown semantic kind |
| `e` | 2 | smufl-unknown | uniE110 | 0.460 | 3.092 | 2.608 | unknown semantic kind |
| `t` | 2 | smufl-unknown | uniE5E2 | 0.680 | 1.335 | 2.646 | unknown semantic kind |
| `v` | 2 | smufl-unknown | uniE4AA | 0.922 | 0.279 | 1.438 | unknown semantic kind |
| `o` | 1 | smufl-unknown | uniE06C | 0.538 | 2.092 | 2.815 | unknown semantic kind |
| `q` | 1 | smufl-unknown | uniE1BF | 0.526 | 3.493 | 1.552 | unknown semantic kind |
| `r` | 1 | smufl-unknown | uniE4D2 | 0.693 | 3.303 | 1.878 | unknown semantic kind |
| `w` | 1 | smufl-unknown | uniE0C0 | 0.751 | 0.616 | 1.091 | unknown semantic kind |
| `x` | 1 | smufl-unknown | uniE127 | 0.841 | 1.194 | 1.527 | unknown semantic kind |
| `y` | 1 | smufl-unknown | uniE130 | 0.816 | 0.963 | 1.091 | unknown semantic kind |
| `z` | 1 | smufl-unknown | uniE137 | 0.745 | 0.767 | 1.472 | unknown semantic kind |
| `A` | 1 | smufl-unknown | uniE1D2 | 0.841 | 1.087 | 1.135 | unknown semantic kind |
| `B` | 1 | smufl-unknown | uniE5BB | 0.489 | 5.257 | 3.093 | unknown semantic kind |
| `path:000000` | 1 | rest-1024th | rest1024th | 0.037 | 0.000 | 15.791 | low confidence |
| `path:000001` | 1 | smufl-unknown | uniE540 | 0.002 | 101.874 | 15.792 | unknown semantic kind |
| `path:000002` | 1 | smufl-unknown | uniE4B7 | 0.640 | 1.017 | 7.918 | unknown semantic kind |
| `path:000003` | 1 | smufl-unknown | uniE4B7 | 0.641 | 1.017 | 7.875 | unknown semantic kind |
| `path:000004` | 1 | smufl-unknown | uniE5C3 | 0.105 | 21.084 | 0.959 | unknown semantic kind |
| `path:000005` | 1 | smufl-unknown | uniE07C | 0.100 | 0.000 | 2.334 | unknown semantic kind |
| `path:000006` | 1 | smufl-unknown | uniE07C | 0.100 | 0.000 | 2.334 | unknown semantic kind |
| `path:000007` | 1 | smufl-unknown | uniE07C | 0.100 | 0.000 | 2.334 | unknown semantic kind |
| `path:000008` | 1 | rest-1024th | rest1024th | 0.037 | 0.000 | 15.791 | low confidence |
| `path:000009` | 1 | smufl-unknown | uniE002 | 0.187 | 1.792 | 6.333 | unknown semantic kind |
| `path:000010` | 1 | smufl-unknown | uniE07C | 0.100 | 0.000 | 2.334 | unknown semantic kind |
| `path:000011` | 1 | smufl-unknown | uniE07C | 0.100 | 0.000 | 2.334 | unknown semantic kind |
| `path:000012` | 1 | smufl-unknown | uniE003 | 0.628 | 5.938 | 1.751 | unknown semantic kind |
| `path:000013` | 1 | smufl-unknown | uniE07C | 0.100 | 0.000 | 2.334 | unknown semantic kind |
