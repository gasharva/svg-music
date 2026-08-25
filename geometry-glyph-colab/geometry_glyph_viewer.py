import io
import re
import zipfile
from pathlib import PurePosixPath
import xml.etree.ElementTree as ET

import matplotlib.pyplot as plt
import numpy as np
import requests
import ipywidgets as widgets
from IPython.display import display, clear_output
from svgpathtools import Line, CubicBezier, QuadraticBezier, Arc
from shapely.geometry import LineString, MultiLineString, GeometryCollection
from shapely.ops import unary_union, linemerge

DATASET_URL = 'https://raw.githubusercontent.com/gasharva/svg-music/master/References/dataset.zip'

COLORS = {
    'M': '#7f7f7f', 'L': '#1f77b4', 'H': '#17becf', 'V': '#2ca02c',
    'C': '#ff7f0e', 'S': '#ffbb78', 'Q': '#9467bd', 'T': '#c5b0d5',
    'A': '#d62728', 'Z': '#8c564b'
}
CONTOUR_COLORS = plt.get_cmap('tab20').colors
TOKEN_RE = re.compile(r'[AaCcHhLlMmQqSsTtVvZz]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?')
PARAMS = {'M':2,'L':2,'H':1,'V':1,'C':6,'S':4,'Q':4,'T':2,'A':7,'Z':0}


def load_dataset():
    r = requests.get(DATASET_URL, timeout=60)
    r.raise_for_status()
    zf = zipfile.ZipFile(io.BytesIO(r.content))
    svg_files = [n for n in zf.namelist() if n.lower().endswith('.svg')]
    classes = sorted({PurePosixPath(n).parent.name for n in svg_files})
    return zf, svg_files, classes


def parse_command_chunks(d):
    tokens = TOKEN_RE.findall(d or '')
    out = []
    i = 0
    cmd = None
    while i < len(tokens):
        if tokens[i].isalpha():
            cmd = tokens[i]
            i += 1
            if cmd.upper() == 'Z':
                out.append((cmd, []))
                continue
        if cmd is None:
            raise ValueError('Path data starts without a command')
        n = PARAMS[cmd.upper()]
        if i + n > len(tokens):
            break
        vals = list(map(float, tokens[i:i+n]))
        out.append((cmd, vals))
        i += n
        if cmd.upper() == 'M':
            cmd = 'l' if cmd.islower() else 'L'
    return out


def command_segments(d):
    cur = np.array([0.0, 0.0])
    sub_start = None
    prev_ctrl = None
    result = []

    def abspt(x, y, rel):
        p = np.array([x, y], dtype=float)
        return cur + p if rel else p

    for raw, v in parse_command_chunks(d):
        c = raw.upper()
        rel = raw.islower()
        start = cur.copy()

        if c == 'M':
            cur = abspt(v[0], v[1], rel)
            sub_start = cur.copy()
            prev_ctrl = None
            result.append(('M', start, cur.copy(), None))
        elif c == 'L':
            end = abspt(v[0], v[1], rel)
            seg = Line(complex(*start), complex(*end))
            result.append(('L', start, end, seg)); cur = end; prev_ctrl = None
        elif c == 'H':
            end = np.array([cur[0] + v[0] if rel else v[0], cur[1]])
            seg = Line(complex(*start), complex(*end))
            result.append(('H', start, end, seg)); cur = end; prev_ctrl = None
        elif c == 'V':
            end = np.array([cur[0], cur[1] + v[0] if rel else v[0]])
            seg = Line(complex(*start), complex(*end))
            result.append(('V', start, end, seg)); cur = end; prev_ctrl = None
        elif c == 'C':
            p1 = abspt(v[0], v[1], rel); p2 = abspt(v[2], v[3], rel); end = abspt(v[4], v[5], rel)
            seg = CubicBezier(complex(*start), complex(*p1), complex(*p2), complex(*end))
            result.append(('C', start, end, seg)); cur = end; prev_ctrl = p2
        elif c == 'S':
            p1 = start if prev_ctrl is None else 2 * start - prev_ctrl
            p2 = abspt(v[0], v[1], rel); end = abspt(v[2], v[3], rel)
            seg = CubicBezier(complex(*start), complex(*p1), complex(*p2), complex(*end))
            result.append(('S', start, end, seg)); cur = end; prev_ctrl = p2
        elif c == 'Q':
            p1 = abspt(v[0], v[1], rel); end = abspt(v[2], v[3], rel)
            seg = QuadraticBezier(complex(*start), complex(*p1), complex(*end))
            result.append(('Q', start, end, seg)); cur = end; prev_ctrl = p1
        elif c == 'T':
            p1 = start if prev_ctrl is None else 2 * start - prev_ctrl
            end = abspt(v[0], v[1], rel)
            seg = QuadraticBezier(complex(*start), complex(*p1), complex(*end))
            result.append(('T', start, end, seg)); cur = end; prev_ctrl = p1
        elif c == 'A':
            rx, ry, rot, large, sweep, x, y = v
            end = abspt(x, y, rel)
            try:
                seg = Arc(complex(*start), complex(rx, ry), rot, bool(large), bool(sweep), complex(*end))
            except Exception:
                seg = Line(complex(*start), complex(*end))
            result.append(('A', start, end, seg)); cur = end; prev_ctrl = None
        elif c == 'Z' and sub_start is not None:
            end = sub_start.copy()
            seg = Line(complex(*start), complex(*end))
            result.append(('Z', start, end, seg)); cur = end; prev_ctrl = None

    return result


