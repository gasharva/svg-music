import base64
import html
from pathlib import PurePosixPath

import numpy as np
import ipywidgets as widgets
from IPython.display import HTML, display, clear_output
from sklearn.ensemble import RandomForestClassifier


def _svg_uri(raw):
    return 'data:image/svg+xml;base64,' + base64.b64encode(raw).decode('ascii')


def _font_name(path):
    return PurePosixPath(path).name


def _class_name(path):
    return PurePosixPath(path).parent.name


def _feature_vector(d, max_contours, point_count):
    # Global / topology features first.
    out = [
        float(d.get('aspect', 0.0)),
        float(d.get('count', 0)),
        float(d.get('holes', 0)),
        float(d.get('max_depth', 0)),
    ]

    perims = d.get('perimeter_ratios', [])
    areas = d.get('area_ratios', [])
    boxes = d.get('bboxes', [])
    cents = d.get('centroids', [])
    depths = d.get('depths', [])
    contours = d.get('contours', [])

    # Contours have already been sorted by perimeter by the geometry pipeline.
    # Every contour gets the same fixed slot so RandomForest sees a rectangular matrix.
    for i in range(max_contours):
        if i < len(contours):
            p = np.asarray(contours[i], dtype=float)
            if len(p) != point_count:
                q = np.zeros((point_count, 2), dtype=float)
                q[:min(len(p), point_count)] = p[:point_count]
                p = q
            bbox = boxes[i] if i < len(boxes) else [0.0, 0.0]
            cent = cents[i] if i < len(cents) else [0.0, 0.0]
            out.extend([
                float(perims[i]) if i < len(perims) else 0.0,
                float(areas[i]) if i < len(areas) else 0.0,
                float(bbox[0]), float(bbox[1]),
                float(cent[0]), float(cent[1]),
                float(depths[i]) if i < len(depths) else 0.0,
            ])
            out.extend(p.reshape(-1).tolist())
        else:
            out.extend([0.0] * (7 + point_count * 2))
    return np.asarray(out, dtype=np.float32)


