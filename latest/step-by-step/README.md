# SvgStructure step-by-step

**Step 1 — PartMeasureResolver:** SVG → parts + measures + logical/physical coordinate map.  
**Step 2 — PrimitiveResolver:** primitives → Pn-Mm, measure-only, or physical-only ownership.  
Overlays are diagnostics only.

| SVG | Step 1 | Step 2 | Resolved data |
|---|---|---|---|
| **[kancheli-accusation.svg](kancheli-accusation/source.svg)** | [![part-measure map](kancheli-accusation/measures.png)](kancheli-accusation/measures.png) | [![primitive ownership](kancheli-accusation/classified.png)](kancheli-accusation/classified.png) | parts=2<br>measures=12<br>P+M=713<br>M-only=0<br>physical=387<br>[json](kancheli-accusation/structure.json) |
| **[kancheli-king-lear.svg](kancheli-king-lear/source.svg)** | [![part-measure map](kancheli-king-lear/measures.png)](kancheli-king-lear/measures.png) | [![primitive ownership](kancheli-king-lear/classified.png)](kancheli-king-lear/classified.png) | parts=2<br>measures=13<br>P+M=458<br>M-only=1<br>physical=280<br>[json](kancheli-king-lear/structure.json) |
| **[kancheli-mimino-1.svg](kancheli-mimino-1/source.svg)** | [![part-measure map](kancheli-mimino-1/measures.png)](kancheli-mimino-1/measures.png) | [![primitive ownership](kancheli-mimino-1/classified.png)](kancheli-mimino-1/classified.png) | parts=2<br>measures=20<br>P+M=601<br>M-only=0<br>physical=306<br>[json](kancheli-mimino-1/structure.json) |

A standalone browser report is also available as [index.html](index.html).
