# Initial canonical glyph filter

The first dataset selection uses canonical SMuFL glyph names directly. There are no project-specific aliases such as `bass`, `treble`, `dgt3`, or `flat`.

`selected-glyphs.txt` is the human-readable selection. `Program.cs` highlights the same names in `smufl-inventory.html` and writes `selected-glyphs.csv`.

Related glyph variants such as `flag8thUp`/`flag8thDown`, `articMarcatoAbove`/`articMarcatoBelow`, and `tremolo1`...`tremolo5` remain separate canonical SMuFL names at this stage. A later training/export layer may collapse them into one recognition target where appropriate without changing the source glyph identity.
