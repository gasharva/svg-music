import base64
import html
from pathlib import PurePosixPath

import numpy as np
import ipywidgets as widgets
from IPython.display import HTML, display, clear_output


def _svg_data_uri(svg_bytes):
    encoded = base64.b64encode(svg_bytes).decode('ascii')
    return f'data:image/svg+xml;base64,{encoded}'


def _fmt(value):
    if value is None or not np.isfinite(value):
        return '—'
    return f'{value:.4f}'


def _sort_value(row, key):
    if key == 'class':
        return (row['class'].lower(), row['font'].lower())
    if key == 'font':
        return row['font'].lower()
    if key == 'top1_ok':
        return int(row['top1_ok'])
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
        '.rms-grand .yes{font-weight:700;}',
        '.rms-grand .no{font-weight:800;color:#a00000;}',
        '.rms-grand .top5{font-size:11px;max-width:520px;}',
        '.rms-grand .pos{background:#effbef;}',
        '.rms-grand .neg{background:#fff0f0;font-weight:700;}',
        '</style>',
        '<table class="rms-grand">',
        '<thead><tr>',
        '<th>Glyph</th><th>Class</th><th>Font</th>',
        '<th>Nearest correct</th><th>Nearest wrong</th><th>Nearest diff</th>',
        '<th>Farthest correct</th><th>Class gap</th>',
        '<th>TOP-1 correct?</th><th>TOP-5 nearest</th>',
        '</tr></thead><tbody>',
    ]

    for r in ordered:
        i = r['index']
        cls = 'ok' if r['top1_ok'] else 'fail'
        flag_cls = 'yes' if r['top1_ok'] else 'no'
        flag = 'ДА' if r['top1_ok'] else 'НЕТ'
        near_cls = 'pos' if np.isfinite(r['nearest_margin']) and r['nearest_margin'] >= 0 else 'neg'
        gap_cls = 'pos' if np.isfinite(r['class_margin']) and r['class_margin'] >= 0 else 'neg'
        uri = _svg_data_uri(svg_bytes_cache[i])
        table.extend([
            f'<tr class="{cls}">',
            f'<td><img src="{uri}" alt="glyph"></td>',
            f'<td>{html.escape(r["class"])}</td>',
            f'<td>{html.escape(r["font"])}</td>',
            f'<td class="num">{_fmt(r["nearest_correct"])}</td>',
            f'<td class="num">{_fmt(r["nearest_wrong"])}</td>',
            f'<td class="num {near_cls}">{_fmt(r["nearest_margin"])}</td>',
            f'<td class="num">{_fmt(r["farthest_correct"])}</td>',
            f'<td class="num {gap_cls}">{_fmt(r["class_margin"])}</td>',
            f'<td class="{flag_cls}">{flag}</td>',
            f'<td class="top5">{html.escape(r["top5"])}</td>',
            '</tr>',
        ])

    table.append('</tbody></table>')
    return ''.join(table)