def sample_seg(seg, n=40):
    if seg is None:
        return np.empty((0, 2))
    pts = [seg.point(t) for t in np.linspace(0, 1, n)]
    return np.array([[p.real, p.imag] for p in pts])


def parse_svg(svg_bytes):
    root = ET.fromstring(svg_bytes)
    return [el.get('d') for el in root.iter() if el.tag.split('}')[-1] == 'path' and el.get('d')]


def all_points(paths):
    pts = []
    for d in paths:
        for _, _, end, seg in command_segments(d):
            pts.extend(sample_seg(seg, 40) if seg is not None else [end])
    return np.asarray(pts, dtype=float) if pts else np.zeros((0, 2))


def geometry_parts(g):
    if g is None or g.is_empty:
        return []
    if isinstance(g, LineString):
        return [g]
    if isinstance(g, (MultiLineString, GeometryCollection)):
        out = []
        for x in g.geoms:
            out.extend(geometry_parts(x))
        return out
    return []


def snap_points(points, eps):
    return np.round(points / eps) * eps if eps > 0 else points


def merged_contours(paths, sample_points=32, snap_fraction=0.0005):
    pts = all_points(paths)
    if len(pts) == 0:
        return []
    h = max(pts[:, 1].max() - pts[:, 1].min(), 1e-9)
    eps = h * snap_fraction
    lines = []
    for d in paths:
        for cmd, _, _, seg in command_segments(d):
            if cmd == 'M' or seg is None:
                continue
            q = snap_points(sample_seg(seg, sample_points), eps)
            if len(q) >= 2 and np.linalg.norm(q[-1] - q[0]) > 0:
                lines.append(LineString(q))
    if not lines:
        return []
    merged = linemerge(unary_union(lines))
    parts = geometry_parts(merged)
    return sorted(parts, key=lambda x: x.length, reverse=True)


def closed_contours(paths):
    # Contour order in source SVG is irrelevant. Canonical order = decreasing perimeter.
    return sorted([c for c in merged_contours(paths) if c.is_ring], key=lambda c: c.length, reverse=True)


def resample_closed_contour(contour, count):
    """Uniform arc-length samples; index 0 is the topmost SVG point (minimum Y)."""
    if count < 3 or contour.length <= 0:
        return np.empty((0, 2))
    distances = np.linspace(0.0, contour.length, count, endpoint=False)
    pts = np.array([[contour.interpolate(d).x, contour.interpolate(d).y] for d in distances], dtype=float)
    min_y = pts[:, 1].min()
    candidates = np.flatnonzero(np.isclose(pts[:, 1], min_y, rtol=0, atol=max(contour.length, 1.0) * 1e-9))
    start = candidates[np.argmin(pts[candidates, 0])] if len(candidates) else int(np.argmin(pts[:, 1]))
    return np.roll(pts, -int(start), axis=0)


def fourier_descriptor(points):
    if len(points) == 0:
        return np.array([], dtype=complex)
    z = points[:, 0].astype(float) + 1j * points[:, 1].astype(float)
    z = z - z.mean()
    rms = np.sqrt(np.mean(np.abs(z) ** 2))
    if rms > 1e-12:
        z = z / rms
    return np.fft.fft(z) / len(z)


def norm_factory(pts):
    xmin, ymin = pts.min(axis=0); xmax, ymax = pts.max(axis=0)
    h = max(ymax - ymin, 1e-9); w = max(xmax - xmin, 1e-9)
    def norm(p):
        p = np.asarray(p, dtype=float)
        return np.column_stack(((p[:, 0] - xmin) / h, (p[:, 1] - ymin) / h))
    return norm, w / h


