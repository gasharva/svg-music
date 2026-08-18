using System.Globalization;
using System.Net;
using System.Text;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>Diagnostic gallery for MusicSymbolResolver candidates.</summary>
public sealed class MusicSymbolSvgExporter
{
    public const string DirectoryName = "music-symbols";
    public const string GalleryFileName = "music-symbols.html";
    public const string AnnotatorFileName = "annotate.html";

    private readonly bool _drawPrimitiveBounds;

    /// <param name="drawPrimitiveBounds">
    /// Draw the PrimitiveResolver bbox scaffold into exported SVGs. Off by default so the files can
    /// be reused directly for recognition/debugging without gray diagnostic geometry contaminating them.
    /// </param>
    public MusicSymbolSvgExporter(bool drawPrimitiveBounds = false)
    {
        _drawPrimitiveBounds = drawPrimitiveBounds;
    }

    public MusicSymbolExportResult Export(MusicSymbolResolution resolution, string itemDirectory)
    {
        var outputDirectory = Path.Combine(itemDirectory, DirectoryName);
        if (Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, recursive: true);
        Directory.CreateDirectory(outputDirectory);

        var counters = new Dictionary<(int? Part, int Measure), int>();
        var splitCounters = new Dictionary<int, int>();
        var items = new List<MusicSymbolExportItem>();

        foreach (var candidate in resolution.Candidates)
        {
            var key = (candidate.PartNumber, candidate.MeasureNumber);
            counters.TryGetValue(key, out var index);
            index++;
            counters[key] = index;

            var prefix = candidate.PartNumber is null
                ? $"measure{candidate.MeasureNumber}"
                : $"part{candidate.PartNumber}-measure{candidate.MeasureNumber}";

            string fileName;
            if (candidate.ParentCandidateId is int parentId)
            {
                splitCounters.TryGetValue(parentId, out var splitIndex);
                splitIndex++;
                splitCounters[parentId] = splitIndex;
                fileName = $"{prefix}-candidate{parentId}-split{splitIndex}.svg";
            }
            else
            {
                fileName = $"{prefix}-{index}.svg";
            }

            WriteSvg(Path.Combine(outputDirectory, fileName), candidate);
            items.Add(new MusicSymbolExportItem(fileName, candidate, index));
        }

        var galleryPath = Path.Combine(itemDirectory, GalleryFileName);
        WriteGallery(galleryPath, items, annotate: false);
        WriteGallery(Path.Combine(itemDirectory, AnnotatorFileName), items, annotate: true);
        return new MusicSymbolExportResult(outputDirectory, galleryPath, items);
    }

