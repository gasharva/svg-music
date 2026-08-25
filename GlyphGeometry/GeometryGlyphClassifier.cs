using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Xml.Linq;
using SkiaSharp;

namespace GlyphGeometry;

public sealed record GlyphClassCandidate(string ClassName, double Distance)
{
    public double Confidence => 1.0 / (1.0 + Math.Max(0, Distance));
}

public sealed class GeometryGlyphClassifier
{
    public const int DefaultPointCount = 16;
    private readonly IReadOnlyList<ReferenceGlyph> _references;
    private readonly int _pointCount;

    public GeometryGlyphClassifier(int pointCount = DefaultPointCount)
    {
        _pointCount = pointCount;
        _references = GeometryReferenceCatalog.LoadEmbedded(pointCount);
    }

    public IReadOnlyList<GlyphClassCandidate> Classify(
        IReadOnlyList<IReadOnlyList<Vector2>> contours,
        IEnumerable<string>? allowedClasses = null,
        int top = 5)
    {
        var query = GeometryDescriptorBuilder.FromContours(contours, _pointCount);
        var allowed = allowedClasses?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _references
            .Where(r => allowed is null || allowed.Contains(r.ClassName))
            .GroupBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GlyphClassCandidate(g.Key, g.Average(x => GeometryDistance.Calculate(query, x.Descriptor))))
            .OrderBy(x => x.Distance)
            .Take(top)
            .ToArray();
    }
}

internal sealed record ReferenceGlyph(string ClassName, string FontName, GeometryDescriptor Descriptor);
internal sealed record GeometryDescriptor(double Aspect, IReadOnlyList<ContourDescriptor> Contours, int Holes, int MaxDepth);
internal sealed record ContourDescriptor(
    double PerimeterRatio, double AreaRatio, Vector2 Center, Vector2 Size, int Depth, IReadOnlyList<Vector2> Points);

internal static class GeometryReferenceCatalog
{
    private static readonly object Sync = new();
    private static readonly Dictionary<int, IReadOnlyList<ReferenceGlyph>> Cache = new();

    public static IReadOnlyList<ReferenceGlyph> LoadEmbedded(int pointCount)
    {
        lock (Sync)
        {
            if (Cache.TryGetValue(pointCount, out var cached)) return cached;
            var asm = typeof(GeometryReferenceCatalog).Assembly;
            var resource = asm.GetManifestResourceNames().Single(x => x.EndsWith("dataset.zip", StringComparison.OrdinalIgnoreCase));
            using var stream = asm.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("Embedded dataset.zip not found.");
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, false);
            var result = new List<ReferenceGlyph>();
            foreach (var entry in zip.Entries.Where(x => x.FullName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)))
            {
                using var s = entry.Open();
                var doc = XDocument.Load(s);
                var className = entry.FullName.Replace('\\','/').Split('/').Reverse().Skip(1).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(className)) continue;
                var contours = SvgPathGeometryReader.Read(doc);
                if (contours.Count == 0) continue;
                result.Add(new ReferenceGlyph(className, Path.GetFileName(entry.FullName), GeometryDescriptorBuilder.FromContours(contours, pointCount)));
            }
            return Cache[pointCount] = result;
        }
    }
}

internal static class SvgPathGeometryReader
{
    public static IReadOnlyList<IReadOnlyList<Vector2>> Read(XDocument doc)
    {
        var result = new List<IReadOnlyList<Vector2>>();
        foreach (var element in doc.Descendants().Where(x => x.Name.LocalName.Equals("path", StringComparison.OrdinalIgnoreCase)))
        {
            var d = (string?)element.Attribute("d");
            if (string.IsNullOrWhiteSpace(d)) continue;
            using var path = SKPath.ParseSvgPathData(d);
            if (path is null) continue;
            using var measure = new SKPathMeasure(path, false);
            do
            {
                var length = measure.Length;
                if (length <= 0) continue;
                var n = Math.Max(64, (int)Math.Ceiling(length / 5.0));
                var points = new List<Vector2>(n);
                for (var i = 0; i < n; i++)
                {
                    var distance = length * i / n;
                    if (measure.GetPositionAndTangent(distance, out var p, out _)) points.Add(new Vector2(p.X, p.Y));
                }
                if (points.Count >= 3) result.Add(points);
            } while (measure.NextContour());
        }
        return result;
    }
}