def launch_grand_test(viewer, point_count=16, count_penalty=0.35, top_k=5):
    """Interactive full leave-one-out RMS test over every glyph in the dataset.

    Change Points / count penalty / top-K and press Recalculate. Pairwise distances are
    recomputed only when requested. Sorting the finished table is instant and does not
    recompute descriptors or distances.
    """
    zf, svg_files, _ = viewer.load_dataset()
    files = sorted(svg_files)
    n = len(files)

    if n < 2:
        print('Dataset is too small for a nearest-neighbour test.')
        return None

    points_widget = widgets.IntSlider(
        value=max(4, int(point_count)), min=4, max=96, step=1,
        description='Points:', continuous_update=False,
        layout=widgets.Layout(width='320px'))
    penalty_widget = widgets.FloatSlider(
        value=float(count_penalty), min=0, max=1.5, step=.05,
        description='Count penalty:', continuous_update=False,
        readout_format='.2f', layout=widgets.Layout(width='360px'))
    topk_widget = widgets.IntSlider(
        value=max(1, min(int(top_k), n - 1)), min=1, max=min(20, n - 1), step=1,
        description='Top K:', continuous_update=False,
        layout=widgets.Layout(width='280px'))
    run_button = widgets.Button(description='Recalculate', button_style='primary', icon='refresh')

    sort_widget = widgets.Dropdown(
        options=[
            ('Nearest diff', 'nearest_margin'),
            ('Class gap', 'class_margin'),
            ('Nearest correct', 'nearest_correct'),
            ('Nearest wrong', 'nearest_wrong'),
            ('Farthest correct', 'farthest_correct'),
            ('TOP-1 correct?', 'top1_ok'),
            ('Class', 'class'),
            ('Font', 'font'),
        ],
        value='nearest_margin', description='Sort by:',
        layout=widgets.Layout(width='330px'))
    direction_widget = widgets.ToggleButtons(
        options=[('Ascending', False), ('Descending', True)], value=False,
        description='Order:')

    title = widgets.HTML()
    stage = widgets.HTML()
    progress = widgets.IntProgress(value=0, min=0, max=n, description='Ready:')
    table_output = widgets.Output()
    controls = widgets.HBox([points_widget, penalty_widget, topk_widget, run_button])
    sort_controls = widgets.HBox([sort_widget, direction_widget])

    state = {
        'rows': None,
        'svg_bytes_cache': None,
        'distances': None,
        'files': files,
        'classes': None,
        'fonts': None,
        'top1_accuracy': None,
        'top1_correct': None,
        'count': n,
    }

    display(controls, title, stage, progress, sort_controls, table_output)

    def render_table(*_):
        if state['rows'] is None:
            return
        with table_output:
            clear_output(wait=True)
            display(HTML(_build_table(
                state['rows'], state['svg_bytes_cache'],
                sort_widget.value, direction_widget.value)))

    def run(_=None):
        run_button.disabled = True
        try:
            point_count_now = int(points_widget.value)
            count_penalty_now = float(penalty_widget.value)
            top_k_now = max(1, min(int(topk_widget.value), n - 1))

            title.value = (
                f'<b>RMS grand test</b> — {n} glyphs; top-K={top_k_now}; '
                f'points={point_count_now}; contour-count penalty={count_penalty_now:.2f}'
            )
            stage.value = 'Preparing descriptors…'
            progress.description = 'Descriptors:'
            progress.bar_style = 'info'
            progress.max = n
            progress.value = 0

            descriptors = []
            classes = []
            fonts = []
            svg_bytes_cache = []

            for i, name in enumerate(files, start=1):
                raw = zf.read(name)
                svg_bytes_cache.append(raw)
                descriptors.append(viewer.glyph_descriptor(raw, point_count_now))
                p = PurePosixPath(name)
                classes.append(p.parent.name)
                fonts.append(p.name)
                progress.value = i

            pair_count = n * (n - 1) // 2
            progress.description = 'Pairs:'
            progress.max = pair_count
            progress.value = 0
            stage.value = f'Comparing {pair_count:,} unique glyph pairs…'

            distances = np.full((n, n), np.inf, dtype=float)
            done = 0
            update_every = max(1, pair_count // 500)

            for i in range(n):
                for j in range(i + 1, n):
                    score = viewer.compare_glyph_descriptors(
                        descriptors[i], descriptors[j], count_penalty_now)
                    d = float(score['total'])
                    distances[i, j] = d
                    distances[j, i] = d
                    done += 1
                    if done % update_every == 0 or done == pair_count:
                        progress.value = done

            stage.value = 'Building nearest-neighbour report…'
            progress.description = 'Rows:'
            progress.max = n
            progress.value = 0

            rows = []
            correct_top1 = 0

            for i in range(n):
                order = np.argsort(distances[i])
                order = [int(j) for j in order if j != i and np.isfinite(distances[i, j])]
                nearest = order[:top_k_now]

                same = [j for j in range(n) if j != i and classes[j] == classes[i] and np.isfinite(distances[i, j])]
                wrong = [j for j in range(n) if classes[j] != classes[i] and np.isfinite(distances[i, j])]

                nearest_correct = min((distances[i, j] for j in same), default=np.nan)
                farthest_correct = max((distances[i, j] for j in same), default=np.nan)
                nearest_wrong = min((distances[i, j] for j in wrong), default=np.nan)

                nearest_margin = (
                    nearest_wrong - nearest_correct
                    if np.isfinite(nearest_correct) and np.isfinite(nearest_wrong) else np.nan)
                class_margin = (
                    nearest_wrong - farthest_correct
                    if np.isfinite(farthest_correct) and np.isfinite(nearest_wrong) else np.nan)

                top1_ok = bool(order) and classes[order[0]] == classes[i]
                if top1_ok:
                    correct_top1 += 1

                top5_text = ' · '.join(
                    f'{classes[j]}/{PurePosixPath(files[j]).stem} ({distances[i, j]:.4f})'
                    for j in nearest
                )

                rows.append({
                    'index': i,
                    'class': classes[i],
                    'font': fonts[i],
                    'nearest_correct': nearest_correct,
                    'farthest_correct': farthest_correct,
                    'nearest_wrong': nearest_wrong,
                    'nearest_margin': nearest_margin,
                    'class_margin': class_margin,
                    'top1_ok': top1_ok,
                    'top5': top5_text,
                })
                progress.value = i + 1

            accuracy = correct_top1 / n if n else 0.0
            negative_class = sum(1 for r in rows if np.isfinite(r['class_margin']) and r['class_margin'] < 0)
            negative_nearest = sum(1 for r in rows if np.isfinite(r['nearest_margin']) and r['nearest_margin'] < 0)
            stage.value = (
                f'<b>Done.</b> TOP-1 accuracy: {correct_top1}/{n} = {accuracy:.1%}. '
                f'Negative nearest margins: {negative_nearest}. '
                f'Negative class-gap margins: {negative_class}.'
            )
            progress.bar_style = 'success' if correct_top1 == n else 'warning'

            state.update({
                'rows': rows,
                'svg_bytes_cache': svg_bytes_cache,
                'distances': distances,
                'classes': classes,
                'fonts': fonts,
                'top1_accuracy': accuracy,
                'top1_correct': correct_top1,
            })
            render_table()
        finally:
            run_button.disabled = False

    run_button.on_click(run)
    sort_widget.observe(render_table, names='value')
    direction_widget.observe(render_table, names='value')
    run()
    return state
