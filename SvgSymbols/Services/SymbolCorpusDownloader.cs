using System.Net;
using System.Text.Json;
using SvgSymbols.Models;

namespace SvgSymbols.Services;

public sealed class SymbolCorpusDownloader
{
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(750);
    private readonly HttpClient _http;
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

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
                if (!File.Exists(target))
                {
                    var bytes = await DownloadBytesAsync(source.FileUrl, cancellationToken);
                    await File.WriteAllBytesAsync(target, bytes, cancellationToken);
                    Console.WriteLine($"  + {fileName}");
                }
                else
                {
                    Console.WriteLine($"  = {fileName} (already downloaded)");
                }

                downloaded.Add(source with { FileName = fileName });
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

    private async Task<byte[]> DownloadBytesAsync(string url, CancellationToken cancellationToken)
    {
        const int maxAttempts = 6;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await WaitForRequestSlotAsync(cancellationToken);

            using var response = await _http.GetAsync(url, cancellationToken);
            _lastRequestAt = DateTimeOffset.UtcNow;

            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (response.StatusCode != HttpStatusCode.TooManyRequests &&
                response.StatusCode != HttpStatusCode.ServiceUnavailable)
            {
                response.EnsureSuccessStatusCode();
            }

            if (attempt == maxAttempts)
                response.EnsureSuccessStatusCode();

            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);

            var fallback = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt + 1)));
            var delay = retryAfter is { } specified && specified > TimeSpan.Zero
                ? specified
                : fallback;

            Console.WriteLine(
                $"  Wikimedia returned {(int)response.StatusCode}; waiting {delay.TotalSeconds:0.#}s before retry {attempt + 1}/{maxAttempts}...");

            await Task.Delay(delay, cancellationToken);
        }

        throw new InvalidOperationException("Unreachable Wikimedia retry loop exit.");
    }

    private async Task WaitForRequestSlotAsync(CancellationToken cancellationToken)
    {
        var elapsed = DateTimeOffset.UtcNow - _lastRequestAt;
        var remaining = MinRequestInterval - elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, cancellationToken);
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