internal static class GeometryDescriptorBuilder
{
    public static GeometryDescriptor FromContours(IReadOnlyList<IReadOnlyList<Vector2>> source, int pointCount)
    {
        var usable = source.Where(x => x.Count >= 3).Select(x => x.ToArray()).ToArray();
        if (usable.Length == 0) throw new InvalidOperationException("No usable contours.");
        var all = usable.SelectMany(x => x).ToArray();
        var minX = all.Min(p => p.X); var maxX = all.Max(p => p.X);
        var minY = all.Min(p => p.Y); var maxY = all.Max(p => p.Y);
        var h = Math.Max(1e-9, maxY - minY); var w = Math.Max(1e-9, maxX - minX);
        var aspect = w / h;

        var normalized = usable.Select(c => c.Select(p => new Vector2((float)((p.X-minX)/h), (float)((p.Y-minY)/h))).ToArray()).ToArray();
        var perimeters = normalized.Select(Perimeter).ToArray();
        var order = Enumerable.Range(0, normalized.Length).OrderByDescending(i => perimeters[i]).ToArray();
        var totalPerimeter = Math.Max(1e-12, perimeters.Sum());
        var bboxArea = Math.Max(1e-12, aspect);
        var depths = new int[normalized.Length];
        for (var i=0;i<normalized.Length;i++)
        {
            var center = Centroid(normalized[i]);
            for (var j=0;j<normalized.Length;j++)
                if (i!=j && Math.Abs(SignedArea(normalized[j])) > Math.Abs(SignedArea(normalized[i])) && Contains(normalized[j], center)) depths[i]++;
        }

        var contours = new List<ContourDescriptor>();
        foreach (var i in order)
        {
            var c = normalized[i];
            var cminX=c.Min(p=>p.X); var cmaxX=c.Max(p=>p.X); var cminY=c.Min(p=>p.Y); var cmaxY=c.Max(p=>p.Y);
            contours.Add(new ContourDescriptor(
                perimeters[i]/totalPerimeter,
                Math.Abs(SignedArea(c))/bboxArea,
                Centroid(c),
                new Vector2(cmaxX-cminX,cmaxY-cminY),
                depths[i],
                Resample(c, pointCount)));
        }
        return new GeometryDescriptor(aspect, contours, depths.Count(d=>d%2==1), depths.DefaultIfEmpty(0).Max());
    }

    private static IReadOnlyList<Vector2> Resample(IReadOnlyList<Vector2> c, int n)
    {
        var lengths = new double[c.Count+1];
        for (var i=0;i<c.Count;i++) lengths[i+1]=lengths[i]+Vector2.Distance(c[i], c[(i+1)%c.Count]);
        var total=lengths[^1]; var result=new Vector2[n];
        for(var k=0;k<n;k++)
        {
            var target=total*k/n; var seg=0;
            while(seg+1<lengths.Length && lengths[seg+1]<target) seg++;
            var a=c[seg%c.Count]; var b=c[(seg+1)%c.Count]; var len=lengths[seg+1]-lengths[seg];
            var t=len<=1e-12?0:(target-lengths[seg])/len; result[k]=Vector2.Lerp(a,b,(float)t);
        }
        return result;
    }

    private static double Perimeter(IReadOnlyList<Vector2> c) => Enumerable.Range(0,c.Count).Sum(i=>Vector2.Distance(c[i],c[(i+1)%c.Count]));
    private static double SignedArea(IReadOnlyList<Vector2> c) { double a=0; for(var i=0;i<c.Count;i++){var p=c[i];var q=c[(i+1)%c.Count];a+=p.X*q.Y-q.X*p.Y;} return a/2; }
    private static Vector2 Centroid(IReadOnlyList<Vector2> c) { var s=Vector2.Zero; foreach(var p in c)s+=p; return s/c.Count; }
    private static bool Contains(IReadOnlyList<Vector2> poly, Vector2 p) { var inside=false; for(int i=0,j=poly.Count-1;i<poly.Count;j=i++){var a=poly[i];var b=poly[j]; if(((a.Y>p.Y)!=(b.Y>p.Y)) && p.X < (b.X-a.X)*(p.Y-a.Y)/(b.Y-a.Y+1e-20f)+a.X) inside=!inside;} return inside; }
}