    private void WriteSvg(string path, MusicSymbolCandidate candidate)
    {
        var b = candidate.PhysicalBounds;
        var extent = Math.Max(b.Width, b.Height);
        var pad = Math.Max(extent * 0.35, 2.0);
        var width = Math.Max(b.Width + 2 * pad, 1);
        var height = Math.Max(b.Height + 2 * pad, 1);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(b.Left - pad)} {F(b.Top - pad)} {F(width)} {F(height)}\">");

        if (_drawPrimitiveBounds)
        {
            foreach (var box in candidate.PrimitiveBounds)
            {
                sb.AppendLine($"  <rect x=\"{F(box.Left)}\" y=\"{F(box.Top)}\" width=\"{F(box.Width)}\" height=\"{F(box.Height)}\" fill=\"none\" stroke=\"#999\" stroke-width=\"0.45\" stroke-dasharray=\"1.2 0.8\"/>");
            }
        }

        foreach (var smooth in candidate.SmoothPaths)
        {
            var transform = string.IsNullOrWhiteSpace(smooth.Transform)
                ? string.Empty
                : $" transform=\"{H(smooth.Transform)}\"";
            sb.AppendLine($"  <path fill=\"black\" fill-rule=\"evenodd\"{transform} d=\"{H(smooth.PathData)}\"/>");
        }
        sb.AppendLine("</svg>");
        File.WriteAllText(path, sb.ToString());
    }

    private void WriteGallery(string path, IReadOnlyList<MusicSymbolExportItem> items, bool annotate)
    {
        var resolved = items.Where(x => x.Candidate.SmoothPaths.Count > 0).ToArray();
        var unresolved = items.Where(x => x.Candidate.SmoothPaths.Count == 0).ToArray();
        var roots = resolved.Where(x => !x.Candidate.IsDerived).ToArray();
        var derived = resolved.Where(x => x.Candidate.IsDerived).ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Music symbol candidates</title>");
        sb.AppendLine("<style>body{font-family:system-ui,Arial,sans-serif;margin:24px;background:#fafafa;color:#222}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr));gap:14px}.card{background:#fff;border:1px solid #ddd;border-radius:8px;padding:10px}.shape{height:150px;display:flex;align-items:center;justify-content:center;background:#f6f6f6}.shape img{max-width:100%;max-height:140px}.name{font:12px ui-monospace,Consolas,monospace;margin-top:8px}.meta{font-size:12px;color:#666;margin-top:4px}.src{font:10px ui-monospace,Consolas,monospace;color:#888;margin-top:5px;word-break:break-all}.bad{color:#a00}.split{border-color:#9ab6d8}h2{margin-top:36px}</style>");
        if (annotate)
        {
            sb.AppendLine("<style>.annotator-bar{position:sticky;top:0;z-index:1000;background:#fff;border:1px solid #ccc;border-radius:10px;padding:12px 14px;margin:0 0 18px;box-shadow:0 2px 12px #0002;display:flex;gap:10px;align-items:center;flex-wrap:wrap}.annotator-bar label{font-weight:600}.annotator-bar input[type=text]{padding:7px 9px;border:1px solid #bbb;border-radius:6px;min-width:180px}.annotator-bar button{padding:8px 12px;border:1px solid #999;border-radius:6px;background:#f4f4f4;cursor:pointer}.annotator-bar button.primary{background:#222;color:white;border-color:#222}.annotator-status{font-size:12px;color:#666}.class-input{box-sizing:border-box;width:100%;margin-top:9px;padding:7px 8px;border:1px solid #bbb;border-radius:6px;font:13px system-ui,Arial,sans-serif}.card.is-labeled{outline:2px solid #8bb98b;outline-offset:1px}.annotator-count{font-weight:600}</style>");
        }
        sb.AppendLine("</head><body>");
        sb.AppendLine($"<h1>MusicSymbolResolver</h1><p>{items.Count} candidates total: {roots.Length} resolved bbox roots, {derived.Length} ink-split alternatives, {unresolved.Length} unresolved. Primitive bbox drawing in exported SVGs is {(_drawPrimitiveBounds ? "ON" : "OFF")}.</p>");

        sb.AppendLine("<h2>Original bbox candidates</h2><div class=\"grid\">");
        WriteCards(sb, roots);
        sb.AppendLine("</div>");

        if (derived.Length > 0)
        {
            sb.AppendLine($"<h2>Ink-split alternatives ({derived.Length})</h2><p>Generated only when a bbox candidate contains multiple disconnected positive-area ink components. The original parent candidate above is always preserved.</p><div class=\"grid\">");
            WriteCards(sb, derived);
            sb.AppendLine("</div>");
        }

        if (unresolved.Length > 0)
        {
            sb.AppendLine($"<h2>Unresolved ({unresolved.Length})</h2><p class=\"bad\">Kept for diagnostics, but excluded from the main visual result.</p><div class=\"grid\">");
            WriteCards(sb, unresolved);
            sb.AppendLine("</div>");
        }

        if (annotate)
            WriteAnnotatorScript(sb);

        sb.AppendLine("</body></html>");
        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteCards(StringBuilder sb, IReadOnlyList<MusicSymbolExportItem> items)
    {
        foreach (var item in items)
        {
            var c = item.Candidate;
            var src = $"{DirectoryName}/{item.FileName}";
            var css = c.IsDerived ? "card split" : "card";
            sb.AppendLine($"<div class=\"{css}\">");
            sb.AppendLine($"<a class=\"shape\" href=\"{src}\"><img src=\"{src}\" loading=\"lazy\"></a>");
            sb.AppendLine($"<div class=\"name\">{H(item.FileName)}</div>");
            sb.AppendLine($"<div class=\"meta\">#{c.Id} · {H(c.LogicalLabel)} · primitives={c.PrimitiveIds.Count} · smooth paths={c.SmoothPaths.Count}</div>");
            if (c.ParentCandidateId is int parentId)
                sb.AppendLine($"<div class=\"meta\"><b>ink split of parent #{parentId}</b></div>");
            if (c.SmoothPaths.Count == 0)
                sb.AppendLine("<div class=\"meta bad\">No retained smooth path resolved</div>");
            sb.AppendLine($"<div class=\"src\">primitive ids: {string.Join(",", c.PrimitiveIds)}<br>{string.Join("<br>", c.Sources.Select(x => H(x.ElementAddress ?? x.Anchor)))}</div>");
            sb.AppendLine("</div>");
        }
    }

    private static void WriteAnnotatorScript(StringBuilder sb)
    {
        sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/jszip@3.10.1/dist/jszip.min.js\"></script>");
        sb.AppendLine("""
<script>
(() => {
  const pageKey = 'svg-annotator:' + location.pathname;
  let selectedFiles = new Map();

  function safePart(s) {
    return (s || '').trim().replace(/[\\/:*?"<>|]+/g, '-').replace(/\s+/g, '-').replace(/^-+|-+$/g, '');
  }

  function state() {
    try { return JSON.parse(localStorage.getItem(pageKey) || '{}'); } catch { return {}; }
  }

  function saveState() {
    const labels = {};
    document.querySelectorAll('.card').forEach((card, i) => {
      const v = card.querySelector('.class-input')?.value.trim();
      if (v) labels[i] = v;
    });
    localStorage.setItem(pageKey, JSON.stringify({ id: document.querySelector('#dataset-id').value, labels }));
    refresh();
  }

  function refresh() {
    let n = 0;
    document.querySelectorAll('.card').forEach(card => {
      const on = !!card.querySelector('.class-input')?.value.trim();
      card.classList.toggle('is-labeled', on);
      if (on) n++;
    });
    document.querySelector('#annotator-count').textContent = n;
  }

  const old = state();
  const bar = document.createElement('div');
  bar.className = 'annotator-bar';
  bar.innerHTML = `
    <label>ID набора: <input id="dataset-id" type="text" placeholder="например, id1"></label>
    <button id="export-zip" class="primary">Скачать ZIP</button>
    <span>Размечено: <span id="annotator-count" class="annotator-count">0</span></span>
    <span id="annotator-status" class="annotator-status">ZIP содержит только карточки с классом</span>
    <input id="svg-folder" type="file" webkitdirectory directory multiple accept="image/svg+xml,.svg" hidden>
    <datalist id="known-classes"></datalist>`;
  document.body.insertBefore(bar, document.body.firstChild);
  document.querySelector('#dataset-id').value = old.id || '';
  document.querySelector('#dataset-id').addEventListener('input', saveState);

  const datalist = document.querySelector('#known-classes');
  function updateKnownClasses() {
    const values = [...new Set([...document.querySelectorAll('.class-input')].map(x => x.value.trim()).filter(Boolean))].sort();
    datalist.innerHTML = values.map(v => `<option value="${v.replace(/&/g,'&amp;').replace(/"/g,'&quot;')}"></option>`).join('');
  }

  document.querySelectorAll('.card').forEach((card, i) => {
    const input = document.createElement('input');
    input.className = 'class-input';
    input.type = 'text';
    input.placeholder = 'класс (пусто = пропустить)';
    input.setAttribute('list', 'known-classes');
    input.value = old.labels?.[i] || '';
    input.addEventListener('input', () => { saveState(); updateKnownClasses(); });
    card.appendChild(input);
  });
  updateKnownClasses();
  refresh();

  const picker = document.querySelector('#svg-folder');
  function pickFolder() {
    return new Promise((resolve) => {
      const onChange = () => { picker.removeEventListener('change', onChange); resolve([...picker.files]); };
      picker.addEventListener('change', onChange, {once:true});
      picker.click();
    });
  }

  document.querySelector('#export-zip').addEventListener('click', async () => {
    const id = safePart(document.querySelector('#dataset-id').value);
    const status = document.querySelector('#annotator-status');
    if (!id) { alert('Сначала введи ID набора сверху.'); return; }

    const chosen = [...document.querySelectorAll('.card')].map(card => {
      const clsRaw = card.querySelector('.class-input')?.value.trim();
      const src = card.querySelector('img')?.getAttribute('src');
      return clsRaw && src ? { cls: safePart(clsRaw), src, base: src.split('/').pop() } : null;
    }).filter(Boolean);
    if (!chosen.length) { alert('Не размечено ни одного SVG.'); return; }

    if (!selectedFiles.size) {
      status.textContent = 'Выбери папку с SVG…';
      const files = await pickFolder();
      if (!files.length) { status.textContent = 'Папка не выбрана'; return; }
      selectedFiles = new Map();
      for (const f of files) {
        selectedFiles.set(f.name, f);
        if (f.webkitRelativePath) selectedFiles.set(f.webkitRelativePath.replace(/\\/g,'/'), f);
      }
    }

    const zip = new JSZip();
    const counters = new Map();
    const missing = [];
    for (const item of chosen) {
      const file = selectedFiles.get(item.base) || selectedFiles.get(item.src);
      if (!file) { missing.push(item.base); continue; }
      const num = (counters.get(item.cls) || 0) + 1;
      counters.set(item.cls, num);
      const suffix = num === 1 ? '' : '-' + num;
      zip.folder(item.cls).file(`${item.cls}-${id}${suffix}.svg`, file);
    }

    if (missing.length) {
      alert('Не нашёл в выбранной папке SVG:\n' + missing.slice(0, 20).join('\n') + (missing.length > 20 ? `\n… ещё ${missing.length}` : ''));
      status.textContent = `Не найдено файлов: ${missing.length}`;
      return;
    }

    status.textContent = 'Собираю ZIP…';
    const blob = await zip.generateAsync({type:'blob'});
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `symbols-${id}.zip`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
    status.textContent = `Готово: ${chosen.length} SVG`;
  });
})();
</script>
""");
    }

    private static string H(string value) => WebUtility.HtmlEncode(value);
    private static string F(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}

public sealed record MusicSymbolExportItem(
    string FileName,
    MusicSymbolCandidate Candidate,
    int Index);

public sealed record MusicSymbolExportResult(
    string OutputDirectory,
    string GalleryPath,
    IReadOnlyList<MusicSymbolExportItem> Items);
