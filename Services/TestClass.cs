using System.Globalization;
using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;
namespace SvgToMusicXmlPoc.Services;

public sealed class MusicXmlRestYPostProcessor
{
    public void Apply(string path, AnalysisResult a)
    {
        var d=XDocument.Load(path);
        var staves=a.Staves.OrderBy(s=>s.Center).ToList();
        var q=a.Staves.ToDictionary(s=>s.Index,s=>new Queue<RecognizedEvent>(a.Events.Where(e=>e.StaffIndex==s.Index&&(e.Step is not null||e.Kind.StartsWith("rest-"))).OrderBy(e=>e.X).ThenByDescending(e=>e.Y)));
        var sys=-1;
        foreach(var m in d.Descendants("measure"))
        {
            if(m.Element("attributes")?.Elements("clef").Any()==true) sys++;
            if(sys<0) sys=0;
            var pair=sys*2;
            foreach(var n in m.Elements("note"))
            {
                var sn=(int?)n.Element("staff")??1;
                var si=pair+sn-1;
                if(si<0||si>=staves.Count) continue;
                var staff=staves[si];
                if(!q.TryGetValue(staff.Index,out var queue)||queue.Count==0) continue;
                var e=queue.Dequeue();
                if(n.Element("rest") is null||!e.Kind.StartsWith("rest-")) continue;
                var y=(staff.Top-e.Y)*10.0/Math.Max(staff.Space,.001);
                n.SetAttributeValue("default-y",y.ToString("0.###",CultureInfo.InvariantCulture));
            }
        }
        d.Save(path);
    }
}
