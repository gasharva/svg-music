using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;
namespace SvgToMusicXmlPoc.Services;
public sealed class MusicXmlSustainPostProcessor
{
    public void Apply(string path,AnalysisResult a)
    {
        var st=a.Staves.OrderBy(s=>s.Center).ToList();if(st.Count<2)return;
        var gs=new List<(Staff U,Staff L)>();for(int i=0;i+1<st.Count;i+=2)gs.Add((st[i],st[i+1]));
        var marks=SustainGeometry.Find(a,gs);if(marks.Count==0)return;
        var d=XDocument.Load(path);var ms=d.Descendants("measure").ToList();int mi=0,div=1,beats=4,bt=4;
        for(int g=0;g<gs.Count&&mi<ms.Count;g++)
        {
            var b=SustainGeometry.Bounds(a,gs[g]);
            for(int k=0;k+1<b.Count&&mi<ms.Count;k++,mi++)
            {
                var m=ms[mi];Timing(m,ref div,ref beats,ref bt);var dur=Math.Max(1,beats*div*4/Math.Max(1,bt));
                foreach(var x in marks.Where(x=>x.Group==g))
                {
                    if(In(x.Left,b[k],b[k+1],k+2==b.Count))Add(m,"start",x.Left,b[k],b[k+1],dur);
                    if(In(x.Right,b[k],b[k+1],k+2==b.Count))Add(m,"stop",x.Right,b[k],b[k+1],dur);
                }
            }
        }
        d.Save(path);
    }
    static void Add(XElement m,string type,double x,double l,double r,int dur)
    {
        var tag="pe"+"dal";
        var e=new XElement("direction",new XAttribute("placement","below"),
            new XElement("direction-type",new XElement(tag,new XAttribute("type",type),new XAttribute("line","yes"),new XAttribute("sign",type=="start"?"yes":"no"))),
            new XElement("offset",Off(x,l,r,dur)),new XElement("staff",2));
        var a=m.Elements().FirstOrDefault(v=>v.Name!="attributes"&&v.Name!="print");if(a is null)m.Add(e);else a.AddBeforeSelf(e);
    }
    static int Off(double x,double l,double r,int d)=>r<=l?0:Math.Clamp((int)Math.Round(Math.Clamp((x-l)/(r-l),0,1)*d),0,d);
    static bool In(double x,double l,double r,bool last)=>x>=l&&(last?x<=r:x<r);
    static void Timing(XElement m,ref int d,ref int b,ref int bt)
    {
        var a=m.Element("attributes");if(a is null)return;var v=(int?)a.Element("divisions");if(v>0)d=v.Value;
        var t=a.Element("time");if(t is null)return;v=(int?)t.Element("beats");if(v>0)b=v.Value;v=(int?)t.Element("beat-type");if(v>0)bt=v.Value;
    }
}
