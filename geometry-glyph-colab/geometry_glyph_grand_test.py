import base64
import html
from pathlib import PurePosixPath

import numpy as np
import ipywidgets as widgets
from IPython.display import HTML, display, clear_output
from shapely.geometry import Polygon, Point

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


def _svg_data_uri(svg_bytes):
    return 'data:image/svg+xml;base64,' + base64.b64encode(svg_bytes).decode('ascii')


def _fmt(value):
    if value is None or not np.isfinite(value):
        return '—'
    return f'{value:.4f}'


def _sort_value(row, key):
    if key == 'class': return (row['class'].lower(), row['font'].lower())
    if key == 'font': return row['font'].lower()
    if key == 'top1_ok': return int(row['top1_ok'])
    value = row.get(key, np.nan)
    return float(value) if np.isfinite(value) else float('inf')


def _build_table(rows, svg_bytes_cache, sort_key, descending):
    ordered = sorted(rows, key=lambda r: _sort_value(r, sort_key), reverse=descending)
    table = [
        '<style>',
        '.rms-grand{border-collapse:collapse;font-family:system-ui,Arial,sans-serif;font-size:13px;width:100%;}',
        '.rms-grand th,.rms-grand td{border:1px solid #ddd;padding:6px 8px;vertical-align:middle;text-align:left;}',
        '.rms-grand th{background:#f2f2f2;position:sticky;top:0;z-index:1;}',
        '.rms-grand tr.fail{background:#ffd7d7;}',
        '.rms-grand tr.ok:hover{background:#f7f7f7;}',
        '.rms-grand img{width:58px;height:58px;object-fit:contain;display:block;}',
        '.rms-grand .num{font-family:ui-monospace,Consolas,monospace;text-align:right;white-space:nowrap;}',
        '.rms-grand .yes{font-weight:700;}.rms-grand .no{font-weight:800;color:#a00000;}',
        '.rms-grand .top5{font-size:11px;max-width:520px;}',
        '.rms-grand .pos{background:#effbef;}.rms-grand .neg{background:#fff0f0;font-weight:700;}',
        '</style>',
        '<table class="rms-grand"><thead><tr>',
        '<th>Glyph</th><th>Class</th><th>Font</th>',
        '<th>Nearest correct</th><th>Nearest wrong</th><th>Nearest diff</th>',
        '<th>Farthest correct</th><th>Class gap</th><th>TOP-1 correct?</th><th>TOP-K nearest</th>',
        '</tr></thead><tbody>'
    ]
    for r in ordered:
        i = r['index']; ok = r['top1_ok']
        near_cls = 'pos' if np.isfinite(r['nearest_margin']) and r['nearest_margin'] >= 0 else 'neg'
        gap_cls = 'pos' if np.isfinite(r['class_margin']) and r['class_margin'] >= 0 else 'neg'
        table.extend([
            f'<tr class="{"ok" if ok else "fail"}">',
            f'<td><img src="{_svg_data_uri(svg_bytes_cache[i])}" alt="glyph"></td>',
            f'<td>{html.escape(r["class"])}</td><td>{html.escape(r["font"])}</td>',
            f'<td class="num">{_fmt(r["nearest_correct"])}</td>',
            f'<td class="num">{_fmt(r["nearest_wrong"])}</td>',
            f'<td class="num {near_cls}">{_fmt(r["nearest_margin"])}</td>',
            f'<td class="num">{_fmt(r["farthest_correct"])}</td>',
            f'<td class="num {gap_cls}">{_fmt(r["class_margin"])}</td>',
            f'<td class="{"yes" if ok else "no"}">{"ДА" if ok else "НЕТ"}</td>',
            f'<td class="top5">{html.escape(r["top5"])}</td></tr>'
        ])
    table.append('</tbody></table>')
    return ''.join(table)


def _pad_rms(a, b, width=1):
    a = np.asarray(a, dtype=float); b = np.asarray(b, dtype=float)
    n = max(len(a), len(b))
    if n == 0: return 0.0
    aa = np.zeros((n, width), dtype=float); bb = np.zeros((n, width), dtype=float)
    if len(a): aa[:len(a)] = a.reshape(len(a), width)
    if len(b): bb[:len(b)] = b.reshape(len(b), width)
    return float(np.sqrt(np.mean((aa - bb) ** 2)))


