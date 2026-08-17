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
:root{font-family:system-ui,sans-serif;color:#222;background:#f5f5f5}body{margin:20px}.toolbar{position:sticky;top:0;z-index:10;background:#fff;border:1px solid #ddd;padding:12px 16px;border-radius:10px;box-shadow:0 2px 10px #0001;margin-bottom:16px}.stats{display:flex;gap:20px;flex-wrap:wrap;margin-bottom:10px;font-size:14px}.controls{display:flex;gap:16px;align-items:center;flex-wrap:wrap}#multiplier{width:280px}.legend{font-size:12px;color:#666}.gallery{display:grid;grid-template-columns:repeat(auto-fill,minmax(270px,1fr));gap:12px}.card{background:white;border:5px solid #999;border-radius:10px;padding:10px;transition:opacity .15s,border-color .15s}.card.rejected{opacity:.40}.card.error{border-color:#111!important;background:#fee}.preview{height:180px;display:flex;align-items:center;justify-content:center;background:#fafafa}.preview img{max-width:95%;max-height:170px}.name{font-size:12px;overflow-wrap:anywhere;margin:8px 0;color:#555}.best{font-size:22px;font-weight:700}.decision{font-size:15px;font-weight:700}.decision.reject{color:#b00020}.decision.accept{color:#087f23}.metrics{font-size:12px;color:#555;margin:5px 0 8px;line-height:1.55}.metric-strong{font-weight:700;color:#222}.matches{width:100%;border-collapse:collapse;font-size:12px}.matches td{border-top:1px solid #eee;padding:3px}.matches td:nth-child(2){text-align:right;font-variant-numeric:tabular-nums}
</style></head><body>
""");

        html.Append($"""
<div class="toolbar"><div class="stats"><b>{successful.Length} glyphs</b><span>errors: {items.Count-successful.Length}</span><span>wall time: {totalElapsed.TotalSeconds:F2}s</span><span>avg analysis: {avgMs:F2} ms</span><span>p95: {p95Ms:F2} ms</span><span>parallelism: {parallelism}</span></div>
<div class="controls"><label>class limit multiplier: <input id="multiplier" type="range" min="0.50" max="2.50" value="1.00" step="0.05"> <b id="multiplierValue">1.00×</b></label><span id="accepted"></span><label>sort: <select id="sort"><option value="risk">highest risk first</option><option value="safe">lowest risk first</option><option value="distance">largest normalized distance first</option><option value="ratio">largest d1/d2 first</option><option value="name">filename</option></select></label></div><div class="legend">Acceptance: normalized class distance ≤ multiplier AND d1/d2 ≤ ratio limit. Border: green = safely inside both limits, red = at/outside either limit.</div></div><div class="gallery" id="gallery">
""");

        foreach (var item in items)
        {
            if (item.Error is not null)
            {
                html.Append($"<div class=\"card error\" data-name=\"{H(Path.GetFileName(item.SourcePath))}\"><div class=\"name\">{H(Path.GetFileName(item.SourcePath))}</div><b>ERROR</b><div>{H(item.Error)}</div></div>");
                continue;
            }

            var norm = item.NormalizedDistance.ToString("F8", CultureInfo.InvariantCulture);
            var ratio = item.DistanceRatio.ToString("F8", CultureInfo.InvariantCulture);
            var ratioLimit = item.RatioThreshold.ToString("F8", CultureInfo.InvariantCulture);
            var initialRisk = item.Risk.ToString("F8", CultureInfo.InvariantCulture);

            html.Append($"<div class=\"card\" data-norm=\"{norm}\" data-ratio=\"{ratio}\" data-ratio-limit=\"{ratioLimit}\" data-risk=\"{initialRisk}\" data-name=\"{H(Path.GetFileName(item.SourcePath))}\">");
            html.Append($"<div class=\"preview\"><img loading=\"lazy\" src=\"assets/{Uri.EscapeDataString(item.AssetName)}\"></div><div class=\"name\">{H(Path.GetFileName(item.SourcePath))}</div><div class=\"best\">{H(item.Matches[0].Class)}</div><div class=\"decision\"><span class=\"decision-text\"></span> &nbsp; risk <b class=\"risk-value\"></b></div>");
            html.Append($"<div class=\"metrics\">distance=<span class=\"metric-strong\">{item.BestDistance:F3}</span> &nbsp; class limit={item.ClassDistanceThreshold:F3}<br>normalized=<span class=\"norm-value metric-strong\">{item.NormalizedDistance:F2}</span> &nbsp; d1/d2=<span class=\"metric-strong\">{item.DistanceRatio:F3}</span> / {item.RatioThreshold:F2}<br>second={item.SecondDistance:F3} &nbsp; margin={item.Margin:F3} &nbsp; {item.ElapsedMicroseconds/1000.0:F2}ms</div><table class=\"matches\">");
            foreach (var match in item.Matches)
                html.Append($"<tr><td>{H(match.Class)}</td><td>{match.Distance:F4}</td><td>{H(match.Prototype)}</td></tr>");
            html.Append("</table></div>");
        }

        html.Append("""
</div><script>
const gallery=document.getElementById('gallery'),slider=document.getElementById('multiplier'),mv=document.getElementById('multiplierValue'),accepted=document.getElementById('accepted'),sort=document.getElementById('sort');
function adjustedRisk(card,m){const nd=Number(card.dataset.norm)/m,ratio=Number(card.dataset.ratio),rl=Number(card.dataset.ratioLimit)||0.5;return Math.max(nd,ratio/rl);}
function borderColor(risk){const x=Math.min(1,Math.max(0,risk));const hue=120*(1-x);return `hsl(${hue.toFixed(0)},80%,42%)`;}
function update(){const m=Number(slider.value);mv.textContent=m.toFixed(2)+'×';const cards=[...gallery.children];let ok=0,total=0;for(const c of cards){if(c.classList.contains('error'))continue;total++;const risk=adjustedRisk(c,m),isOk=risk<=1;c.dataset.adjustedRisk=String(risk);c.style.borderColor=borderColor(risk);c.classList.toggle('rejected',!isOk);const d=c.querySelector('.decision-text');d.textContent=isOk?'ACCEPT':'REJECT';d.parentElement.classList.toggle('accept',isOk);d.parentElement.classList.toggle('reject',!isOk);c.querySelector('.risk-value').textContent=risk.toFixed(2);c.querySelector('.norm-value').textContent=(Number(c.dataset.norm)/m).toFixed(2);if(isOk)ok++;}accepted.textContent=`accepted: ${ok}/${total}`;cards.sort((a,b)=>{if(sort.value==='name')return a.dataset.name.localeCompare(b.dataset.name);if(a.classList.contains('error'))return 1;if(b.classList.contains('error'))return -1;if(sort.value==='safe')return Number(a.dataset.adjustedRisk)-Number(b.dataset.adjustedRisk);if(sort.value==='distance')return Number(b.dataset.norm)/m-Number(a.dataset.norm)/m;if(sort.value==='ratio')return Number(b.dataset.ratio)-Number(a.dataset.ratio);return Number(b.dataset.adjustedRisk)-Number(a.dataset.adjustedRisk);});cards.forEach(c=>gallery.appendChild(c));}
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
