using System.Text.RegularExpressions;

namespace SvgToMusicXmlPoc.Services;

public sealed record ScoreTextMetadata(
    string? Title,
    IReadOnlyList<string> DescriptionLines,
    string? Author,
    string? Tempo,
    IReadOnlyList<ScoreTextPlacement> Placements)
{
    private static readonly Regex PlacementRegex = new(
        @"^(?<measure>\d+)-(?<staff>\d+)-(?<align>[LCR])\s*:\s*(?<text>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ScoreTextMetadata? TryLoad(string directoryPath)
    {
        var path = Path.Combine(directoryPath, "score.txt");
        if (!File.Exists(path)) return null;

        string? title = null;
        string? author = null;
        string? tempo = null;
        var description = new List<string>();
        var placements = new List<ScoreTextPlacement>();

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (TryField(line, "Title", out var value)) { title = value; continue; }
            if (TryField(line, "Author", out value)) { author = value; continue; }
            if (TryField(line, "Tempo", out value)) { tempo = value; continue; }
            if (TryField(line, "Description", out value))
            {
                description.AddRange(value.Split('\\', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                continue;
            }

            var match = PlacementRegex.Match(line);
            if (!match.Success) continue;
            placements.Add(new ScoreTextPlacement(
                int.Parse(match.Groups["measure"].Value),
                int.Parse(match.Groups["staff"].Value),
                match.Groups["align"].Value.ToUpperInvariant()[0],
                match.Groups["text"].Value.Trim()));
        }

        return new ScoreTextMetadata(title, description, author, tempo, placements);
    }

    private static bool TryField(string line, string name, out string value)
    {
        var prefix = name + ":";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = string.Empty;
            return false;
        }
        value = line[prefix.Length..].Trim();
        return true;
    }
}

public sealed record ScoreTextPlacement(int Measure, int Staff, char Align, string Text);