def launch_random_forest(viewer, point_count=16, trees=500, train_font_count=3, random_state=42):
    """Train on selected fonts and evaluate only on one completely unseen font."""
    descriptor_fn = globals().get('_enriched_descriptor')
    if descriptor_fn is None:
        raise RuntimeError(
            'Run geometry_glyph_grand_test.py first: _enriched_descriptor is required.')

    zf, svg_files, _ = viewer.load_dataset()
    files = sorted(svg_files)
    fonts = sorted({_font_name(n) for n in files})
    if len(fonts) < 2:
        print('Need at least two fonts.'); return None

    test_font = widgets.Dropdown(options=fonts, value=fonts[0], description='Test font:', layout=widgets.Layout(width='330px'))
    train_fonts = widgets.SelectMultiple(options=[], description='Train fonts:', rows=min(6, len(fonts)-1), layout=widgets.Layout(width='350px', height='145px'))
    points = widgets.IntSlider(value=int(point_count), min=8, max=64, step=1, description='Points:', continuous_update=False, layout=widgets.Layout(width='280px'))
    tree_count = widgets.IntSlider(value=int(trees), min=100, max=1500, step=100, description='Trees:', continuous_update=False, layout=widgets.Layout(width='300px'))
    max_depth = widgets.IntSlider(value=0, min=0, max=40, step=1, description='Max depth:', continuous_update=False, layout=widgets.Layout(width='300px'))
    run_btn = widgets.Button(description='Train & evaluate', button_style='primary', icon='play')

    summary = widgets.HTML()
    progress = widgets.IntProgress(value=0, min=0, max=1, description='Ready:')
    output = widgets.Output()
    updating = {'v': False}

    def reset_train_fonts(*_):
        if updating['v']: return
        updating['v'] = True
        try:
            others = [f for f in fonts if f != test_font.value]
            train_fonts.options = others
            train_fonts.value = tuple(others[:min(train_font_count, len(others))])
        finally:
            updating['v'] = False

    def run(_=None):
        selected = tuple(train_fonts.value)
        if not selected:
            summary.value = '<b>Select at least one training font.</b>'
            return
        if test_font.value in selected:
            summary.value = '<b>Test font must not be in training fonts.</b>'
            return

        run_btn.disabled = True
        try:
            pcount = int(points.value)
            selected_set = set(selected)
            relevant = [n for n in files if _font_name(n) == test_font.value or _font_name(n) in selected_set]

            progress.description = 'Descriptors:'
            progress.bar_style = 'info'
            progress.max = len(relevant)
            progress.value = 0
            desc = {}
            raw_cache = {}
            for idx, name in enumerate(relevant, 1):
                raw = zf.read(name)
                raw_cache[name] = raw
                desc[name] = descriptor_fn(viewer, raw, pcount)
                progress.value = idx

            max_contours = max((int(d.get('count', 0)) for d in desc.values()), default=0)
            train = [n for n in relevant if _font_name(n) in selected_set]
            test = [n for n in relevant if _font_name(n) == test_font.value]

            x_train = np.vstack([_feature_vector(desc[n], max_contours, pcount) for n in train])
            y_train = np.array([_class_name(n) for n in train])
            x_test = np.vstack([_feature_vector(desc[n], max_contours, pcount) for n in test])
            y_test = np.array([_class_name(n) for n in test])

            trained_classes = set(y_train.tolist())
            impossible = np.array([c not in trained_classes for c in y_test])

            progress.description = 'Forest:'
            progress.max = int(tree_count.value)
            progress.value = 0
            clf = RandomForestClassifier(
                n_estimators=int(tree_count.value),
                max_depth=(None if int(max_depth.value) == 0 else int(max_depth.value)),
                class_weight='balanced_subsample',
                random_state=int(random_state),
                n_jobs=-1,
                max_features='sqrt',
            )
            clf.fit(x_train, y_train)
            progress.value = int(tree_count.value)

            pred = clf.predict(x_test)
            probs = clf.predict_proba(x_test)
            prob_classes = clf.classes_
            correct = pred == y_test
            accuracy = float(correct.mean()) if len(correct) else 0.0
            possible_mask = ~impossible
            possible_accuracy = float(correct[possible_mask].mean()) if possible_mask.any() else float('nan')

            summary.value = (
                f'<b>Random Forest</b> — test: <b>{html.escape(test_font.value)}</b>; '
                f'train: {html.escape(", ".join(selected))}; trees={tree_count.value}; points={pcount}; '
                f'features={x_train.shape[1]}<br>'
                f'<b>Accuracy: {int(correct.sum())}/{len(test)} = {accuracy:.1%}</b>. '
                f'Classes absent from training: {int(impossible.sum())}. '
                + (f'Accuracy where class exists in training: {possible_accuracy:.1%}.' if np.isfinite(possible_accuracy) else '')
            )
            progress.bar_style = 'success' if accuracy == 1.0 else 'warning'

            rows = []
            for i, name in enumerate(test):
                order = np.argsort(probs[i])[::-1][:3]
                top3 = ' · '.join(f'{prob_classes[j]} ({probs[i,j]:.1%})' for j in order)
                rows.append((not bool(correct[i]), float(probs[i, order[0]]), name, y_test[i], pred[i], top3, bool(impossible[i])))
            rows.sort(key=lambda r: (not r[0], -r[1]))  # failures first, strongest confidence first

            table = [
                '<style>.rf{border-collapse:collapse;font-family:system-ui,Arial,sans-serif;font-size:13px;width:100%}',
                '.rf th,.rf td{border:1px solid #ddd;padding:6px 8px;text-align:left;vertical-align:middle}',
                '.rf th{background:#f2f2f2;position:sticky;top:0}.rf tr.bad{background:#ffd7d7}',
                '.rf tr.missing{background:#ffe8b5}.rf img{width:58px;height:58px;object-fit:contain;display:block}',
                '.rf .yes{font-weight:700}.rf .no{font-weight:800;color:#a00000}</style>',
                '<table class="rf"><thead><tr><th>Glyph</th><th>Class</th><th>Font</th><th>Predicted</th><th>Correct?</th><th>Top-3 probability</th></tr></thead><tbody>'
            ]
            for failed, conf, name, true_cls, pcls, top3, missing in rows:
                cls = 'missing' if missing else ('bad' if failed else '')
                ok = not failed
                table.append(
                    f'<tr class="{cls}"><td><img src="{_svg_uri(raw_cache[name])}"></td>'
                    f'<td>{html.escape(true_cls)}</td><td>{html.escape(_font_name(name))}</td>'
                    f'<td>{html.escape(str(pcls))}</td>'
                    f'<td class="{"yes" if ok else "no"}">{"ДА" if ok else "НЕТ"}</td>'
                    f'<td>{html.escape(top3)}</td></tr>'
                )
            table.append('</tbody></table>')
            with output:
                clear_output(wait=True)
                display(HTML(''.join(table)))

            return {'classifier': clf, 'accuracy': accuracy, 'train_files': train, 'test_files': test,
                    'feature_count': x_train.shape[1], 'max_contours': max_contours}
        finally:
            run_btn.disabled = False

    test_font.observe(reset_train_fonts, names='value')
    reset_train_fonts()
    display(widgets.HBox([test_font, points, tree_count, max_depth, run_btn]), train_fonts, summary, progress, output)
    run_btn.on_click(run)
    run()
    return {'run': run, 'test_font': test_font, 'train_fonts': train_fonts}
