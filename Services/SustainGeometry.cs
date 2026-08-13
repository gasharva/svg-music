using SvgToMusicXmlPoc.Models;
namespace SvgToMusicXmlPoc.Services;
internal static class SustainGeometry
{
    internal sealed record Mark(int Group,double Left,double Right);
    internal static List<Mark> Find(AnalysisResult a,IReadOnlyList<(Staff U,Staff L)> gs)
    {
        var r=new List<Mark>();
        for(int g=0;g<gs.Count;g++)foreach(var p in a.DirectPaths)foreach(var c in p.Geometry.Contours)
        {
            if(c.Count<2)continue;var s=gs[g].L;var l=c.Min(v=>v.X);var rr=c.Max(v=>v.X);var t=c.Min(v=>v.Y);var b=c.Max(v=>v.Y);
            var w=(rr-l)/s.Space;var h=(b-t)/s.Space;var y=(t+b)/2;
            if(w<5||w>42||h>2.8||y<s.Bottom+s.Space*1.2||y>s.Bottom+s.Space*9)continue;
            double longH=0;bool hook=false;
            for(int i=1;i<c.Count;i++)
            {
                var dx=Math.Abs(c[i].X-c[i-1].X);var dy=Math.Abs(c[i].Y-c[i-1].Y);
                if(dy<=s.Space*.16)longH=Math.Max(longH,dx);
                if(Math.Max(c[i].X,c[i-1].X)>=rr-s.Space*.35&&dx<=s.Space*.18&&dy>=s.Space*.35)hook=true;
            }
            if(longH<(rr-l)*.55||(!hook&&w<8))continue;
            r.Add(new Mark(g,l,rr));
        }
        return r.GroupBy(x=>(x.Group,L:(int)Math.Round(x.Left/4),R:(int)Math.Round(x.Right/4))).Select(x=>x.First()).ToList();
    }
    internal static List<double> Bounds(AnalysisResult a,(Staff U,Staff L) g)
    {
        var s=(g.U.Space+g.L.Space)/2;
        var xs=a.LineSegments.Where(x=>x.Width<=s*.35&&x.Top<=g.U.Top+s*.45&&x.Bottom>=g.L.Bottom-s*.45).Select(x=>x.CenterX).OrderBy(x=>x).ToList();
        var m=new List<double>();foreach(var x in xs){if(m.Count==0||x-m[^1]>s*.65)m.Add(x);else m[^1]=(m[^1]+x)/2;}
        var left=Math.Min(g.U.Left,g.L.Left);var right=Math.Max(g.U.Right,g.L.Right);var r=new List<double>{left};
        r.AddRange(m.Where(x=>x>left+s*2&&x<right-s*.8));r.Add(right);return r.Distinct().OrderBy(x=>x).ToList();
    }
}