def draw_background(ax, paths, norm):
    for d in paths:
        for _, _, _, seg in command_segments(d):
            if seg is None:
                continue
            q = norm(sample_seg(seg, 60))
            ax.plot(q[:, 0], q[:, 1], linewidth=5, alpha=.10)


def draw_commands(ax, paths, norm):
    for d in paths:
        for cmd, _, end, seg in command_segments(d):
            color = COLORS.get(cmd.upper(), 'black')
            if cmd.upper() == 'M':
                q = norm(np.array([end]))[0]
                ax.scatter([q[0]], [q[1]], s=46, color=color, zorder=5)
                continue
            if seg is not None:
                q = norm(sample_seg(seg, 40))
                ax.plot(q[:, 0], q[:, 1], linewidth=2.2, color=color, zorder=3)
            qe = norm(np.array([end]))[0]
            ax.scatter([qe[0]], [qe[1]], s=34, color=color, edgecolors='white', linewidths=.5, zorder=6)


def draw_contours(ax, paths, norm):
    contours = merged_contours(paths)
    for i, c in enumerate(contours):
        q = norm(np.asarray(c.coords)); color = CONTOUR_COLORS[i % len(CONTOUR_COLORS)]
        ax.plot(q[:, 0], q[:, 1], linewidth=3, color=color, zorder=3)
        ax.scatter([q[0, 0]], [q[0, 1]], s=45, color=color, edgecolors='white', linewidths=.6, zorder=5)
    closed = sum(1 for c in contours if c.is_ring)
    ax.text(.02, .02, f'contours={len(contours)}  closed={closed}', transform=ax.transAxes, fontsize=8, va='bottom')


def draw_resampled(ax, paths, norm, point_count):
    closed = closed_contours(paths)
    for i, c in enumerate(closed):
        pts = resample_closed_contour(c, point_count)
        if len(pts) == 0:
            continue
        q = norm(pts)
        loop = np.vstack([q, q[0]])
        color = CONTOUR_COLORS[i % len(CONTOUR_COLORS)]
        ax.plot(loop[:, 0], loop[:, 1], linewidth=2.4, color=color, zorder=3)
        ax.scatter(q[:, 0], q[:, 1], s=45, color=color, edgecolors='white', linewidths=.7, zorder=5)
        ax.scatter([q[0, 0]], [q[0, 1]], s=105, color=color, edgecolors='black', linewidths=1.0, zorder=6)
    ax.text(.02, .02, f'closed={len(closed)}  points/contour={point_count}', transform=ax.transAxes, fontsize=8, va='bottom')


def draw_example(ax, svg_bytes, title, mode, point_count):
    paths = parse_svg(svg_bytes)
    pts = all_points(paths)
    if len(pts) == 0:
        ax.set_title(title); ax.axis('off'); return
    norm, aspect = norm_factory(pts)
    draw_background(ax, paths, norm)
    if mode == 'Path commands':
        draw_commands(ax, paths, norm)
    elif mode == 'Merged contours':
        draw_contours(ax, paths, norm)
    else:
        draw_resampled(ax, paths, norm, point_count)
    ax.set_xlim(-.05, aspect + .05)
    ax.set_ylim(1.05, -.05)
    ax.set_aspect('equal')
    ax.axis('off')
    ax.set_title(title, fontsize=10)


def collect_fourier(zf, files, contour_index, point_count):
    result = []
    for name in files:
        paths = parse_svg(zf.read(name))
        contours = closed_contours(paths)
        if contour_index >= len(contours):
            continue
        pts = resample_closed_contour(contours[contour_index], point_count)
        coeffs = fourier_descriptor(pts)
        result.append((PurePosixPath(name).name, coeffs))
    return result


def draw_fourier_charts(series, component_count, contour_index):
    if not series:
        print(f'No samples contain contour {contour_index}.')
        return
    m = min(component_count, min(len(c) for _, c in series) - 1)
    if m <= 0:
        return
    ks = np.arange(1, m + 1)
    fig, axes = plt.subplots(1, 2, figsize=(14, 4.2))
    for name, coeffs in series:
        label = PurePosixPath(name).stem
        axes[0].plot(ks, coeffs[1:m+1].real, marker='o', linewidth=1.5, label=label)
        axes[1].plot(ks, coeffs[1:m+1].imag, marker='o', linewidth=1.5, label=label)
    axes[0].axhline(0, linewidth=.7, alpha=.4)
    axes[1].axhline(0, linewidth=.7, alpha=.4)
    axes[0].set_title(f'Contour {contour_index} — Fourier real components')
    axes[1].set_title(f'Contour {contour_index} — Fourier imaginary components')
    for ax in axes:
        ax.set_xlabel('k'); ax.set_xticks(ks); ax.grid(alpha=.2); ax.legend(fontsize=8)
    axes[0].set_ylabel('Re(Zk)'); axes[1].set_ylabel('Im(Zk)')
    plt.tight_layout(); plt.show()


