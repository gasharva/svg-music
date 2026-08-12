#!/usr/bin/env python3
import argparse, json, math
from pathlib import Path

def f(v): return float(v or 0)

def area(contour):
    if len(contour) < 3: return 0.0
    s = 0.0
    for i,a in enumerate(contour):
        b = contour[(i+1)%len(contour)]
        s += f(a.get('X'))*f(b.get('Y')) - f(b.get('X'))*f(a.get('Y'))
    return abs(s)/2.0

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('analysis'); ap.add_argument('output'); args=ap.parse_args()
    d=json.loads(Path(args.analysis).read_text(encoding='utf-8-sig'))
    staves=d.get('Staves',[]); events=d.get('Events',[]); paths=d.get('DirectPaths',[])
    avg=sum(f(s.get('Space')) for s in staves)/max(1,len(staves))
    beams=[]
    for p in paths:
        pts=[q for c in ((p.get('Geometry') or {}).get('Contours') or []) for q in c]
        if not (3 <= len(pts) <= 14): continue
        xs=[f(q.get('X')) for q in pts]; ys=[f(q.get('Y')) for q in pts]
        l,r,t,b=min(xs),max(xs),min(ys),max(ys); w=r-l; h=b-t
        staff=min(staves, key=lambda s: abs((t+b)/2-f(s.get('Center')))/max(f(s.get('Space')),1e-6), default=None)
        if not staff: continue
        sp=f(staff.get('Space')) or avg
        if w < sp*1.4 or h > sp*6.0 or w/max(h,sp*.05) < 1.5: continue
        a=sum(area(c) for c in ((p.get('Geometry') or {}).get('Contours') or []))
        thick=a/max(math.hypot(w,h),.001)
        if thick < sp*.05 or thick > sp*.55: continue
        beams.append((p.get('SymbolId'),l,r,t,b,sp))
    notes=[e for e in events if str(e.get('Kind','')).startswith('notehead-') and e.get('StemX') is None]
    lines=['# Missing-stem / beam-end probe','',f'Beam candidates: **{len(beams)}**','', '| symbol | staff | x | y | nearest beam | endpoint dx (sp) | inside beam x | beam span |','|---|---:|---:|---:|---|---:|---|---|']
    for n in notes:
        x,y=f(n.get('X')),f(n.get('Y')); si=int(n.get('StaffIndex',-1)); sp=f(staves[si].get('Space')) if 0<=si<len(staves) else avg
        candidates=[]
        for bid,l,r,t,b,bsp in beams:
            # beam can be several spaces vertically from head because stem bridges the gap
            dx=min(abs(x-l),abs(x-r)); inside=(l-sp*.3 <= x <= r+sp*.3)
            # rank primarily by horizontal endpoint proximity, then beam vertical plausibility
            ygap=0 if t-sp*8 <= y <= b+sp*8 else min(abs(y-t),abs(y-b))
            candidates.append((dx/sp, ygap/sp, bid,l,r,inside))
        best=min(candidates, default=None)
        if best:
            edx,yg,bid,l,r,inside=best
            lines.append(f"| `{n.get('SourceSymbolId')}` | {si} | {x:.2f} | {y:.2f} | `{bid}` | {edx:.3f} | {'yes' if inside else 'no'} | {l:.1f}..{r:.1f} |")
        else:
            lines.append(f"| `{n.get('SourceSymbolId')}` | {si} | {x:.2f} | {y:.2f} |  |  |  |  |")
    Path(args.output).write_text('\n'.join(lines)+'\n',encoding='utf-8')
if __name__=='__main__': main()
