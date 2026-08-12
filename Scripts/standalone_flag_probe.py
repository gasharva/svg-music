#!/usr/bin/env python3
import argparse, collections, json
from pathlib import Path


def f(v): return float(v or 0)

def bbox(item):
    contours=((item.get('Geometry') or {}).get('Contours') or [])
    pts=[p for c in contours for p in c]
    if not pts: return None
    xs=[f(p.get('X')) for p in pts]; ys=[f(p.get('Y')) for p in pts]
    return min(xs),min(ys),max(xs),max(ys),sum(len(c) for c in contours),len(contours)

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('analysis'); ap.add_argument('classification'); ap.add_argument('output'); a=ap.parse_args()
    d=json.loads(Path(a.analysis).read_text(encoding='utf-8-sig'))
    c=json.loads(Path(a.classification).read_text(encoding='utf-8-sig'))
    staves=d.get('Staves',[]); events=d.get('Events',[]); page=d.get('PageGeometry',[]); lines=d.get('LineSegments',[])
    classes={x.get('SymbolId'):x for x in c.get('Symbols',[])}

    def stem_for(note, staff):
        sp=f(staff.get('Space')) or 1
        sx=f(note.get('StemX'))
        cand=[]
        for line in lines:
            cx=(f(line.get('X1'))+f(line.get('X2')))/2
            top=min(f(line.get('Y1')),f(line.get('Y2'))); bot=max(f(line.get('Y1')),f(line.get('Y2')))
            h=bot-top
            if abs(cx-sx)>sp*.20 or not (sp*1.0<=h<=sp*11.2): continue
            if top<=f(note.get('Y'))+sp*.95 and bot>=f(note.get('Y'))-sp*.95:
                cand.append((abs(cx-sx),top,bot,cx))
        return min(cand,default=None)

    ignored_prefix=('notehead-','clef-','rest-','accidental-')
    ignored_exact={'augmentation-dot','time-signature-digit'}
    rows=collections.defaultdict(lambda:{'hits':0,'kind':'','ref':'','samples':[],'instances':set()})

    for note in events:
        if note.get('Kind')!='notehead-black' or int(note.get('BeamCount') or 0)>0 or note.get('StemX') is None: continue
        si=int(note.get('StaffIndex',-1))
        if si<0 or si>=len(staves): continue
        staff=staves[si]; sp=f(staff.get('Space')) or 1
        stem=stem_for(note,staff)
        if not stem: continue
        _,top,bot,sx=stem
        direction=note.get('StemDirection')
        free_y=top if direction=='up' else bot

        for item in page:
            if item.get('SourceKind')!='use' or not item.get('SourceSymbolId'): continue
            sid=item.get('SourceSymbolId'); cls=classes.get(sid) or {}; kind=str(cls.get('Kind') or '')
            if kind.startswith(ignored_prefix) or kind in ignored_exact: continue
            b=bbox(item)
            if not b: continue
            l,t,r,bb,points,contours=b
            cx=(l+r)/2; cy=(t+bb)/2; w=(r-l)/sp; h=(bb-t)/sp
            if not (.25<=w<=2.8 and .45<=h<=4.2): continue
            dx=(cx-sx)/sp; dy=(cy-free_y)/sp
            if abs(dx)>2.0 or abs(dy)>2.8: continue
            # A flag lives at the free end and normally extends to the stem's outside/right for
            # up-stems or outside/left for down-stems. Keep a loose signed-side preference but
            # report near-zero cases too because exporter transforms can shift the glyph origin.
            side_ok=(direction=='up' and dx>=-.35) or (direction=='down' and dx<=.35)
            score=abs(dy)+abs(dx)*.65+(0 if side_ok else .8)
            if score>3.0: continue
            row=rows[sid]; row['hits']+=1; row['kind']=kind or '<unclassified>'; row['ref']=cls.get('ReferenceId',''); row['instances'].add(item.get('InstanceId'))
            if len(row['samples'])<6:
                row['samples'].append((round(f(note.get('X')),2),round(f(note.get('Y')),2),direction,round(dx,2),round(dy,2),round(w,2),round(h,2),points,contours,item.get('InstanceId')))

    out=['# Standalone flag probe','', 'Unknown/non-note glyphs near the free end of unbeamed black-note stems. Signed dx/dy are in staff spaces relative to the stem free end.','', '| symbol | hits | kind | reference | samples (note x,y,dir, dx,dy,w,h,points,contours,instance) |','|---|---:|---|---|---|']
    for sid,row in sorted(rows.items(), key=lambda z:(-z[1]['hits'],z[0])):
        out.append(f"| `{sid}` | {row['hits']} | {row['kind']} | {row['ref']} | `{row['samples']}` |")
    Path(a.output).write_text('\n'.join(out)+'\n',encoding='utf-8')

if __name__=='__main__': main()
