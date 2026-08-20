using MusicXml;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project MusicXml -- <input.musicxml> [output.musicxml]");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var output = args.Length == 2
    ? Path.GetFullPath(args[1])
    : Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + ".roundtrip.musicxml");

var reader = new MusicXmlReader();
var writer = new MusicXmlWriter();
var score = reader.Read(input);

PrintSummary("INPUT", input, score);
writer.Write(score, output);

var roundTrip = reader.Read(output);
PrintSummary("ROUNDTRIP", output, roundTrip);

var before = Fingerprints(score).ToArray();
var after = Fingerprints(roundTrip).ToArray();
if (!before.SequenceEqual(after, StringComparer.Ordinal))
{
    Console.Error.WriteLine("Round-trip verification FAILED: musical note projection changed.");
    return 1;
}

Console.WriteLine($"Round-trip OK: {before.Length} notes/rests preserved in typed projection.");
return 0;

static IEnumerable<string> Fingerprints(MusicXmlDocument score)
{
    foreach (var part in score.Parts)
    foreach (var measure in part.Measures)
    foreach (var note in measure.Notes)
        yield return string.Join('|',
            part.Id, measure.Number, note.DefaultX, note.DefaultY, note.IsChordTone, note.IsRest,
            note.Step, note.Alter, note.Octave, note.Duration, note.Voice, note.Type,
            note.Accidental, note.Stem, note.Staff);
}

static void PrintSummary(string label, string path, MusicXmlDocument score)
{
    Console.WriteLine($"{label}: {path}");
    Console.WriteLine($"MusicXML version: {score.Version ?? "unknown"}; parts: {score.Parts.Count}; measures: {score.Parts.Sum(x => x.Measures.Count)}; notes/rests: {score.Notes.Count}");

    foreach (var note in score.Notes.Take(20))
    {
        Console.WriteLine(
            $"  {note.Pitch,-8} duration={note.Duration?.ToString() ?? "?",-5} voice={note.Voice ?? "?",-3} " +
            $"type={note.Type ?? "?",-8} staff={note.Staff?.ToString() ?? "?",-2} stem={note.Stem ?? "?",-4} " +
            $"accidental={note.Accidental ?? "-",-10} x={note.DefaultX?.ToString() ?? "?"} y={note.DefaultY?.ToString() ?? "?"}");
    }
}
