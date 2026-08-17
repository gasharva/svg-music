namespace GlyphPcaGallery.Models;

public sealed class GlyphModel
{
    public int Version { get; set; }
    public NormalizationOptions Normalization { get; set; } = new();
    public SdfOptions Sdf { get; set; } = new();
    public PcaModel Pca { get; set; } = new();
    public Calibration Calibration { get; set; } = new();
    public List<GlyphReference> References { get; set; } = [];
}

public sealed class NormalizationOptions
{
    public string Mode { get; set; } = "pca_rotate";
    public int BoundarySamples { get; set; } = 512;
    public double TargetRadius { get; set; } = 0.8;
    public int SdfBoundarySamples { get; set; } = 1024;
}

public sealed class SdfOptions
{
    public int GridSize { get; set; } = 32;
    public double GridExtent { get; set; } = 1.0;
    public double Clip { get; set; } = 0.30;
}

public sealed class PcaModel
{
    public int ComponentsCount { get; set; }
    public double[] ExplainedVarianceRatio { get; set; } = [];
    public double[] Mean { get; set; } = [];
    public double[][] Components { get; set; } = [];
}

public sealed class Calibration
{
    public double NearestSameP95 { get; set; }
    public double NearestSameP99 { get; set; }
    public double NearestWrongP05 { get; set; }
    public double NearestWrongP01 { get; set; }
    public double MarginP05 { get; set; }
    public double ClassThresholdMultiplier { get; set; } = 1.25;
    public double RatioThreshold { get; set; } = 0.50;
    public Dictionary<string, ClassCalibration> Classes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ClassCalibration
{
    public double NearestSameMedian { get; set; }
    public double NearestSameP95 { get; set; }
    public double NearestSameMax { get; set; }
    public double DistanceThreshold { get; set; }
}

public sealed class GlyphReference
{
    public string Class { get; set; } = "";
    public string Source { get; set; } = "";
    public double[] Fingerprint { get; set; } = [];
}

public sealed record ClassMatch(string Class, double Distance, string Prototype);

public sealed record GlyphAnalysis(
    string SourcePath,
    string AssetName,
    IReadOnlyList<ClassMatch> Matches,
    double BestDistance,
    double SecondDistance,
    double Margin,
    double DistanceRatio,
    double ClassDistanceThreshold,
    double NormalizedDistance,
    double RatioThreshold,
    double Risk,
    bool Accepted,
    long ElapsedMicroseconds,
    string? Error = null);