def _enriched_descriptor(viewer, svg_bytes, point_count):
    base = viewer.glyph_descriptor(svg_bytes, point_count)
    paths = viewer.parse_svg(svg_bytes)
    raw = viewer.all_points(paths)
    if len(raw) == 0:
        return {**base, 'count': 0, 'perimeter_ratios': [], 'area_ratios': [], 'bboxes': [],
                'centroids': [], 'depths': [], 'holes': 0, 'max_depth': 0}

    xmin, ymin = raw.min(axis=0); xmax, ymax = raw.max(axis=0)
    h = max(ymax - ymin, 1e-9); w = max(xmax - xmin, 1e-9)
    bbox_area = max((w / h), 1e-9)  # normalized by h, so normalized glyph bbox area = aspect
    contours = viewer.closed_contours(paths)  # already perimeter-sorted
    perims = np.array([c.length / h for c in contours], dtype=float)
    perim_sum = max(perims.sum(), 1e-12)

    polygons = []
    areas = []; boxes = []; cents = []
    for c in contours:
        q = np.asarray(c.coords, dtype=float)
        qn = np.column_stack(((q[:, 0] - xmin) / h, (q[:, 1] - ymin) / h))
        poly = Polygon(qn)
        if not poly.is_valid:
            poly = poly.buffer(0)
        polygons.append(poly)
        areas.append(abs(float(poly.area)) / bbox_area if not poly.is_empty else 0.0)
        qmin = qn.min(axis=0); qmax = qn.max(axis=0)
        boxes.append([qmax[0] - qmin[0], qmax[1] - qmin[1]])
        if not poly.is_empty:
            ctd = poly.centroid
            cents.append([float(ctd.x), float(ctd.y)])
        else:
            cents.append([float(qn[:, 0].mean()), float(qn[:, 1].mean())])

    depths = []
    for i, poly in enumerate(polygons):
        if poly.is_empty:
            depths.append(0); continue
        p = poly.representative_point()
        depth = 0
        for j, outer in enumerate(polygons):
            if i != j and not outer.is_empty and outer.area > poly.area and outer.contains(p):
                depth += 1
        depths.append(depth)

    holes = sum(1 for d in depths if d % 2 == 1)
    return {
        **base,
        'count': len(contours),
        'perimeter_ratios': (perims / perim_sum).tolist(),
        'area_ratios': areas,
        'bboxes': boxes,
        'centroids': cents,
        'depths': depths,
        'holes': holes,
        'max_depth': max(depths, default=0),
    }


def _feature_distances(viewer, a, b):
    rms = float(viewer.compare_glyph_descriptors(a, b, 0.0)['shape'])
    count_norm = max(a['count'], b['count'], 1)
    topology_depth = _pad_rms(a['depths'], b['depths'])
    topology = (
        abs(a['holes'] - b['holes']) / count_norm
        + abs(a['max_depth'] - b['max_depth']) / max(a['max_depth'], b['max_depth'], 1)
        + topology_depth
    ) / 3.0
    return {
        'rms': rms,
        'aspect': abs(float(a['aspect']) - float(b['aspect'])),
        'count': abs(a['count'] - b['count']) / count_norm,
        'perimeters': _pad_rms(a['perimeter_ratios'], b['perimeter_ratios']),
        'areas': _pad_rms(a['area_ratios'], b['area_ratios']),
        'bbox': _pad_rms(a['bboxes'], b['bboxes'], 2),
        'centroids': _pad_rms(a['centroids'], b['centroids'], 2),
        'topology': topology,
    }


def _combined_distance(viewer, a, b, enabled):
    parts = _feature_distances(viewer, a, b)
    chosen = [parts[k] for k in enabled if k in parts]
    return (float(np.mean(chosen)) if chosen else float('inf')), parts


