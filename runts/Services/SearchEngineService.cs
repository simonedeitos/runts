using OpenQA.Selenium;
using runts.Helpers;
using runts.Models;

namespace runts.Services;

public sealed class SearchEngineService
{
    private readonly LoggerService _logger;

    public SearchEngineService(LoggerService logger)
    {
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

    public async Task<string> FindBestWebsiteAsync(Ente ente, bool headless = true, CancellationToken cancellationToken = default)
    {
        using var chrome = new ChromeAutomationHelper(_logger, headless);
        var queries = CostruisciQuery(ente);

        foreach (var query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _logger.LogAsync($"Ricerca Google: '{query}'", cancellationToken);
                var googleResults = await chrome.SearchGoogleAsync(query, cancellationToken);
                var match = googleResults.FirstOrDefault(url => IsCandidateMatch(url, ente));

                if (!string.IsNullOrWhiteSpace(match))
                {
                    var domain = ExtractDomain(match);
                    await _logger.LogAsync($"SITO TROVATO: {domain}", cancellationToken);
                    return domain;
                }

                if (googleResults.Count == 0)
                {
                    await _logger.LogAsync($"Google senza risultati, provo Bing: '{query}'", cancellationToken);
                    var bingResults = await chrome.SearchBingAsync(query, cancellationToken);
                    match = bingResults.FirstOrDefault(url => IsCandidateMatch(url, ente));

                    if (!string.IsNullOrWhiteSpace(match))
                    {
                        var domain = ExtractDomain(match);
                        await _logger.LogAsync($"SITO TROVATO (Bing): {domain}", cancellationToken);
                        return domain;
                    }
                }

                await Task.Delay(2000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (WebDriverException ex)
            {
                await _logger.LogAsync($"Errore ricerca '{query}': {ex.Message}", cancellationToken);
            }
        }

        await _logger.LogAsync($"NESSUN SITO trovato per {ente.Denominazione}", cancellationToken);
        return string.Empty;
    }

    private static bool IsCandidateMatch(string url, Ente ente)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var excluded = new[] { "facebook.com", "instagram.com", "youtube.com", "linkedin.com", "wikipedia.org", "paginebianche.it", "comune." };
        if (excluded.Any(host => uri.Host.Contains(host, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var comune = ente.Comune.ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        var denominazione = ente.Denominazione.ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);

        return host.Contains("proloco", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(comune) && host.Contains(comune, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(denominazione) && host.Contains(denominazione, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractDomain(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }

        return string.Empty;
    }
}
