import base64
import html
from collections import defaultdict
from pathlib import PurePosixPath

import numpy as np
import ipywidgets as widgets
from IPython.display import HTML, display, clear_output


def _svg_data_uri(svg_bytes):
    return 'data:image/svg+xml;base64,' + base64.b64encode(svg_bytes).decode('ascii')


def _class_score(values, mode):
    values = np.asarray(values, dtype=float)
    if len(values) == 0:
        return np.inf
    if mode == 'nearest':
        return float(np.min(values))
    if mode == 'mean':
        return float(np.mean(values))
    if mode == 'median':
        return float(np.median(values))
    raise ValueError(f'Unknown aggregation mode: {mode}')


def launch_class_distance_holdout(viewer, point_count=16):
    """Font-holdout classifier based on distance to an entire class.

    Requires the geometry grand-test helpers to have been loaded first because it reuses
    _enriched_descriptor(), _combined_distance(), FEATURES and ALL_FEATURE_KEYS.
    """
    required = ['_enriched_descriptor', '_combined_distance', 'FEATURES', 'ALL_FEATURE_KEYS']
    missing = [name for name in required if name not in globals()]
    if missing:
        print('Run geometry_glyph_grand_test.py first. Missing:', ', '.join(missing))
        return None

    zf, svg_files, _ = viewer.load_dataset()
    files = sorted(svg_files)
    fonts = sorted({PurePosixPath(name).name for name in files})
    if len(fonts) < 2:
        print('Need at least two fonts for holdout testing.')
        return None

    test_font = widgets.Dropdown(options=fonts, value=fonts[-1], description='Test font:', layout=widgets.Layout(width='330px'))
    train_fonts = widgets.SelectMultiple(options=fonts, value=tuple(fonts[:min(3, len(fonts)-1)]), description='Train fonts:', rows=min(8, len(fonts)), layout=widgets.Layout(width='360px', height='150px'))
    points = widgets.IntSlider(value=max(4, int(point_count)), min=4, max=96, step=1, description='Points:', continuous_update=False, layout=widgets.Layout(width='300px'))
    aggregation = widgets.ToggleButtons(options=[('Nearest ref', 'nearest'), ('Class mean', 'mean'), ('Class median', 'median')], value='median', description='Score:')
    feature_widget = widgets.SelectMultiple(options=FEATURES, value=ALL_FEATURE_KEYS, description='Features:', rows=len(FEATURES), layout=widgets.Layout(width='360px', height='190px'))
    run_button = widgets.Button(description='Run holdout', button_style='primary', icon='play')
    status = widgets.HTML()
    progress = widgets.IntProgress(value=0, min=0, max=1, description='Ready:')
    out = widgets.Output()
    display(widgets.HBox([test_font, points, run_button]), aggregation, train_fonts, feature_widget, status, progress, out)

    state = {}

    def normalize_train_selection(*_):
        tf = test_font.value
        selected = [f for f in train_fonts.value if f != tf]
        if not selected:
            selected = [f for f in fonts if f != tf][:3]
        train_fonts.value = tuple(selected)

    def run(_=None):
        run_button.disabled = True
        try:
            tf = test_font.value
            trains = tuple(f for f in train_fonts.value if f != tf)
            enabled = tuple(feature_widget.value)
            mode = aggregation.value
            pcount = int(points.value)

            if not trains:
                status.value = '<b>Select at least one training font.</b>'
                return
            if not enabled:
                status.value = '<b>Select at least one feature.</b>'
                return

            train_items = [name for name in files if PurePosixPath(name).name in trains]
            test_items = [name for name in files if PurePosixPath(name).name == tf]
            total_desc = len(train_items) + len(test_items)
            progress.description = 'Descriptors:'
            progress.max = max(total_desc, 1)
            progress.value = 0
            progress.bar_style = 'info'
            status.value = f'Preparing {total_desc} descriptors…'

            descriptors = {}
            raw_cache = {}
            done = 0
            for name in train_items + test_items:
                raw = zf.read(name)
                raw_cache[name] = raw
                descriptors[name] = _enriched_descriptor(viewer, raw, pcount)
                done += 1
                progress.value = done

            train_by_class = defaultdict(list)
            for name in train_items:
                train_by_class[PurePosixPath(name).parent.name].append(name)

            pair_count = len(test_items) * len(train_items)
            progress.description = 'Distances:'
            progress.max = max(pair_count, 1)
            progress.value = 0
            status.value = f'Comparing {len(test_items)} test glyphs to {len(train_items)} training glyphs…'

            distances = {}
            done = 0
            update_every = max(1, pair_count // 300) if pair_count else 1
            for test_name in test_items:
                row = {}
                for train_name in train_items:
                    d, _ = _combined_distance(viewer, descriptors[test_name], descriptors[train_name], enabled)
                    row[train_name] = float(d)
                    done += 1
                    if done % update_every == 0 or done == pair_count:
                        progress.value = done
                distances[test_name] = row

            rows = []
            correct = 0
            evaluable = 0
            missing_class = 0

            for test_name in test_items:
                true_class = PurePosixPath(test_name).parent.name
                class_scores = []
                for cls, members in train_by_class.items():
                    vals = [distances[test_name][m] for m in members]
                    class_scores.append((cls, _class_score(vals, mode), vals))
                class_scores.sort(key=lambda x: x[1])

                has_class = true_class in train_by_class
                if not has_class:
                    missing_class += 1
                predicted = class_scores[0][0] if class_scores else None
                ok = has_class and predicted == true_class
                if has_class:
                    evaluable += 1
                    correct += int(ok)

                true_score = next((s for cls, s, _ in class_scores if cls == true_class), np.nan)
                wrong_scores = [s for cls, s, _ in class_scores if cls != true_class]
                nearest_wrong = min(wrong_scores, default=np.nan)
                margin = nearest_wrong - true_score if np.isfinite(true_score) and np.isfinite(nearest_wrong) else np.nan
                top3 = ' · '.join(f'{cls} ({score:.4f})' for cls, score, _ in class_scores[:3])

                rows.append({
                    'name': test_name,
                    'true': true_class,
                    'predicted': predicted,
                    'ok': ok,
                    'has_class': has_class,
                    'true_score': true_score,
                    'nearest_wrong': nearest_wrong,
                    'margin': margin,
                    'top3': top3,
                })

            accuracy = correct / evaluable if evaluable else 0.0
            label = {'nearest':'Nearest reference', 'mean':'Class mean', 'median':'Class median'}[mode]
            status.value = (
                f'<b>{html.escape(label)}</b> — test: <b>{html.escape(tf)}</b>; '
                f'train: {html.escape(", ".join(trains))}; points={pcount}<br>'
                f'<b>Accuracy: {correct}/{evaluable} = {accuracy:.1%}</b>. '
                f'Classes absent from training: {missing_class}.'
            )
            progress.bar_style = 'success' if evaluable and correct == evaluable else 'warning'

            rows.sort(key=lambda r: (r['ok'], r['margin'] if np.isfinite(r['margin']) else -np.inf))
            table = [
                '<style>',
                '.cdh{border-collapse:collapse;font-family:system-ui,Arial,sans-serif;font-size:13px;width:100%}',
                '.cdh th,.cdh td{border:1px solid #ddd;padding:6px 8px;vertical-align:middle;text-align:left}',
                '.cdh th{background:#f2f2f2;position:sticky;top:0}',
                '.cdh tr.fail{background:#ffd7d7}.cdh tr.missing{background:#ffe7b3}',
                '.cdh img{width:58px;height:58px;object-fit:contain;display:block}',
                '.cdh .num{font-family:ui-monospace,Consolas,monospace;text-align:right;white-space:nowrap}',
                '.cdh .neg{font-weight:700;color:#a00000}',
                '</style>',
                '<table class="cdh"><thead><tr>',
                '<th>Glyph</th><th>True class</th><th>Predicted</th><th>Correct?</th>',
                '<th>True class score</th><th>Nearest wrong score</th><th>Margin</th><th>Top-3 classes</th>',
                '</tr></thead><tbody>'
            ]
            for r in rows:
                cls = 'missing' if not r['has_class'] else ('ok' if r['ok'] else 'fail')
                flag = '—' if not r['has_class'] else ('ДА' if r['ok'] else 'НЕТ')
                margin_cls = 'neg' if np.isfinite(r['margin']) and r['margin'] < 0 else ''
                table.extend([
                    f'<tr class="{cls}">',
                    f'<td><img src="{_svg_data_uri(raw_cache[r["name"]])}"></td>',
                    f'<td>{html.escape(r["true"])}</td>',
                    f'<td>{html.escape(r["predicted"] or "—")}</td>',
                    f'<td><b>{flag}</b></td>',
                    f'<td class="num">{r["true_score"]:.4f}</td>' if np.isfinite(r['true_score']) else '<td class="num">—</td>',
                    f'<td class="num">{r["nearest_wrong"]:.4f}</td>' if np.isfinite(r['nearest_wrong']) else '<td class="num">—</td>',
                    f'<td class="num {margin_cls}">{r["margin"]:.4f}</td>' if np.isfinite(r['margin']) else '<td class="num">—</td>',
                    f'<td>{html.escape(r["top3"])}</td>',
                    '</tr>'
                ])
            table.append('</tbody></table>')

            with out:
                clear_output(wait=True)
                display(HTML(''.join(table)))

            state.clear()
            state.update({
                'rows': rows,
                'accuracy': accuracy,
                'correct': correct,
                'evaluable': evaluable,
                'test_font': tf,
                'train_fonts': trains,
                'aggregation': mode,
            })
        finally:
            run_button.disabled = False

    test_font.observe(normalize_train_selection, names='value')
    run_button.on_click(run)
    normalize_train_selection()
    run()
    return state