def launch():
    zf, svg_files, classes = load_dataset()
    print(f'Loaded {len(svg_files)} SVG files in {len(classes)} classes')

    class_dropdown = widgets.Dropdown(options=classes, description='Class:', layout=widgets.Layout(width='430px'))
    mode_dropdown = widgets.Dropdown(options=['Path commands', 'Merged contours', 'Resampled contours'], value='Resampled contours', description='Mode:', layout=widgets.Layout(width='290px'))
    point_slider = widgets.IntSlider(value=16, min=4, max=128, step=1, description='Points:', continuous_update=False, layout=widgets.Layout(width='330px'))
    contour_dropdown = widgets.Dropdown(options=[0], value=0, description='Contour:', layout=widgets.Layout(width='230px'))
    fourier_slider = widgets.IntSlider(value=8, min=1, max=15, step=1, description='Fourier M:', continuous_update=False, layout=widgets.Layout(width='320px'))
    controls_top = widgets.HBox([class_dropdown, mode_dropdown, point_slider])
    controls_bottom = widgets.HBox([contour_dropdown, fourier_slider])
    updating_controls = {'value': False}

    def render(*_):
        if updating_controls['value']:
            return
        class_name = class_dropdown.value; mode = mode_dropdown.value; point_count = point_slider.value
        files = sorted([n for n in svg_files if PurePosixPath(n).parent.name == class_name])
        max_contours = 0
        if mode == 'Resampled contours' and files:
            for name in files:
                max_contours = max(max_contours, len(closed_contours(parse_svg(zf.read(name)))))
        updating_controls['value'] = True
        try:
            new_options = list(range(max_contours)) if max_contours else [0]
            old_value = contour_dropdown.value
            contour_dropdown.options = new_options
            contour_dropdown.value = old_value if old_value in new_options else 0
            fourier_slider.max = max(1, point_count - 1)
            if fourier_slider.value > fourier_slider.max:
                fourier_slider.value = fourier_slider.max
        finally:
            updating_controls['value'] = False
        clear_output(wait=True)
        is_resampled = mode == 'Resampled contours'
        point_slider.layout.display = '' if is_resampled else 'none'
        contour_dropdown.layout.display = '' if is_resampled else 'none'
        fourier_slider.layout.display = '' if is_resampled else 'none'
        display(controls_top)
        if is_resampled: display(controls_bottom)
        print(f'{class_name}: {len(files)} examples — {mode}')
        if mode == 'Path commands': print('Commands:', '   '.join(f'{k}={v}' for k, v in COLORS.items()))
        if not files: return
        fig, axes = plt.subplots(1, len(files), figsize=(max(4, 3.2 * len(files)), 4), squeeze=False)
        for ax, name in zip(axes[0], files):
            draw_example(ax, zf.read(name), PurePosixPath(name).name, mode, point_count)
        plt.tight_layout(); plt.show()
        if is_resampled:
            contour_index = contour_dropdown.value
            series = collect_fourier(zf, files, contour_index, point_count)
            print(f'Fourier: contour={contour_index}, points={point_count}, showing k=1..{fourier_slider.value}; centered + RMS scale normalized')
            draw_fourier_charts(series, fourier_slider.value, contour_index)

    class_dropdown.observe(render, names='value'); mode_dropdown.observe(render, names='value')
    point_slider.observe(render, names='value'); contour_dropdown.observe(render, names='value'); fourier_slider.observe(render, names='value')
    render()


# ---------- RMS nearest-neighbour experiment ----------

def _signed_area(points):
    if len(points) < 3:
        return 0.0
    x = points[:, 0]; y = points[:, 1]
    return 0.5 * np.sum(x * np.roll(y, -1) - np.roll(x, -1) * y)


