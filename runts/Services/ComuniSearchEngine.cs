using EasySearch.Helpers;
using EasySearch.Models;
using System.Globalization;
using System.Text;

namespace EasySearch.Services;

/// <summary>
/// Ricerca il sito di un ente partendo dal nome del comune ISTAT.
/// </summary>
public sealed class ComuniSearchEngine
{
    private readonly LoggerService _logger;
    private readonly PuppeteerHelper _puppeteer;

    public ComuniSearchEngine(LoggerService logger, PuppeteerHelper puppeteer)
    {
        _logger = logger;
        _puppeteer = puppeteer;
    }

    public async Task<string> FindProLocoForComuneAsync(ComuneIstat comune, string searchWord, bool searchGoogleMaps = false, CancellationToken cancellationToken = default)
    {
        var results = await FindMultipleForComuneAsync(comune, searchWord, searchGoogleMaps, cancellationToken);
        return results.FirstOrDefault() ?? string.Empty;
    }

    public async Task<List<string>> FindMultipleForComuneAsync(ComuneIstat comune, string searchWord, bool searchGoogleMaps = false, CancellationToken cancellationToken = default)
    {
        var queries = BuildQueries(comune, searchWord, searchGoogleMaps);
        var matches = new List<string>();
        var seenDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);
        await _logger.LogAsync($"🏛️ COMUNE: {comune.Nome} ({comune.SiglaProvincia})", cancellationToken);
        await _logger.LogAsync($"🔎 Ricerca: {searchWord}", cancellationToken);
        await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);
        await _logger.LogAsync($"Query generate: {queries.Count}", cancellationToken);

        foreach (var query in queries)
        {
            await _logger.LogAsync($"  - '{query}'", cancellationToken);
        }

        foreach (var query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);
            await _logger.LogAsync($"Query: '{query}'", cancellationToken);
            await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);

            var isMapsQuery = searchGoogleMaps && query.Contains("maps", StringComparison.OrdinalIgnoreCase);
            var results = await _puppeteer.SearchAsync(query, cancellationToken, includeMapsLinks: isMapsQuery);
            if (results.Count == 0)
            {
                await _logger.LogAsync($"✗ Nessun risultato per '{query}'", cancellationToken);
                continue;
            }

            await _logger.LogAsync($"Risultati: {results.Count}", cancellationToken);
            foreach (var url in results)
            {
                if (!IsCandidateMatch(url, comune, searchWord))
                {
                    await _logger.LogAsync($"  ✗ Scartato: {url}", cancellationToken);
                    continue;
                }

                var domain = ExtractDomain(url);
                if (string.IsNullOrWhiteSpace(domain) || !seenDomains.Add(domain))
                {
                    continue;
                }

                matches.Add(domain);
                await _logger.LogAsync($"✓ MATCH: {domain}", cancellationToken);
                if (matches.Count >= 5)
                {
                    return matches;
                }
            }

            await Task.Delay(Random.Shared.Next(1000, 2500), cancellationToken);
        }

        if (matches.Count == 0)
        {
            await _logger.LogAsync($"❌ Nessun sito trovato per {comune.Nome}", cancellationToken);
        }

        return matches;
    }

    private static List<string> BuildQueries(ComuneIstat comune, string searchWord, bool searchGoogleMaps = false)
    {
        var normalizedSearchWord = string.IsNullOrWhiteSpace(searchWord) ? "Pro Loco" : searchWord.Trim();
        var queries = new List<string>();

        AddQuery(queries, $"{normalizedSearchWord} {comune.Nome}");

        if (!string.IsNullOrWhiteSpace(comune.SiglaProvincia))
        {
            AddQuery(queries, $"{normalizedSearchWord} {comune.Nome} {comune.SiglaProvincia}");
        }

        AddQuery(queries, $"{normalizedSearchWord} {comune.Nome} contatti");
        AddQuery(queries, $"{normalizedSearchWord} {comune.Nome} email");
        AddQuery(queries, $"{normalizedSearchWord} {comune.Nome} sito");
        AddQuery(queries, $"{comune.Nome} {normalizedSearchWord}");

        if (searchGoogleMaps)
        {
            AddQuery(queries, $"{normalizedSearchWord} {comune.Nome} google maps");
            AddQuery(queries, $"{normalizedSearchWord} {comune.Nome} maps");
        }

        return queries;
    }

    private static void AddQuery(List<string> queries, string query)
    {
        if (!queries.Contains(query, StringComparer.OrdinalIgnoreCase))
        {
            queries.Add(query);
        }
    }

    private static bool IsCandidateMatch(string url, ComuneIstat comune, string searchWord)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var urlValue = $"{uri.Host}{uri.AbsolutePath}".ToLowerInvariant();
        var normalizedUrl = NormalizeForMatch(urlValue);
        var normalizedComune = NormalizeForMatch(comune.Nome);
        var normalizedSearchWord = NormalizeForMatch(searchWord);

        if (normalizedUrl.Contains("facebook") ||
            normalizedUrl.Contains("instagram") ||
            normalizedUrl.Contains("youtube") ||
            normalizedUrl.Contains("wikipedia") ||
            normalizedUrl.Contains("linkedin") ||
            normalizedUrl.Contains("tiktok") ||
            normalizedUrl.Contains("paginegialle") ||
            normalizedUrl.Contains("paginebianche") ||
            normalizedUrl.Contains("virgilio") ||
            normalizedUrl.Contains("trovalo") ||
            normalizedUrl.Contains("paginesi") ||
            normalizedUrl.Contains("tuttocitta") ||
            normalizedUrl.Contains("misterimprese") ||
            normalizedUrl.Contains("11880"))
        {
            return false;
        }

        var hasSearchWord = !string.IsNullOrWhiteSpace(normalizedSearchWord) && normalizedUrl.Contains(normalizedSearchWord);
        var hasComune = normalizedUrl.Contains(normalizedComune);

        if (hasSearchWord && hasComune)
        {
            return true;
        }

        if (hasComune && IsLikelyEntityUrl(normalizedUrl))
        {
            return true;
        }

        return hasComune && (uri.Host.EndsWith(".it", StringComparison.OrdinalIgnoreCase) ||
                             uri.Host.EndsWith(".com", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLikelyEntityUrl(string url) =>
        new[] { "turismo", "eventi", "cultura", "territorio", "comune", "visit", "discover", "tourist", "info", "welcome", "unpli" }
            .Any(url.Contains);

    private static string ExtractDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        // For Google Maps place URLs, use the full URL so that multiple
        // different business listings are not deduplicated to a single domain.
        if (uri.Host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith("/maps/", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string NormalizeForMatch(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(value.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
