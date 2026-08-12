#!/usr/bin/env python3
import argparse, json, math
from pathlib import Path

def f(v): return float(v or 0)

def bbox(p):
    pts=[q for c in ((p.get('Geometry') or {}).get('Contours') or []) for q in c]
    if not pts: return None
    xs=[f(q.get('X')) for q in pts]; ys=[f(q.get('Y')) for q in pts]
    return min(xs),max(xs),min(ys),max(ys),len(pts),pts

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('analysis'); ap.add_argument('output'); args=ap.parse_args()
    d=json.loads(Path(args.analysis).read_text(encoding='utf-8-sig'))
    staves=d.get('Staves',[]); events=d.get('Events',[]); paths=d.get('DirectPaths',[])
    avg=sum(f(s.get('Space')) for s in staves)/max(1,len(staves))
    notes=[e for e in events if str(e.get('Kind','')).startswith('notehead-') and e.get('StemX') is None]
    lines=['# Missing-stem / local beam-shape probe','']
    seen=set()
    for n in notes:
        x,y=f(n.get('X')),f(n.get('Y')); si=int(n.get('StaffIndex',-1)); sp=f(staves[si].get('Space')) if 0<=si<len(staves) else avg
        lines += [f"## `{n.get('SourceSymbolId')}` staff {si} at ({x:.2f}, {y:.2f})",'', '| path | span x | size (sp) | endpoint dx (sp) | vertical offset (sp) | points |','|---|---|---|---:|---:|---:|']
        c=[]
        for p in paths:
            b=bbox(p)
            if not b: continue
            l,r,t,bot,np,pts=b; w=r-l; h=bot-t
            if w < sp*.8 or w > sp*30 or h > sp*3.0 or w/max(h,sp*.03) < 1.4: continue
            xdist=0 if l-sp*1.5 <= x <= r+sp*1.5 else min(abs(x-l),abs(x-r))
            cy=(t+bot)/2; yoff=abs(y-cy)
            if xdist > sp*3.0 or yoff > sp*9.0: continue
            edx=min(abs(x-l),abs(x-r))/sp
            c.append((xdist/sp,yoff/sp,edx,p.get('SymbolId'),l,r,w/sp,h/sp,np,pts))
        c.sort(key=lambda z:(z[0],z[1],z[2]))
        for _,yoff,edx,pid,l,r,wsp,hsp,np,pts in c[:10]:
            lines.append(f"| `{pid}` | {l:.1f}..{r:.1f} | {wsp:.2f}×{hsp:.2f} | {edx:.3f} | {yoff:.2f} | {np} |")
            if pid not in seen and np <= 12:
                seen.add(pid)
                coords=' -> '.join(f"({f(q.get('X')):.2f},{f(q.get('Y')):.2f})" for q in pts)
                lines.append(f"\n`{pid}` vertices: `{coords}`\n")
        lines.append('')
    Path(args.output).write_text('\n'.join(lines)+'\n',encoding='utf-8')
if __name__=='__main__': main()