def launch_grand_test(viewer, point_count=16, count_penalty=0.35, top_k=5):
    zf, svg_files, _ = viewer.load_dataset()
    files = sorted(svg_files); n = len(files)
    if n < 2:
        print('Dataset is too small for a nearest-neighbour test.'); return None

    points_widget = widgets.IntSlider(value=max(4, int(point_count)), min=4, max=96, step=1,
                                     description='Points:', continuous_update=False, layout=widgets.Layout(width='300px'))
    topk_widget = widgets.IntSlider(value=max(1, min(int(top_k), n - 1)), min=1, max=min(20, n - 1), step=1,
                                   description='Top K:', continuous_update=False, layout=widgets.Layout(width='260px'))
    feature_widget = widgets.SelectMultiple(options=FEATURES, value=ALL_FEATURE_KEYS, description='Features:',
                                            rows=len(FEATURES), layout=widgets.Layout(width='360px', height='190px'))
    run_button = widgets.Button(description='Recalculate', button_style='primary', icon='refresh')
    sort_widget = widgets.Dropdown(options=[('Nearest diff','nearest_margin'),('Class gap','class_margin'),
                                            ('Nearest correct','nearest_correct'),('Nearest wrong','nearest_wrong'),
                                            ('Farthest correct','farthest_correct'),('TOP-1 correct?','top1_ok'),
                                            ('Class','class'),('Font','font')],
                                   value='nearest_margin', description='Sort by:', layout=widgets.Layout(width='330px'))
    direction_widget = widgets.ToggleButtons(options=[('Ascending',False),('Descending',True)], value=False, description='Order:')

    title = widgets.HTML(); stage = widgets.HTML(); progress = widgets.IntProgress(value=0, min=0, max=n, description='Ready:')
    table_output = widgets.Output(); controls = widgets.HBox([points_widget, topk_widget, run_button]); sort_controls = widgets.HBox([sort_widget, direction_widget])
    state = {'rows':None,'svg_bytes_cache':None,'distances':None,'files':files,'classes':None,'fonts':None,
             'top1_accuracy':None,'top1_correct':None,'count':n,'feature_distances':None}

    display(controls, feature_widget, title, stage, progress, sort_controls, table_output)

    def render_table(*_):
        if state['rows'] is None: return
        with table_output:
            clear_output(wait=True)
            display(HTML(_build_table(state['rows'], state['svg_bytes_cache'], sort_widget.value, direction_widget.value)))

    def run(_=None):
        run_button.disabled = True
        try:
            point_count_now = int(points_widget.value); top_k_now = max(1, min(int(topk_widget.value), n - 1))
            enabled = tuple(feature_widget.value)
            if not enabled:
                stage.value = '<b>Select at least one feature.</b>'; return
            enabled_labels = [label for label, key in FEATURES if key in enabled]
            title.value = (f'<b>Geometry grand test</b> — {n} glyphs; top-K={top_k_now}; points={point_count_now}<br>'
                           f'<b>Features:</b> {html.escape(", ".join(enabled_labels))}')
            stage.value='Preparing descriptors…'; progress.description='Descriptors:'; progress.bar_style='info'; progress.max=n; progress.value=0

            descriptors=[]; classes=[]; fonts=[]; svg_bytes_cache=[]
            for i,name in enumerate(files,1):
                raw=zf.read(name); svg_bytes_cache.append(raw); descriptors.append(_enriched_descriptor(viewer, raw, point_count_now))
                p=PurePosixPath(name); classes.append(p.parent.name); fonts.append(p.name); progress.value=i

            pair_count=n*(n-1)//2; progress.description='Pairs:'; progress.max=pair_count; progress.value=0
            stage.value=f'Comparing {pair_count:,} unique glyph pairs…'; distances=np.full((n,n),np.inf,float)
            feature_distances={k:np.full((n,n),np.inf,float) for k in ALL_FEATURE_KEYS}
            done=0; update_every=max(1,pair_count//500)
            for i in range(n):
                for j in range(i+1,n):
                    d,parts=_combined_distance(viewer,descriptors[i],descriptors[j],enabled)
                    distances[i,j]=distances[j,i]=d
                    for k,v in parts.items(): feature_distances[k][i,j]=feature_distances[k][j,i]=v
                    done+=1
                    if done%update_every==0 or done==pair_count: progress.value=done

            stage.value='Building nearest-neighbour report…'; progress.description='Rows:'; progress.max=n; progress.value=0
            rows=[]; correct_top1=0
            for i in range(n):
                order=[int(j) for j in np.argsort(distances[i]) if j!=i and np.isfinite(distances[i,j])]
                nearest=order[:top_k_now]
                same=[j for j in range(n) if j!=i and classes[j]==classes[i] and np.isfinite(distances[i,j])]
                wrong=[j for j in range(n) if classes[j]!=classes[i] and np.isfinite(distances[i,j])]
                nearest_correct=min((distances[i,j] for j in same),default=np.nan)
                farthest_correct=max((distances[i,j] for j in same),default=np.nan)
                nearest_wrong=min((distances[i,j] for j in wrong),default=np.nan)
                nearest_margin=nearest_wrong-nearest_correct if np.isfinite(nearest_correct) and np.isfinite(nearest_wrong) else np.nan
                class_margin=nearest_wrong-farthest_correct if np.isfinite(farthest_correct) and np.isfinite(nearest_wrong) else np.nan
                top1_ok=bool(order) and classes[order[0]]==classes[i]; correct_top1+=int(top1_ok)
                top5_text=' · '.join(f'{classes[j]}/{PurePosixPath(files[j]).stem} ({distances[i,j]:.4f})' for j in nearest)
                rows.append({'index':i,'class':classes[i],'font':fonts[i],'nearest_correct':nearest_correct,
                             'farthest_correct':farthest_correct,'nearest_wrong':nearest_wrong,'nearest_margin':nearest_margin,
                             'class_margin':class_margin,'top1_ok':top1_ok,'top5':top5_text})
                progress.value=i+1

            accuracy=correct_top1/n; negative_class=sum(np.isfinite(r['class_margin']) and r['class_margin']<0 for r in rows)
            negative_nearest=sum(np.isfinite(r['nearest_margin']) and r['nearest_margin']<0 for r in rows)
            stage.value=(f'<b>Done.</b> TOP-1 accuracy: {correct_top1}/{n} = {accuracy:.1%}. '
                         f'Negative nearest margins: {negative_nearest}. Negative class-gap margins: {negative_class}.')
            progress.bar_style='success' if correct_top1==n else 'warning'
            state.update({'rows':rows,'svg_bytes_cache':svg_bytes_cache,'distances':distances,'classes':classes,'fonts':fonts,
                          'top1_accuracy':accuracy,'top1_correct':correct_top1,'feature_distances':feature_distances})
            render_table()
        finally:
            run_button.disabled=False

    run_button.on_click(run); sort_widget.observe(render_table,names='value'); direction_widget.observe(render_table,names='value')
    run(); return state
