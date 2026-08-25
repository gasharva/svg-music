import base64
import html
from collections import defaultdict
from pathlib import PurePosixPath

import numpy as np
import ipywidgets as widgets
from IPython.display import HTML, display, clear_output
from shapely.geometry import Polygon

FEATURES = [
    ('RMS contours', 'rms'),
    ('Aspect ratio', 'aspect'),
    ('Contour count', 'count'),
    ('Perimeter spectrum', 'perimeters'),
    ('Area spectrum', 'areas'),
    ('Contour bbox', 'bbox'),
    ('Contour centroids', 'centroids'),
    ('Topology / nesting', 'topology'),
]
ALL_FEATURE_KEYS = tuple(k for _, k in FEATURES)

CLASSIFIERS = [
    ('1-NN', '1nn'),
    ('3-NN majority', '3nn'),
    ('5-NN majority', '5nn'),
    ('3-NN weighted', '3nnw'),
    ('5-NN weighted', '5nnw'),
    ('7-NN weighted', '7nnw'),
]
CLASSIFIER_LABELS = {key: label for label, key in CLASSIFIERS}


def _svg_data_uri(svg_bytes):
    return 'data:image/svg+xml;base64,' + base64.b64encode(svg_bytes).decode('ascii')


def _fmt(v):
    return '—' if v is None or not np.isfinite(v) else f'{v:.4f}'


def _pad_rms(a, b, width=1):
    a = np.asarray(a, float); b = np.asarray(b, float)
    n = max(len(a), len(b))
    if n == 0: return 0.0
    aa = np.zeros((n, width)); bb = np.zeros((n, width))
    if len(a): aa[:len(a)] = a.reshape(len(a), width)
    if len(b): bb[:len(b)] = b.reshape(len(b), width)
    return float(np.sqrt(np.mean((aa - bb) ** 2)))


def _enriched_descriptor(viewer, svg_bytes, point_count):
    base = viewer.glyph_descriptor(svg_bytes, point_count)
    paths = viewer.parse_svg(svg_bytes)
    raw = viewer.all_points(paths)
    if len(raw) == 0:
        return {**base, 'count':0, 'perimeter_ratios':[], 'area_ratios':[], 'bboxes':[], 'centroids':[], 'depths':[], 'holes':0, 'max_depth':0}

    xmin, ymin = raw.min(axis=0); xmax, ymax = raw.max(axis=0)
    h = max(ymax-ymin, 1e-9); w = max(xmax-xmin, 1e-9)
    bbox_area = max(w/h, 1e-9)
    contours = viewer.closed_contours(paths)
    perims = np.array([c.length/h for c in contours], float)
    perim_sum = max(perims.sum(), 1e-12)

    polygons=[]; areas=[]; boxes=[]; cents=[]
    for c in contours:
        q=np.asarray(c.coords,float)
        qn=np.column_stack(((q[:,0]-xmin)/h,(q[:,1]-ymin)/h))
        poly=Polygon(qn)
        if not poly.is_valid: poly=poly.buffer(0)
        polygons.append(poly)
        areas.append(abs(float(poly.area))/bbox_area if not poly.is_empty else 0.0)
        qmin=qn.min(axis=0); qmax=qn.max(axis=0)
        boxes.append([qmax[0]-qmin[0], qmax[1]-qmin[1]])
        if not poly.is_empty:
            ctd=poly.centroid; cents.append([float(ctd.x),float(ctd.y)])
        else:
            cents.append([float(qn[:,0].mean()),float(qn[:,1].mean())])

    depths=[]
    for i,poly in enumerate(polygons):
        if poly.is_empty: depths.append(0); continue
        p=poly.representative_point(); depth=0
        for j,outer in enumerate(polygons):
            if i!=j and not outer.is_empty and outer.area>poly.area and outer.contains(p): depth+=1
        depths.append(depth)

    return {
        **base,
        'count':len(contours),
        'perimeter_ratios':(perims/perim_sum).tolist(),
        'area_ratios':areas,
        'bboxes':boxes,
        'centroids':cents,
        'depths':depths,
        'holes':sum(1 for d in depths if d%2==1),
        'max_depth':max(depths,default=0),
    }