def _canonical_direction(points):
    """Use one winding direction for every contour, then put the visually topmost point first."""
    if len(points) == 0:
        return points
    q = points.copy()
    if _signed_area(q) < 0:
        q = q[::-1].copy()
    min_y = q[:, 1].min()
    candidates = np.flatnonzero(np.isclose(q[:, 1], min_y, rtol=0, atol=1e-10))
    start = candidates[np.argmin(q[candidates, 0])] if len(candidates) else int(np.argmin(q[:, 1]))
    return np.roll(q, -int(start), axis=0)


def glyph_descriptor(svg_bytes, point_count=16):
    """All contours in one glyph coordinate system, normalized by whole-glyph bbox height."""
    paths = parse_svg(svg_bytes)
    raw = all_points(paths)
    if len(raw) == 0:
        return {'contours': [], 'perimeters': [], 'aspect': 0.0, 'paths': paths}
    xmin, ymin = raw.min(axis=0); xmax, ymax = raw.max(axis=0)
    height = max(ymax - ymin, 1e-9)
    aspect = (xmax - xmin) / height

    contours = closed_contours(paths)  # already sorted by decreasing perimeter
    result = []
    perimeters = []
    for contour in contours:
        pts = resample_closed_contour(contour, point_count)
        if len(pts) == 0:
            continue
        pts = np.column_stack(((pts[:, 0] - xmin) / height, (pts[:, 1] - ymin) / height))
        pts = _canonical_direction(pts)
        result.append(pts)
        perimeters.append(contour.length / height)
    return {'contours': result, 'perimeters': perimeters, 'aspect': aspect, 'paths': paths}


def cyclic_rms(a, b):
    """Best pointwise RMS over every cyclic starting-point shift."""
    if len(a) != len(b) or len(a) == 0:
        return float('inf'), 0
    best = float('inf'); best_shift = 0
    for shift in range(len(a)):
        br = np.roll(b, shift, axis=0)
        d = np.sqrt(np.mean(np.sum((a - br) ** 2, axis=1)))
        if d < best:
            best = float(d); best_shift = shift
    return best, best_shift


def compare_glyph_descriptors(a, b, count_penalty=0.35):
    """Perimeter-sorted contour comparison with weighted cyclic RMS + explicit contour-count penalty."""
    na = len(a['contours']); nb = len(b['contours']); matched = min(na, nb)
    details = []
    weights = []
    for i in range(matched):
        d, shift = cyclic_rms(a['contours'][i], b['contours'][i])
        wa = a['perimeters'][i] if i < len(a['perimeters']) else 1.0
        wb = b['perimeters'][i] if i < len(b['perimeters']) else 1.0
        w = max(1e-9, (wa + wb) / 2.0)
        details.append({'index': i, 'rms': d, 'shift': shift, 'weight': w})
        weights.append(w)
    shape_distance = (sum(x['rms'] * x['weight'] for x in details) / sum(weights)) if weights else 0.0
    contour_penalty = abs(na - nb) * count_penalty
    total = shape_distance + contour_penalty
    return {
        'total': total,
        'shape': shape_distance,
        'count_penalty': contour_penalty,
        'contours_a': na,
        'contours_b': nb,
        'details': details,
    }


def _draw_descriptor(ax, descriptor, title, subtitle=None):
    all_pts = [p for p in descriptor['contours'] if len(p)]
    if not all_pts:
        ax.set_title(title); ax.axis('off'); return
    for i, pts in enumerate(all_pts):
        color = CONTOUR_COLORS[i % len(CONTOUR_COLORS)]
        loop = np.vstack([pts, pts[0]])
        ax.plot(loop[:, 0], loop[:, 1], linewidth=2.2, color=color)
        ax.scatter(pts[:, 0], pts[:, 1], s=22, color=color, edgecolors='white', linewidths=.4, zorder=3)
    xmax = max(p[:, 0].max() for p in all_pts)
    ax.set_xlim(-.05, max(xmax + .05, descriptor['aspect'] + .05)); ax.set_ylim(1.05, -.05)
    ax.set_aspect('equal'); ax.axis('off')
    ax.set_title(title, fontsize=10)
    if subtitle:
        ax.text(.5, -.04, subtitle, transform=ax.transAxes, ha='center', va='top', fontsize=8)


