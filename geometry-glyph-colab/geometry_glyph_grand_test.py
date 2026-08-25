import base64
import html
from pathlib import PurePosixPath

import numpy as np
import ipywidgets as widgets
from IPython.display import HTML, display


def _svg_data_uri(svg_bytes):
    encoded = base64.b64encode(svg_bytes).decode('ascii')
    return f'data:image/svg+xml;base64,{encoded}'


def _fmt(value):
    if value is None or not np.isfinite(value):
        return '—'
    return f'{value:.4f}'


def launch_grand_test(viewer, point_count=16, count_penalty=0.35, top_k=5):
    """Run a full leave-one-out nearest-neighbour test over every glyph in the dataset.

    Metrics are exactly the same as viewer.launch_knn(): whole-glyph bbox normalization,
    closed contours sorted by decreasing perimeter, canonical winding, uniform arc-length
    resampling, cyclic RMS per contour, perimeter-weighted aggregation, and contour-count
    penalty.
    """
    zf, svg_files, _ = viewer.load_dataset()
    files = sorted(svg_files)
    n = len(files)

    if n < 2:
        print('Dataset is too small for a nearest-neighbour test.')
        return

    top_k = max(1, min(int(top_k), n - 1))
    point_count = max(4, int(point_count))
    count_penalty = float(count_penalty)

    title = widgets.HTML(
        value=(
            f'<b>RMS grand test</b> — {n} glyphs; top-K={top_k}; '
            f'points={point_count}; contour-count penalty={count_penalty:.2f}'
        )
    )
    stage = widgets.HTML(value='Preparing descriptors…')
    progress = widgets.IntProgress(value=0, min=0, max=n, description='Descriptors:', bar_style='info')
    display(title, stage, progress)

    descriptors = []
    classes = []
    fonts = []
    svg_bytes_cache = []

    for i, name in enumerate(files, start=1):
        raw = zf.read(name)
        svg_bytes_cache.append(raw)
        descriptors.append(viewer.glyph_descriptor(raw, point_count))
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
                descriptors[i], descriptors[j], count_penalty)
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
        nearest = order[:top_k]

        same = [j for j in range(n) if j != i and classes[j] == classes[i] and np.isfinite(distances[i, j])]
        wrong = [j for j in range(n) if classes[j] != classes[i] and np.isfinite(distances[i, j])]

        farthest_correct = max((distances[i, j] for j in same), default=np.nan)
        nearest_wrong = min((distances[i, j] for j in wrong), default=np.nan)
        margin = nearest_wrong - farthest_correct if np.isfinite(farthest_correct) and np.isfinite(nearest_wrong) else np.nan

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
            'farthest_correct': farthest_correct,
            'nearest_wrong': nearest_wrong,
            'margin': margin,
            'top1_ok': top1_ok,
            'top5': top5_text,
        })
        progress.value = i + 1

    # Put failures first, then the smallest class-separation margin.
    rows.sort(key=lambda r: (r['top1_ok'], r['margin'] if np.isfinite(r['margin']) else float('inf')))

    accuracy = correct_top1 / n if n else 0.0
    negative_margins = sum(1 for r in rows if np.isfinite(r['margin']) and r['margin'] < 0)
    stage.value = (
        f'<b>Done.</b> TOP-1 accuracy: {correct_top1}/{n} = {accuracy:.1%}. '
        f'Negative class-gap margins: {negative_margins}.'
    )
    progress.bar_style = 'success' if correct_top1 == n else 'warning'

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
        '</style>',
        '<table class="rms-grand">',
        '<thead><tr>',
        '<th>Glyph</th><th>Class</th><th>Font</th>',
        '<th>Farthest correct</th><th>Nearest wrong</th><th>Diff</th>',
        '<th>TOP-1 correct?</th><th>TOP-5 nearest</th>',
        '</tr></thead><tbody>',
    ]

    for r in rows:
        i = r['index']
        cls = 'ok' if r['top1_ok'] else 'fail'
        flag_cls = 'yes' if r['top1_ok'] else 'no'
        flag = 'ДА' if r['top1_ok'] else 'НЕТ'
        uri = _svg_data_uri(svg_bytes_cache[i])
        table.extend([
            f'<tr class="{cls}">',
            f'<td><img src="{uri}" alt="glyph"></td>',
            f'<td>{html.escape(r["class"])}</td>',
            f'<td>{html.escape(r["font"])}</td>',
            f'<td class="num">{_fmt(r["farthest_correct"])}</td>',
            f'<td class="num">{_fmt(r["nearest_wrong"])}</td>',
            f'<td class="num">{_fmt(r["margin"])}</td>',
            f'<td class="{flag_cls}">{flag}</td>',
            f'<td class="top5">{html.escape(r["top5"])}</td>',
            '</tr>',
        ])

    table.append('</tbody></table>')
    display(HTML(''.join(table)))

    return {
        'files': files,
        'classes': classes,
        'fonts': fonts,
        'distances': distances,
        'rows': rows,
        'top1_accuracy': accuracy,
        'top1_correct': correct_top1,
        'count': n,
    }
