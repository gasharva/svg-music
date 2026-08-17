using System.Globalization;
using System.Net;
using System.Text;
using GlyphPcaGallery.Models;

namespace GlyphPcaGallery.Services;

public static class GalleryBuilder
{
    public static void Build(string outputDirectory, IReadOnlyList<GlyphAnalysis> items, TimeSpan totalElapsed, int parallelism)
    {
        Directory.CreateDirectory(outputDirectory);
        var assets = Path.Combine(outputDirectory, "assets");
        Directory.CreateDirectory(assets);

        foreach (var item in items.Where(x => x.Error is null))
            File.Copy(item.SourcePath, Path.Combine(assets, item.AssetName), overwrite: true);

        var successful = items.Where(x => x.Error is null).ToArray();
        var micros = successful.Select(x => (double)x.ElapsedMicroseconds).Order().ToArray();
        var avgMs = micros.Length == 0 ? 0 : micros.Average() / 1000.0;
        var p95Ms = micros.Length == 0 ? 0 : Percentile(micros, .95) / 1000.0;

        var html = new StringBuilder();
        html.Append("""
<!doctype html><html><head><meta charset="utf-8"><title>Glyph PCA gallery</title>
<style>
:root{font-family:system-ui,sans-serif;color:#222;background:#f5f5f5}body{margin:20px}.toolbar{position:sticky;top:0;z-index:10;background:#fff;border:1px solid #ddd;padding:12px 16px;border-radius:10px;box-shadow:0 2px 10px #0001;margin-bottom:16px}.stats{display:flex;gap:20px;flex-wrap:wrap;margin-bottom:10px;font-size:14px}.controls{display:flex;gap:16px;align-items:center;flex-wrap:wrap}#threshold{width:260px}.gallery{display:grid;grid-template-columns:repeat(auto-fill,minmax(255px,1fr));gap:12px}.card{background:white;border:5px solid #999;border-radius:10px;padding:10px;transition:opacity .15s}.card.rejected{opacity:.28}.card.error{border-color:#111!important;background:#fee}.preview{height:180px;display:flex;align-items:center;justify-content:center;background:#fafafa}.preview img{max-width:95%;max-height:170px}.name{font-size:12px;overflow-wrap:anywhere;margin:8px 0;color:#555}.best{font-size:22px;font-weight:700}.conf{font-size:15px}.metrics{font-size:12px;color:#555;margin:4px 0 8px}.matches{width:100%;border-collapse:collapse;font-size:12px}.matches td{border-top:1px solid #eee;padding:3px}.matches td:nth-child(2){text-align:right;font-variant-numeric:tabular-nums}
</style></head><body>
""");

        html.Append($"""
<div class="toolbar"><div class="stats"><b>{successful.Length} glyphs</b><span>errors: {items.Count-successful.Length}</span><span>wall time: {totalElapsed.TotalSeconds:F2}s</span><span>avg analysis: {avgMs:F2} ms</span><span>p95: {p95Ms:F2} ms</span><span>parallelism: {parallelism}</span></div>
<div class="controls"><label>accept threshold: <input id="threshold" type="range" min="0" max="1" value="0.50" step="0.01"> <b id="thresholdValue">0.50</b></label><span id="accepted"></span><label>sort: <select id="sort"><option value="low">lowest confidence first</option><option value="high">highest confidence first</option><option value="name">filename</option></select></label></div></div><div class="gallery" id="gallery">
""");

        foreach (var item in items)
        {
            if (item.Error is not null)
            {
                html.Append($"<div class=\"card error\" data-confidence=\"0\" data-name=\"{H(Path.GetFileName(item.SourcePath))}\"><div class=\"name\">{H(Path.GetFileName(item.SourcePath))}</div><b>ERROR</b><div>{H(item.Error)}</div></div>");
                continue;
            }

            var hue = 120.0 * item.Confidence;
            html.Append($"<div class=\"card\" style=\"border-color:hsl({hue.ToString("F0",CultureInfo.InvariantCulture)},80%,42%)\" data-confidence=\"{item.Confidence.ToString("F6",CultureInfo.InvariantCulture)}\" data-name=\"{H(Path.GetFileName(item.SourcePath))}\">");
            html.Append($"<div class=\"preview\"><img loading=\"lazy\" src=\"assets/{Uri.EscapeDataString(item.AssetName)}\"></div><div class=\"name\">{H(Path.GetFileName(item.SourcePath))}</div><div class=\"best\">{H(item.Matches[0].Class)}</div><div class=\"conf\">confidence <b>{item.Confidence:P0}</b></div>");
            html.Append($"<div class=\"metrics\">distance={item.BestDistance:F3} &nbsp; margin={item.Margin:F3} &nbsp; rel={item.RelativeMargin:P0} &nbsp; abs={item.AbsoluteConfidence:P0} &nbsp; {item.ElapsedMicroseconds/1000.0:F2}ms</div><table class=\"matches\">");
            foreach (var match in item.Matches)
                html.Append($"<tr><td>{H(match.Class)}</td><td>{match.Distance:F4}</td><td>{H(match.Prototype)}</td></tr>");
            html.Append("</table></div>");
        }

        html.Append("""
</div><script>
const gallery=document.getElementById('gallery'),slider=document.getElementById('threshold'),tv=document.getElementById('thresholdValue'),accepted=document.getElementById('accepted'),sort=document.getElementById('sort');
function update(){const t=Number(slider.value);tv.textContent=t.toFixed(2);const cards=[...gallery.children];let ok=0,total=0;for(const c of cards){const conf=Number(c.dataset.confidence||0),err=c.classList.contains('error');c.classList.toggle('rejected',conf<t);if(!err){total++;if(conf>=t)ok++;}}accepted.textContent=`accepted: ${ok}/${total}`;cards.sort((a,b)=>sort.value==='name'?a.dataset.name.localeCompare(b.dataset.name):(sort.value==='high'?Number(b.dataset.confidence)-Number(a.dataset.confidence):Number(a.dataset.confidence)-Number(b.dataset.confidence)));cards.forEach(c=>gallery.appendChild(c));}
slider.addEventListener('input',update);sort.addEventListener('change',update);update();
</script></body></html>
""");

        File.WriteAllText(Path.Combine(outputDirectory, "index.html"), html.ToString(), Encoding.UTF8);
    }

    private static string H(string value) => WebUtility.HtmlEncode(value);
    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        var pos=(sorted.Length-1)*p; var i=(int)Math.Floor(pos); var f=pos-i;
        return i+1<sorted.Length ? sorted[i]*(1-f)+sorted[i+1]*f : sorted[i];
    }
}
