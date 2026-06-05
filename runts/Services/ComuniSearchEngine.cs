using runts.Helpers;
using runts.Models;
using System.Globalization;
using System.Text;

namespace runts.Services;

/// <summary>
/// Ricerca il sito della Pro Loco a partire dal nome del comune ISTAT.
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

    public async Task<string> FindProLocoForComuneAsync(ComuneIstat comune, CancellationToken cancellationToken = default)
    {
        var queries = BuildQueries(comune);

        await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);
        await _logger.LogAsync($"🏛️ COMUNE: {comune.Nome} ({comune.SiglaProvincia})", cancellationToken);
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

            var results = await _puppeteer.SearchAsync(query, cancellationToken);
            if (results.Count == 0)
            {
                await _logger.LogAsync($"✗ Nessun risultato per '{query}'", cancellationToken);
                continue;
            }

            await _logger.LogAsync($"Risultati: {results.Count}", cancellationToken);
            foreach (var url in results)
            {
                if (IsCandidateMatch(url, comune))
                {
                    await _logger.LogAsync($"✓ SITO TROVATO: {url}", cancellationToken);
                    return ExtractDomain(url);
                }

                await _logger.LogAsync($"  ✗ Scartato: {url}", cancellationToken);
            }

            await _logger.LogAsync($"✗ Nessun match con query '{query}'", cancellationToken);
            await Task.Delay(Random.Shared.Next(2000, 4000), cancellationToken);
        }

        await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);
        await _logger.LogAsync($"❌ NESSUN SITO trovato per {comune.Nome}", cancellationToken);
        await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);
        return string.Empty;
    }

    private static List<string> BuildQueries(ComuneIstat comune)
    {
        var queries = new List<string>();
        AddQuery(queries, $"Pro Loco {comune.Nome}");

        if (!string.IsNullOrWhiteSpace(comune.SiglaProvincia))
        {
            AddQuery(queries, $"Pro Loco {comune.Nome} {comune.SiglaProvincia}");
        }

        AddQuery(queries, $"Pro Loco {comune.Nome} contatti");
        AddQuery(queries, $"Pro Loco {comune.Nome} email");
        AddQuery(queries, $"Pro Loco {comune.Nome} sito");
        AddQuery(queries, $"Proloco {comune.Nome}");
        AddQuery(queries, $"{comune.Nome} Pro Loco");
        return queries;
    }

    private static void AddQuery(List<string> queries, string query)
    {
        if (!queries.Contains(query, StringComparer.OrdinalIgnoreCase))
        {
            queries.Add(query);
        }
    }

    private static bool IsCandidateMatch(string url, ComuneIstat comune)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var urlValue = $"{uri.Host}{uri.AbsolutePath}".ToLowerInvariant();
        var normalizedUrl = NormalizeForMatch(urlValue);
        var normalizedComune = NormalizeForMatch(comune.Nome);

        if (normalizedUrl.Contains("facebook") ||
            normalizedUrl.Contains("instagram") ||
            normalizedUrl.Contains("youtube") ||
            normalizedUrl.Contains("wikipedia"))
        {
            return false;
        }

        var hasProLoco = normalizedUrl.Contains("proloco") ||
                         normalizedUrl.Contains("prolocopro");
        var hasComune = normalizedUrl.Contains(normalizedComune);

        if (hasProLoco && hasComune)
        {
            return true;
        }

        if (hasComune && IsLikelyProLocoUrl(normalizedUrl))
        {
            return true;
        }

        return hasComune && (uri.Host.EndsWith(".it", StringComparison.OrdinalIgnoreCase) ||
                             uri.Host.EndsWith(".com", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLikelyProLocoUrl(string url) =>
        new[] { "turismo", "eventi", "cultura", "territorio", "comune", "visit", "discover", "tourist", "info", "welcome", "unpli" }
            .Any(url.Contains);

    private static string ExtractDomain(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : string.Empty;
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
