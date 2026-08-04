namespace SvgToMusicXmlPoc.Models;

public sealed class ConversionPerformance
{
    public double ParseSvgMs { get; set; }
    public double DetectStavesMs { get; set; }
    public double ReadInstancesMs { get; set; }
    public double LoadCatalogMs { get; set; }
    public double ClassifyMs { get; set; }
    public double RecognizeSemanticsMs { get; set; }
    public double WriteMusicXmlMs { get; set; }
    public double TotalMs { get; set; }
    public int GlyphInstances { get; set; }
    public int UniqueGeometries { get; set; }
    public int CatalogGlyphs { get; set; }
    public long MaskComparisons { get; set; }
    public long VectorComparisons { get; set; }
    public bool CatalogCacheHit { get; set; }
}

public sealed class ClassifierPerformance
{
    public double LoadCatalogMs { get; set; }
    public double ClassifyMs { get; set; }
    public int GlyphInstances { get; set; }
    public int UniqueGeometries { get; set; }
    public int CatalogGlyphs { get; set; }
    public long MaskComparisons { get; set; }
    public long VectorComparisons { get; set; }
    public bool CatalogCacheHit { get; set; }
}