def launch_knn():
    """Interactive top-K nearest glyphs over the whole dataset using perimeter-sorted cyclic RMS."""
    zf, svg_files, classes = load_dataset()
    files_by_class = {c: sorted([n for n in svg_files if PurePosixPath(n).parent.name == c]) for c in classes}
    descriptor_cache = {}

    def descriptor(name, point_count):
        key = (name, point_count)
        if key not in descriptor_cache:
            descriptor_cache[key] = glyph_descriptor(zf.read(name), point_count)
        return descriptor_cache[key]

    class_dropdown = widgets.Dropdown(options=classes, description='Query class:', layout=widgets.Layout(width='430px'))
    font_dropdown = widgets.Dropdown(options=[], description='Font:', layout=widgets.Layout(width='330px'))
    topk_slider = widgets.IntSlider(value=10, min=1, max=30, step=1, description='Top K:', continuous_update=False, layout=widgets.Layout(width='300px'))
    points_slider = widgets.IntSlider(value=16, min=8, max=64, step=1, description='Points:', continuous_update=False, layout=widgets.Layout(width='300px'))
    penalty_slider = widgets.FloatSlider(value=.35, min=0, max=1.5, step=.05, description='Count penalty:', continuous_update=False, readout_format='.2f', layout=widgets.Layout(width='350px'))
    controls1 = widgets.HBox([class_dropdown, font_dropdown, topk_slider])
    controls2 = widgets.HBox([points_slider, penalty_slider])
    updating = {'value': False}

    def refresh_fonts():
        files = files_by_class[class_dropdown.value]
        options = [(PurePosixPath(n).name, n) for n in files]
        old = font_dropdown.value
        font_dropdown.options = options
        if old not in [v for _, v in options] and options:
            font_dropdown.value = options[0][1]

    def render(*_):
        if updating['value'] or not font_dropdown.value:
            return
        query_name = font_dropdown.value
        point_count = points_slider.value
        count_penalty = penalty_slider.value
        query = descriptor(query_name, point_count)

        scored = []
        for name in svg_files:
            if name == query_name:
                continue
            candidate = descriptor(name, point_count)
            score = compare_glyph_descriptors(query, candidate, count_penalty)
            scored.append((score['total'], name, score, candidate))
        scored.sort(key=lambda x: x[0])
        nearest = scored[:topk_slider.value]

        clear_output(wait=True)
        display(controls1); display(controls2)
        qclass = PurePosixPath(query_name).parent.name
        qfont = PurePosixPath(query_name).name
        print(f'Query: {qclass}/{qfont} — {len(query["contours"])} contours, sorted by perimeter')
        print(f'Metric: whole-glyph bbox normalization + canonical winding + cyclic RMS; points={point_count}; contour-count penalty={count_penalty:.2f}')

        cols = min(4, len(nearest) + 1)
        rows = int(np.ceil((len(nearest) + 1) / cols))
        fig, axes = plt.subplots(rows, cols, figsize=(4.0 * cols, 4.4 * rows), squeeze=False)
        flat_axes = axes.ravel()
        _draw_descriptor(flat_axes[0], query, f'QUERY\n{qclass}/{qfont}', f'{len(query["contours"])} contours')

        for rank, (_, name, score, candidate) in enumerate(nearest, start=1):
            cls = PurePosixPath(name).parent.name
            font = PurePosixPath(name).name
            pieces = [f'c{x["index"]}={x["rms"]:.4f}' for x in score['details']]
            if score['count_penalty'] > 0:
                pieces.append(f'count={score["count_penalty"]:.3f}')
            subtitle = f'TOTAL={score["total"]:.4f}\n' + '  '.join(pieces)
            _draw_descriptor(flat_axes[rank], candidate, f'#{rank}  {cls}\n{font}', subtitle)

        for ax in flat_axes[len(nearest)+1:]:
            ax.axis('off')
        plt.tight_layout(); plt.show()

        print('\nRanking:')
        for rank, (_, name, score, _) in enumerate(nearest, start=1):
            cls = PurePosixPath(name).parent.name; font = PurePosixPath(name).name
            breakdown = ', '.join(f'c{x["index"]}={x["rms"]:.4f}' for x in score['details']) or 'no matched contours'
            extra = f', count penalty={score["count_penalty"]:.3f}' if score['count_penalty'] else ''
            print(f'{rank:2d}. {cls}/{font}: total={score["total"]:.4f} (shape={score["shape"]:.4f}; {breakdown}{extra})')

    def on_class_change(*_):
        updating['value'] = True
        try:
            refresh_fonts()
        finally:
            updating['value'] = False
        render()

    class_dropdown.observe(on_class_change, names='value')
    font_dropdown.observe(render, names='value')
    topk_slider.observe(render, names='value')
    points_slider.observe(render, names='value')
    penalty_slider.observe(render, names='value')
    refresh_fonts()
    render()
