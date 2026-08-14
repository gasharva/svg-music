using System.Text.Json;
using SvgSymbols.Models;

namespace SvgSymbols.Services;

public sealed class SymbolCorpusDownloader
{
    private readonly HttpClient _http;

    public SymbolCorpusDownloader(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SvgSymbols/0.1 (+https://github.com/gasharva/svg-music)");
    }

    public async Task<IReadOnlyList<SymbolSource>> DownloadAsync(
        IReadOnlyList<SymbolSource> sources,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var downloaded = new List<SymbolSource>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            var fileName = MakeUnique(source.FileName, usedNames);
            var target = Path.Combine(outputDirectory, fileName);

            try
            {
                var bytes = await _http.GetByteArrayAsync(source.FileUrl, cancellationToken);
                await File.WriteAllBytesAsync(target, bytes, cancellationToken);
                downloaded.Add(source with { FileName = fileName });
                Console.WriteLine($"  + {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ! {source.Title}: {ex.Message}");
            }
        }

        var metadataPath = Path.Combine(outputDirectory, "sources.json");
        var json = JsonSerializer.Serialize(downloaded, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(metadataPath, json, cancellationToken);

        return downloaded;
    }

    private static string MakeUnique(string fileName, ISet<string> usedNames)
    {
        if (usedNames.Add(fileName))
            return fileName;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var index = 2;

        while (true)
        {
            var candidate = $"{stem}_{index++}{extension}";
            if (usedNames.Add(candidate))
                return candidate;
        }
    }
}