def _feature_distances(viewer,a,b):
    rms=float(viewer.compare_glyph_descriptors(a,b,0.0)['shape'])
    count_norm=max(a['count'],b['count'],1)
    topology=(
        abs(a['holes']-b['holes'])/count_norm +
        abs(a['max_depth']-b['max_depth'])/max(a['max_depth'],b['max_depth'],1) +
        _pad_rms(a['depths'],b['depths'])
    )/3.0
    return {
        'rms':rms,
        'aspect':abs(float(a['aspect'])-float(b['aspect'])),
        'count':abs(a['count']-b['count'])/count_norm,
        'perimeters':_pad_rms(a['perimeter_ratios'],b['perimeter_ratios']),
        'areas':_pad_rms(a['area_ratios'],b['area_ratios']),
        'bbox':_pad_rms(a['bboxes'],b['bboxes'],2),
        'centroids':_pad_rms(a['centroids'],b['centroids'],2),
        'topology':topology,
    }


def _combined_distance(viewer,a,b,enabled):
    parts=_feature_distances(viewer,a,b)
    vals=[parts[k] for k in enabled if k in parts]
    return (float(np.mean(vals)) if vals else float('inf')), parts


def _classifier_k(mode):
    if mode.startswith('3'): return 3
    if mode.startswith('5'): return 5
    if mode.startswith('7'): return 7
    return 1


def _predict(order, distances_row, classes, mode):
    if not order: return None
    if mode=='1nn': return classes[order[0]]
    k=min(_classifier_k(mode),len(order))
    neighbors=order[:k]
    weighted=mode.endswith('w')
    votes=defaultdict(float)
    best_distance={}
    for j in neighbors:
        d=float(distances_row[j])
        votes[classes[j]] += (1.0/max(d,1e-9)) if weighted else 1.0
        best_distance[classes[j]] = min(best_distance.get(classes[j],float('inf')),d)
    return max(votes.keys(), key=lambda c:(votes[c],-best_distance[c]))


def _sort_value(row,key):
    if key=='class': return (row['class'].lower(),row['font'].lower())
    if key=='font': return row['font'].lower()
    if key=='classifier_ok': return int(row['classifier_ok'])
    v=row.get(key,np.nan)
    return float(v) if np.isfinite(v) else float('inf')


