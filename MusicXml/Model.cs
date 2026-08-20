namespace MusicXml;

public sealed class MusicXmlDocument
{
    internal MusicXmlDocument(
        object serializationModel,
        string? version,
        IReadOnlyList<MusicXmlPart> parts)
    {
        SerializationModel = serializationModel;
        Version = version;
        Parts = parts;
    }

    internal object SerializationModel { get; }

    public string? Version { get; }
    public IReadOnlyList<MusicXmlPart> Parts { get; }
    public IReadOnlyList<MusicXmlNote> Notes => Parts.SelectMany(x => x.Measures).SelectMany(x => x.Notes).ToArray();
}

public sealed record MusicXmlPart(string Id, IReadOnlyList<MusicXmlMeasure> Measures);
public sealed record MusicXmlMeasure(string Number, IReadOnlyList<MusicXmlNote> Notes);

public sealed record MusicXmlNote(
    decimal? DefaultX,
    decimal? DefaultY,
    bool IsChordTone,
    bool IsRest,
    string? Step,
    decimal? Alter,
    int? Octave,
    decimal? Duration,
    string? Voice,
    string? Type,
    string? Accidental,
    string? Stem,
    int? Staff)
{
    public string Pitch => IsRest
        ? "rest"
        : $"{Step ?? "?"}{(Alter is null or 0 ? "" : Alter > 0 ? $"+{Alter}" : Alter.ToString())}{Octave?.ToString() ?? "?"}";
}
