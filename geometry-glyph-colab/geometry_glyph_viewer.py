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
    return [c for c in merged_contours(paths) if c.is_ring]


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
    """Complex Fourier descriptor of a closed sampled contour, centered and scale-normalized."""
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
        ax.set_xlabel('k')
        ax.set_xticks(ks)
        ax.grid(alpha=.2)
        ax.legend(fontsize=8)
    axes[0].set_ylabel('Re(Zk)')
    axes[1].set_ylabel('Im(Zk)')
    plt.tight_layout()
    plt.show()


def launch():
    zf, svg_files, classes = load_dataset()
    print(f'Loaded {len(svg_files)} SVG files in {len(classes)} classes')

    class_dropdown = widgets.Dropdown(options=classes, description='Class:', layout=widgets.Layout(width='430px'))
    mode_dropdown = widgets.Dropdown(
        options=['Path commands', 'Merged contours', 'Resampled contours'],
        value='Resampled contours', description='Mode:', layout=widgets.Layout(width='290px'))
    point_slider = widgets.IntSlider(value=16, min=4, max=128, step=1, description='Points:', continuous_update=False,
                                     layout=widgets.Layout(width='330px'))
    contour_dropdown = widgets.Dropdown(options=[0], value=0, description='Contour:', layout=widgets.Layout(width='230px'))
    fourier_slider = widgets.IntSlider(value=8, min=1, max=15, step=1, description='Fourier M:', continuous_update=False,
                                       layout=widgets.Layout(width='320px'))
    controls_top = widgets.HBox([class_dropdown, mode_dropdown, point_slider])
    controls_bottom = widgets.HBox([contour_dropdown, fourier_slider])
    updating_controls = {'value': False}

    def render(*_):
        if updating_controls['value']:
            return
        class_name = class_dropdown.value
        mode = mode_dropdown.value
        point_count = point_slider.value
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
        if is_resampled:
            display(controls_bottom)

        print(f'{class_name}: {len(files)} examples — {mode}')
        if mode == 'Path commands':
            print('Commands:', '   '.join(f'{k}={v}' for k, v in COLORS.items()))
        if not files:
            return

        fig, axes = plt.subplots(1, len(files), figsize=(max(4, 3.2 * len(files)), 4), squeeze=False)
        for ax, name in zip(axes[0], files):
            draw_example(ax, zf.read(name), PurePosixPath(name).name, mode, point_count)
        plt.tight_layout()
        plt.show()

        if is_resampled:
            contour_index = contour_dropdown.value
            series = collect_fourier(zf, files, contour_index, point_count)
            print(f'Fourier: contour={contour_index}, points={point_count}, showing k=1..{fourier_slider.value}; centered + RMS scale normalized')
            draw_fourier_charts(series, fourier_slider.value, contour_index)

    class_dropdown.observe(render, names='value')
    mode_dropdown.observe(render, names='value')
    point_slider.observe(render, names='value')
    contour_dropdown.observe(render, names='value')
    fourier_slider.observe(render, names='value')
    render()
