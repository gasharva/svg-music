#!/usr/bin/env python3
import argparse, json, math
from pathlib import Path

def f(v): return float(v or 0)

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('analysis'); ap.add_argument('output'); args=ap.parse_args()
    d=json.loads(Path(args.analysis).read_text(encoding='utf-8-sig'))
    staves=d.get('Staves',[]); events=d.get('Events',[]); paths=d.get('DirectPaths',[])
    avg=sum(f(s.get('Space')) for s in staves)/max(1,len(staves))
    notes=[e for e in events if str(e.get('Kind','')).startswith('notehead-') and e.get('StemX') is None]
    lines=['# Missing-stem / compound-path vertical-edge probe','']
    for n in notes:
        x,y=f(n.get('X')),f(n.get('Y')); si=int(n.get('StaffIndex',-1)); sp=f(staves[si].get('Space')) if 0<=si<len(staves) else avg
        lines += [f"## `{n.get('SourceSymbolId')}` staff {si} at ({x:.2f}, {y:.2f})",'', '| path | edge x | y span | length (sp) | dx from head (sp) | parent bbox (sp) |','|---|---:|---|---:|---:|---|']
        found=[]
        for p in paths:
            contours=((p.get('Geometry') or {}).get('Contours') or [])
            allpts=[q for c in contours for q in c]
            if not allpts: continue
            xs=[f(q.get('X')) for q in allpts]; ys=[f(q.get('Y')) for q in allpts]
            l,r,t,b=min(xs),max(xs),min(ys),max(ys)
            for contour in contours:
                for i in range(1,len(contour)):
                    a,bp=contour[i-1],contour[i]
                    x1,y1=f(a.get('X')),f(a.get('Y')); x2,y2=f(bp.get('X')),f(bp.get('Y'))
                    dy=abs(y2-y1); dx=abs(x2-x1)
                    if dy < sp*.65 or dx > max(sp*.12,dy*.10): continue
                    ex=(x1+x2)/2; top=min(y1,y2); bot=max(y1,y2)
                    # Stem may end several spaces above/below the notehead at a beam.
                    if abs(ex-x) > sp*1.8: continue
                    if bot < y-sp*9 or top > y+sp*9: continue
                    found.append((abs(ex-x)/sp, -dy/sp, p.get('SymbolId'), ex, top, bot, dy/sp, (r-l)/sp, (b-t)/sp))
        found.sort()
        for dd,_,pid,ex,top,bot,lens,w,h in found[:12]:
            lines.append(f"| `{pid}` | {ex:.2f} | {top:.1f}..{bot:.1f} | {lens:.2f} | {dd:.3f} | {w:.2f}×{h:.2f} |")
        lines.append('')
    Path(args.output).write_text('\n'.join(lines)+'\n',encoding='utf-8')
if __name__=='__main__': main()
