# Real meter digits

Curated vector digits extracted from the exact contours that `MeterResolver` sent to `ISvgNumberRecognizer` in the step-by-step pipeline.

These are deliberately **test inputs, not reference/training glyphs**. The expected digit is encoded in the file name:

- `Real-4-accusation-001.svg` — 4
- `Real-4-accusation-016.svg` — 4
- `Real-3-accusation-005.svg` — 3
- `Real-3-accusation-015.svg` — 3
- `Real-2-accusation-019.svg` — 2
- `Real-2-accusation-021.svg` — 2

They were exported from resolved primitive contours; no crop or re-read of the source score SVG is involved.