def _build_table(rows,svg_bytes_cache,sort_key,descending,classifier_label):
    ordered=sorted(rows,key=lambda r:_sort_value(r,sort_key),reverse=descending)
    t=['<style>',
       '.rms-grand{border-collapse:collapse;font-family:system-ui,Arial,sans-serif;font-size:13px;width:100%;}',
       '.rms-grand th,.rms-grand td{border:1px solid #ddd;padding:6px 8px;vertical-align:middle;text-align:left;}',
       '.rms-grand th{background:#f2f2f2;position:sticky;top:0;z-index:1;}',
       '.rms-grand tr.fail{background:#ffd7d7;}.rms-grand tr.ok:hover{background:#f7f7f7;}',
       '.rms-grand img{width:58px;height:58px;object-fit:contain;display:block;}',
       '.rms-grand .num{font-family:ui-monospace,Consolas,monospace;text-align:right;white-space:nowrap;}',
       '.rms-grand .yes{font-weight:700}.rms-grand .no{font-weight:800;color:#a00000}',
       '.rms-grand .topk{font-size:11px;max-width:560px}.rms-grand .neg{background:#fff0f0;font-weight:700}',
       '</style><table class="rms-grand"><thead><tr>',
       '<th>Glyph</th><th>Class</th><th>Font</th><th>Nearest correct</th><th>Nearest wrong</th><th>Nearest diff</th>',
       '<th>Farthest correct</th><th>Class gap</th>',
       f'<th>{html.escape(classifier_label)} correct?</th><th>Predicted</th><th>Nearest</th>',
       '</tr></thead><tbody>']
    for r in ordered:
        ok=r['classifier_ok']; i=r['index']
        t.extend([
            f'<tr class="{"ok" if ok else "fail"}">',
            f'<td><img src="{_svg_data_uri(svg_bytes_cache[i])}"></td>',
            f'<td>{html.escape(r["class"])}</td><td>{html.escape(r["font"])}</td>',
            f'<td class="num">{_fmt(r["nearest_correct"])}</td><td class="num">{_fmt(r["nearest_wrong"])}</td>',
            f'<td class="num {"neg" if r["nearest_margin"]<0 else ""}">{_fmt(r["nearest_margin"])}</td>',
            f'<td class="num">{_fmt(r["farthest_correct"])}</td><td class="num {"neg" if r["class_margin"]<0 else ""}">{_fmt(r["class_margin"])}</td>',
            f'<td class="{"yes" if ok else "no"}">{"ДА" if ok else "НЕТ"}</td>',
            f'<td>{html.escape(r["predicted"] or "—")}</td>',
            f'<td class="topk">{html.escape(r["topk_text"])}</td></tr>'
        ])
    t.append('</tbody></table>')
    return ''.join(t)


