using System.Text.Json;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class SymbolClassifier
{
    private readonly SvgPathGeometry _geometry = new();

    public ClassificationResult Classify(string scorePath, IReadOnlyList<Staff> staves, string catalogPath)
    {
        var scoreDoc = System.Xml.Linq.XDocument.Load(scorePath);
        var source = _geometry.ReadScoreGeometries(scoreDoc);
        var catalog = JsonSerializer.Deserialize<ReferenceCatalog>(File.ReadAllText(catalogPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Не удалось прочитать каталог эталонов");
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(catalogPath))!;
        var refs = catalog.Symbols.Select(r => (Ref:r, Desc:SvgPathGeometry.Describe(_geometry.ReadStandaloneSvg(Path.Combine(baseDir, r.SvgPath))))).ToList();
        var staffSpace = staves.Count > 0 ? staves.Average(s => s.Space) : 1.0;
        var result = new ClassificationResult();
        foreach (var pair in source)
        {
            var desc = SvgPathGeometry.Describe(pair.Value);
            var widthSpaces = desc.Width / staffSpace; var heightSpaces = desc.Height / staffSpace;
            // Cheap pre-filter first. A full Chamfer comparison against the complete
            // Bravura catalog would be unnecessarily expensive.
            var candidates = refs
                .Select(r => (r.Ref, r.Desc, Cheap: CheapScore(desc, widthSpaces, heightSpaces, r.Ref, r.Desc)))
                .OrderByDescending(x => x.Cheap)
                .Take(64)
                .Select(x => (x.Ref, x.Desc));
            var matches = candidates.Select(r => Score(desc, widthSpaces, heightSpaces, r.Ref, r.Desc)).OrderByDescending(x => x.Total).ToList();
            if (matches.Count == 0) continue;
            var best = matches[0];
            result.Symbols.Add(new SymbolClassification(pair.Key, best.Reference.Kind, best.Reference.Id, best.Total,
                best.Shape, best.Size, widthSpaces, heightSpaces, best.Reference.MusicXmlElement, best.Reference.MusicXmlValue));
        }
        result.Symbols.Sort((a,b) => string.CompareOrdinal(a.SymbolId,b.SymbolId));
        return result;
    }


    private static double CheapScore(ShapeDescriptor a,double widthSpaces,double heightSpaces,ReferenceSymbol r,ShapeDescriptor b)
    {
        var aspect = Math.Exp(-Math.Abs(Math.Log(Math.Max(a.AspectRatio,1e-6)/Math.Max(b.AspectRatio,1e-6))));
        var sizeParts = new List<double>();
        if(r.ExpectedWidthInSpaces is double ew) sizeParts.Add(SizeSimilarity(widthSpaces,ew,r.SizeTolerance));
        if(r.ExpectedHeightInSpaces is double eh) sizeParts.Add(SizeSimilarity(heightSpaces,eh,r.SizeTolerance));
        var size = sizeParts.Count==0 ? 0.5 : sizeParts.Average();
        var contours = 1.0/(1+Math.Abs(a.ClosedContourCount-b.ClosedContourCount));
        return 0.45*aspect + 0.45*size + 0.10*contours;
    }

    private static (ReferenceSymbol Reference,double Total,double Shape,double Size) Score(ShapeDescriptor a,double widthSpaces,double heightSpaces,ReferenceSymbol r,ShapeDescriptor b)
    {
        var aspect = Math.Exp(-Math.Abs(Math.Log(Math.Max(a.AspectRatio,1e-6)/Math.Max(b.AspectRatio,1e-6))));
        var fill = Math.Max(0, 1-Math.Abs(a.FillRatio-b.FillRatio));
        var contours = 1.0/(1+Math.Abs(a.ClosedContourCount-b.ClosedContourCount));
        var chamfer = BestOrientedChamfer(a.NormalizedPoints,b.NormalizedPoints);
        var shape = 0.55*Math.Exp(-6*chamfer)+0.2*aspect+0.15*fill+0.1*contours;
        var sizeParts = new List<double>();
        if(r.ExpectedWidthInSpaces is double ew) sizeParts.Add(SizeSimilarity(widthSpaces,ew,r.SizeTolerance));
        if(r.ExpectedHeightInSpaces is double eh) sizeParts.Add(SizeSimilarity(heightSpaces,eh,r.SizeTolerance));
        var size = sizeParts.Count==0 ? 0.5 : sizeParts.Average();
        var total = 0.78*shape+0.22*size;
        return (r,total,shape,size);
    }

    private static double SizeSimilarity(double actual,double expected,double tolerance)
    {
        if(actual<=0||expected<=0) return 0;
        var relative=Math.Abs(Math.Log(actual/expected));
        return Math.Exp(-relative/Math.Max(tolerance,0.05));
    }
    private static double BestOrientedChamfer(IReadOnlyList<PointD> a,IReadOnlyList<PointD> b)
    {
        static IReadOnlyList<PointD> Flip(IReadOnlyList<PointD> p,bool x,bool y) => p.Select(q => new PointD(x ? 1-q.X : q.X, y ? 1-q.Y : q.Y)).ToArray();
        return new[]{b,Flip(b,true,false),Flip(b,false,true),Flip(b,true,true)}.Min(v => SymmetricChamfer(a,v));
    }
    private static double SymmetricChamfer(IReadOnlyList<PointD> a,IReadOnlyList<PointD> b) => (OneWay(a,b)+OneWay(b,a))/2;
    private static double OneWay(IReadOnlyList<PointD> a,IReadOnlyList<PointD> b) => a.Average(p => b.Min(q => Math.Sqrt(Math.Pow(p.X-q.X,2)+Math.Pow(p.Y-q.Y,2))));
}
