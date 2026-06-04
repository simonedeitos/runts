using HtmlAgilityPack;
using runts.Models;
using System.Text.RegularExpressions;

namespace runts.Services;

public sealed class SearchEngineService
{
    private const string DuckDuckGoHtmlSearchUrl = "https://duckduckgo.com/html/?q=";
    private static readonly string[] ExcludedHosts =
    [
        "facebook.com",
        "instagram.com",
        "youtube.com",
        "linkedin.com",
        "paginebianche.it",
        "comune.",
        "wikipedia.org"
    ];

    private readonly HttpClient _httpClient;
    private readonly LoggerService _logger;

    public SearchEngineService(HttpClient httpClient, LoggerService logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public List<string> CostruisciQuery(Ente ente)
    {
        if (ente.Categoria.Equals("Pro Loco", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                $"{ente.Denominazione} sito ufficiale",
                $"Pro Loco {ente.Comune} sito ufficiale",
                $"Pro Loco di {ente.Comune}",
                $"{ente.Comune} pro loco contatti",
                $"{ente.Denominazione} {ente.Comune} {ente.Provincia}",
                $"Associazione Pro Loco {ente.Comune} {ente.Provincia}"
            ];
        }

        return
        [
            $"{ente.Denominazione} sito ufficiale",
            $"{ente.Denominazione} ETS",
            $"{ente.Denominazione} {ente.Comune}"
        ];
    }

    public async Task<string> FindBestWebsiteAsync(Ente ente, CancellationToken cancellationToken = default)
    {
        var queries = CostruisciQuery(ente);
        foreach (var query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var url = $"{DuckDuckGoHtmlSearchUrl}{Uri.EscapeDataString(query)}";
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    await _logger.LogAsync($"Ricerca web non disponibile per query '{query}': HTTP {(int)response.StatusCode}", cancellationToken);
                    continue;
                }

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var match = ExtractCandidateUrls(html)
                    .FirstOrDefault(candidate => IsCandidateMatch(candidate, ente));

                if (!string.IsNullOrWhiteSpace(match))
                {
                    await _logger.LogAsync($"Sito individuato per {ente.Denominazione}: {match}", cancellationToken);
                    return match;
                }
            }
            catch (TaskCanceledException)
            {
                await _logger.LogAsync($"Timeout ricerca sito per {ente.Denominazione} con query '{query}'.", cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                await _logger.LogAsync($"Errore rete ricerca sito per {ente.Denominazione}: {ex.Message}", cancellationToken);
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> ExtractCandidateUrls(string html)
    {
        var document = new HtmlAgilityPack.HtmlDocument();
        document.LoadHtml(html);

        var links = document.DocumentNode.SelectNodes("//a[contains(@class,'result__a') or contains(@href,'uddg=')]");
        if (links is null)
        {
            yield break;
        }

        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", string.Empty);
            var candidate = DecodeDuckDuckGoUrl(href);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                yield return uri.GetLeftPart(UriPartial.Authority);
            }
        }
    }

    private static string DecodeDuckDuckGoUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var absoluteHref)
            && !absoluteHref.Host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            return absoluteHref.ToString();
        }

        if (!href.Contains("uddg=", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var query = href.Split('?', 2).ElementAtOrDefault(1) ?? string.Empty;
        foreach (var parameter in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = parameter.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals("uddg", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return string.Empty;
    }

    private static bool IsCandidateMatch(string candidate, Ente ente)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (ExcludedHosts.Any(host => uri.Host.Contains(host, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var comparable = $"{ente.Denominazione} {ente.Comune}"
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => Regex.Replace(token.ToLowerInvariant(), "[^a-z0-9]+", string.Empty))
            .Where(token => token.Length >= 4)
            .Distinct()
            .ToArray();
        var hostComparable = Regex.Replace(uri.Host.ToLowerInvariant(), "[^a-z0-9]+", string.Empty);
        var comuneComparable = Regex.Replace(ente.Comune.ToLowerInvariant(), "[^a-z0-9]+", string.Empty);

        return comparable.Length == 0
            || comparable.Any(hostComparable.Contains)
            || (!string.IsNullOrWhiteSpace(comuneComparable) && hostComparable.Contains(comuneComparable));
    }
}