def launch_grand_test(viewer, point_count=16, count_penalty=0.35, top_k=5):
    zf,svg_files,_=viewer.load_dataset(); files=sorted(svg_files); n=len(files)
    if n<2: print('Dataset is too small.'); return None

    points_widget=widgets.IntSlider(value=max(4,int(point_count)),min=4,max=96,step=1,description='Points:',continuous_update=False,layout=widgets.Layout(width='280px'))
    feature_widget=widgets.SelectMultiple(options=FEATURES,value=ALL_FEATURE_KEYS,description='Features:',rows=len(FEATURES),layout=widgets.Layout(width='360px',height='190px'))
    classifier_widget=widgets.Dropdown(options=CLASSIFIERS,value='1nn',description='Classifier:',layout=widgets.Layout(width='300px'))
    display_k_widget=widgets.IntSlider(value=max(5,int(top_k)),min=3,max=15,step=1,description='Show nearest:',continuous_update=False,layout=widgets.Layout(width='280px'))
    run_button=widgets.Button(description='Recalculate geometry',button_style='primary',icon='refresh')
    sort_widget=widgets.Dropdown(options=[('Nearest diff','nearest_margin'),('Class gap','class_margin'),('Classifier correct?','classifier_ok'),('Class','class'),('Font','font')],value='nearest_margin',description='Sort by:',layout=widgets.Layout(width='300px'))
    direction_widget=widgets.ToggleButtons(options=[('Ascending',False),('Descending',True)],value=False,description='Order:')

    title=widgets.HTML(); stage=widgets.HTML(); progress=widgets.IntProgress(value=0,min=0,max=n,description='Ready:')
    table_output=widgets.Output(); summary_output=widgets.Output()
    controls=widgets.HBox([points_widget,classifier_widget,display_k_widget,run_button])
    sort_controls=widgets.HBox([sort_widget,direction_widget])
    state={'distances':None,'files':files,'classes':None,'fonts':None,'svg_bytes_cache':None,'rows':None,'feature_distances':None}
    display(controls,feature_widget,title,stage,progress,summary_output,sort_controls,table_output)

    def rebuild_rows():
        if state['distances'] is None: return
        distances=state['distances']; classes=state['classes']; fonts=state['fonts']
        mode=classifier_widget.value; show_k=int(display_k_widget.value)
        rows=[]; correct=0
        for i in range(n):
            order=[int(j) for j in np.argsort(distances[i]) if j!=i and np.isfinite(distances[i,j])]
            same=[j for j in order if classes[j]==classes[i]]; wrong=[j for j in order if classes[j]!=classes[i]]
            nc=distances[i,same[0]] if same else np.nan; fc=max((distances[i,j] for j in same),default=np.nan); nw=distances[i,wrong[0]] if wrong else np.nan
            nm=nw-nc if np.isfinite(nc) and np.isfinite(nw) else np.nan; cm=nw-fc if np.isfinite(fc) and np.isfinite(nw) else np.nan
            pred=_predict(order,distances[i],classes,mode); ok=(pred==classes[i]); correct+=int(ok)
            topk_text=' · '.join(f'{classes[j]}/{PurePosixPath(files[j]).stem} ({distances[i,j]:.4f})' for j in order[:show_k])
            rows.append({'index':i,'class':classes[i],'font':fonts[i],'nearest_correct':nc,'nearest_wrong':nw,'nearest_margin':nm,'farthest_correct':fc,'class_margin':cm,'classifier_ok':ok,'predicted':pred,'topk_text':topk_text})
        state['rows']=rows
        label=CLASSIFIER_LABELS[mode]
        with summary_output:
            clear_output(wait=True)
            display(HTML(f'<b>{html.escape(label)}</b>: {correct}/{n} = {correct/n:.1%}'))
        render_table()

    def render_table(*_):
        if state['rows'] is None: return
        with table_output:
            clear_output(wait=True)
            display(HTML(_build_table(state['rows'],state['svg_bytes_cache'],sort_widget.value,direction_widget.value,CLASSIFIER_LABELS[classifier_widget.value])))

    def run_geometry(_=None):
        run_button.disabled=True
        try:
            enabled=tuple(feature_widget.value)
            if not enabled: stage.value='<b>Select at least one feature.</b>'; return
            pcount=int(points_widget.value)
            labels=[label for label,key in FEATURES if key in enabled]
            title.value=f'<b>Geometry grand test</b> — {n} glyphs; points={pcount}<br><b>Features:</b> {html.escape(", ".join(labels))}'
            stage.value='Preparing descriptors…'; progress.description='Descriptors:'; progress.bar_style='info'; progress.max=n; progress.value=0
            descriptors=[]; classes=[]; fonts=[]; svgs=[]
            for idx,name in enumerate(files,1):
                raw=zf.read(name); svgs.append(raw); descriptors.append(_enriched_descriptor(viewer,raw,pcount)); pp=PurePosixPath(name); classes.append(pp.parent.name); fonts.append(pp.name); progress.value=idx
            pairs=n*(n-1)//2; progress.description='Pairs:'; progress.max=pairs; progress.value=0; stage.value=f'Comparing {pairs:,} unique glyph pairs…'
            distances=np.full((n,n),np.inf,float); feature_distances={k:np.full((n,n),np.inf,float) for k in ALL_FEATURE_KEYS}; done=0; every=max(1,pairs//500)
            for i in range(n):
                for j in range(i+1,n):
                    d,parts=_combined_distance(viewer,descriptors[i],descriptors[j],enabled); distances[i,j]=distances[j,i]=d
                    for k,v in parts.items(): feature_distances[k][i,j]=feature_distances[k][j,i]=v
                    done+=1
                    if done%every==0 or done==pairs: progress.value=done
            state.update({'distances':distances,'classes':classes,'fonts':fonts,'svg_bytes_cache':svgs,'feature_distances':feature_distances})
            stage.value='<b>Geometry ready.</b> Switching classifier is instant; no RMS recalculation needed.'; progress.bar_style='success'; rebuild_rows()
        finally:
            run_button.disabled=False

    run_button.on_click(run_geometry)
    classifier_widget.observe(lambda *_:rebuild_rows(),names='value')
    display_k_widget.observe(lambda *_:rebuild_rows(),names='value')
    sort_widget.observe(render_table,names='value'); direction_widget.observe(render_table,names='value')
    run_geometry(); return state