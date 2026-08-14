using System.Net;
using System.Text.Json;
using SvgSymbols.Models;

namespace SvgSymbols.Services;

public sealed class WikimediaCommonsClient
{
    private const string ApiUrl = "https://commons.wikimedia.org/w/api.php";
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(750);
    private readonly HttpClient _http;
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public WikimediaCommonsClient(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SvgSymbols/0.1 (+https://github.com/gasharva/svg-music)");
    }

    public async Task<IReadOnlyList<SymbolSource>> GetSvgFilesAsync(
        string kind,
        string rootCategory,
        int subcategoryDepth = 1,
        CancellationToken cancellationToken = default)
    {
        var categories = await GetCategoriesAsync(rootCategory, subcategoryDepth, cancellationToken);
        var result = new Dictionary<string, SymbolSource>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories)
        {
            foreach (var file in await GetSvgFilesInCategoryAsync(kind, category, cancellationToken))
                result.TryAdd(file.FileUrl, file);
        }

        return result.Values
            .OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> GetCategoriesAsync(
        string rootCategory,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootCategory };
        var frontier = new List<string> { rootCategory };

        for (var depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var category in frontier)
            {
                foreach (var child in await GetSubcategoriesAsync(category, cancellationToken))
                {
                    if (found.Add(child))
                        next.Add(child);
                }
            }

            frontier = next;
        }

        return found.ToList();
    }

    private async Task<IReadOnlyList<string>> GetSubcategoriesAsync(
        string category,
        CancellationToken cancellationToken)
    {
        var result = new List<string>();
        string? continuation = null;

        do
        {
            var url = BuildUrl(new Dictionary<string, string?>
            {
                ["action"] = "query",
                ["format"] = "json",
                ["maxlag"] = "5",
                ["list"] = "categorymembers",
                ["cmtitle"] = NormalizeCategory(category),
                ["cmtype"] = "subcat",
                ["cmnamespace"] = "14",
                ["cmlimit"] = "max",
                ["cmcontinue"] = continuation
            });

            using var document = await GetJsonAsync(url, cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("query", out var query) &&
                query.TryGetProperty("categorymembers", out var members))
            {
                foreach (var member in members.EnumerateArray())
                {
                    var title = member.GetProperty("title").GetString();
                    if (!string.IsNullOrWhiteSpace(title))
                        result.Add(title.StartsWith("Category:", StringComparison.OrdinalIgnoreCase)
                            ? title[9..]
                            : title);
                }
            }

            continuation = root.TryGetProperty("continue", out var cont) &&
                           cont.TryGetProperty("cmcontinue", out var token)
                ? token.GetString()
                : null;
        }
        while (!string.IsNullOrWhiteSpace(continuation));

        return result;
    }

    private async Task<IReadOnlyList<SymbolSource>> GetSvgFilesInCategoryAsync(
        string kind,
        string category,
        CancellationToken cancellationToken)
    {
        var result = new List<SymbolSource>();
        string? continuation = null;

        do
        {
            var url = BuildUrl(new Dictionary<string, string?>
            {
                ["action"] = "query",
                ["format"] = "json",
                ["maxlag"] = "5",
                ["generator"] = "categorymembers",
                ["gcmtitle"] = NormalizeCategory(category),
                ["gcmtype"] = "file",
                ["gcmnamespace"] = "6",
                ["gcmlimit"] = "max",
                ["gcmcontinue"] = continuation,
                ["prop"] = "imageinfo",
                ["iiprop"] = "url|mime|extmetadata"
            });

            using var document = await GetJsonAsync(url, cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("query", out var query) &&
                query.TryGetProperty("pages", out var pages))
            {
                foreach (var page in pages.EnumerateObject())
                {
                    var item = TryParseFile(kind, category, page.Value);
                    if (item is not null)
                        result.Add(item);
                }
            }

            continuation = root.TryGetProperty("continue", out var cont) &&
                           cont.TryGetProperty("gcmcontinue", out var token)
                ? token.GetString()
                : null;
        }
        while (!string.IsNullOrWhiteSpace(continuation));

        return result;
    }

    private static SymbolSource? TryParseFile(string kind, string category, JsonElement page)
    {
        if (!page.TryGetProperty("title", out var titleElement) ||
            !page.TryGetProperty("imageinfo", out var imageInfoArray) ||
            imageInfoArray.GetArrayLength() == 0)
            return null;

        var info = imageInfoArray[0];
        var mime = info.TryGetProperty("mime", out var mimeElement)
            ? mimeElement.GetString()
            : null;

        if (!string.Equals(mime, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
            return null;

        var title = titleElement.GetString() ?? "File.svg";
        var fileUrl = info.GetProperty("url").GetString();
        var descriptionUrl = info.TryGetProperty("descriptionurl", out var description)
            ? description.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(fileUrl))
            return null;

        var metadata = info.TryGetProperty("extmetadata", out var meta)
            ? meta
            : default;

        return new SymbolSource(
            kind,
            category,
            title,
            SanitizeFileName(title.StartsWith("File:", StringComparison.OrdinalIgnoreCase) ? title[5..] : title),
            descriptionUrl ?? "https://commons.wikimedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_')),
            fileUrl,
            GetMetadata(metadata, "LicenseShortName"),
            GetMetadata(metadata, "LicenseUrl"),
            GetMetadata(metadata, "Artist"));
    }

    private static string? GetMetadata(JsonElement metadata, string name)
    {
        if (metadata.ValueKind != JsonValueKind.Object || !metadata.TryGetProperty(name, out var value))
            return null;

        return value.TryGetProperty("value", out var actual) ? actual.GetString() : null;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        const int maxAttempts = 6;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await WaitForRequestSlotAsync(cancellationToken);

            using var response = await _http.GetAsync(url, cancellationToken);
            _lastRequestAt = DateTimeOffset.UtcNow;

            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }

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

    private static string NormalizeCategory(string category) =>
        category.StartsWith("Category:", StringComparison.OrdinalIgnoreCase)
            ? category
            : "Category:" + category;

    private static string BuildUrl(IReadOnlyDictionary<string, string?> parameters)
    {
        var query = string.Join("&", parameters
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}"));
        return ApiUrl + "?" + query;
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');

        return fileName;
    }
}