internal static class GeometryDistance
{
    public static double Calculate(GeometryDescriptor a, GeometryDescriptor b)
    {
        var rms = ContourRms(a,b);
        var count = Math.Abs(a.Contours.Count-b.Contours.Count)/(double)Math.Max(1,Math.Max(a.Contours.Count,b.Contours.Count));
        var perimeters = PadRms(a.Contours.Select(x=>x.PerimeterRatio), b.Contours.Select(x=>x.PerimeterRatio));
        var areas = PadRms(a.Contours.Select(x=>x.AreaRatio), b.Contours.Select(x=>x.AreaRatio));
        var bbox = PadVectorRms(a.Contours.Select(x=>x.Size), b.Contours.Select(x=>x.Size));
        var centers = PadVectorRms(a.Contours.Select(x=>x.Center), b.Contours.Select(x=>x.Center));
        var depth = PadRms(a.Contours.Select(x=>(double)x.Depth), b.Contours.Select(x=>(double)x.Depth));
        var topology = (Math.Abs(a.Holes-b.Holes)/(double)Math.Max(1,Math.Max(a.Contours.Count,b.Contours.Count)) + Math.Abs(a.MaxDepth-b.MaxDepth)/(double)Math.Max(1,Math.Max(a.MaxDepth,b.MaxDepth)) + depth)/3.0;
        return (rms + Math.Abs(a.Aspect-b.Aspect) + count + perimeters + areas + bbox + centers + topology)/8.0;
    }

    private static double ContourRms(GeometryDescriptor a, GeometryDescriptor b)
    {
        var n=Math.Max(a.Contours.Count,b.Contours.Count); if(n==0)return 0;
        double sum=0,weight=0;
        for(var i=0;i<n;i++)
        {
            if(i>=a.Contours.Count || i>=b.Contours.Count){sum+=0.35;weight+=1;continue;}
            var w=(a.Contours[i].PerimeterRatio+b.Contours[i].PerimeterRatio)/2; sum+=CyclicRms(a.Contours[i].Points,b.Contours[i].Points)*w; weight+=w;
        }
        return sum/Math.Max(1e-12,weight);
    }

    private static double CyclicRms(IReadOnlyList<Vector2> a,IReadOnlyList<Vector2> b)
    {
        var n=Math.Min(a.Count,b.Count); var best=double.PositiveInfinity;
        for(var shift=0;shift<n;shift++){double sum=0;for(var i=0;i<n;i++){var d=a[i]-b[(i+shift)%n];sum+=d.LengthSquared();}best=Math.Min(best,Math.Sqrt(sum/n));}
        return best;
    }

    private static double PadRms(IEnumerable<double> aa,IEnumerable<double> bb){var a=aa.ToArray();var b=bb.ToArray();var n=Math.Max(a.Length,b.Length);if(n==0)return 0;double s=0;for(var i=0;i<n;i++){var x=i<a.Length?a[i]:0;var y=i<b.Length?b[i]:0;s+=(x-y)*(x-y);}return Math.Sqrt(s/n);}
    private static double PadVectorRms(IEnumerable<Vector2> aa,IEnumerable<Vector2> bb){var a=aa.ToArray();var b=bb.ToArray();var n=Math.Max(a.Length,b.Length);if(n==0)return 0;double s=0;for(var i=0;i<n;i++){var x=i<a.Length?a[i]:Vector2.Zero;var y=i<b.Length?b[i]:Vector2.Zero;var d=x-y;s+=d.LengthSquared();}return Math.Sqrt(s/(2*n));}
}
