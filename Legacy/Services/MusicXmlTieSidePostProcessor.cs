using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;
namespace SvgToMusicXmlPoc.Services;

public sealed class MusicXmlTieSidePostProcessor
{
    public void Apply(string path, AnalysisResult a)
    {
        var d=XDocument.Load(path); var st=a.Staves.OrderBy(s=>s.Center).ToList();
        var q=a.Staves.ToDictionary(s=>s.Index,s=>new Queue<RecognizedEvent>(a.Events.Where(e=>e.StaffIndex==s.Index&&(e.Step is not null||e.Kind.StartsWith("rest-"))).OrderBy(e=>e.X).ThenByDescending(e=>e.Y)));
        var sys=-1;
        foreach(var m in d.Descendants("measure"))
        {
            if(m.Element("attributes")?.Elements("clef").Any()==true) sys++; if(sys<0) sys=0;
            foreach(var n in m.Elements("note"))
            {
                var si=sys*2+((int?)n.Element("staff")??1)-1; if(si<0||si>=st.Count) continue;
                var s=st[si]; if(!q.TryGetValue(s.Index,out var z)||z.Count==0) continue; var e=z.Dequeue();
                var tied=n.Element("notations")?.Elements("tied").FirstOrDefault(x=>(string?)x.Attribute("type")=="start"); if(tied is null) continue;
                var side=Find(e,s,a); if(side is not null) tied.SetAttributeValue("placement",side);
            }
        }
        d.Save(path);
    }
    static string? Find(RecognizedEvent e,Staff s,AnalysisResult a)
    {
        var best=double.MaxValue; double cy=0;
        foreach(var p in a.DirectPaths) foreach(var c in p.Geometry.Contours)
        {
            if(c.Count is <12 or >180) continue; var l=c.Min(v=>v.X); var r=c.Max(v=>v.X); var t=c.Min(v=>v.Y); var b=c.Max(v=>v.Y);
            var w=(r-l)/s.Space; var h=(b-t)/s.Space; if(w is <1.5 or >36||h is <.15 or >3.2) continue;
            var score=Math.Min(Math.Abs(e.X-l)+Math.Abs(e.Y-EndY(c,l,s.Space)),Math.Abs(e.X-r)+Math.Abs(e.Y-EndY(c,r,s.Space)))/s.Space;
            if(score<best){best=score;cy=(t+b)/2;}
        }
        return best<=4?(cy>e.Y?"below":"above"):null;
    }
    static double EndY(IReadOnlyList<PointD> p,double x,double sp){var n=p.Where(v=>Math.Abs(v.X-x)<=sp*.04).ToList();return n.Count>0?n.Average(v=>v.Y):p.OrderBy(v=>Math.Abs(v.X-x)).First().Y;}
}
