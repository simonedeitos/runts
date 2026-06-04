using runts.Helpers;
using runts.Models;
using System.Text.RegularExpressions;

namespace runts.Services;

public sealed class SearchEngineService : IDisposable
{
    private readonly BrightDataSearchService _brightData;
    private readonly LoggerService _logger;

    public SearchEngineService(LoggerService logger)
    {
        _logger = logger;
        _brightData = new BrightDataSearchService(logger);
    }

    public List<string> CostruisciQuery(Ente ente)
    {
        if (ente.Categoria.Equals("Pro Loco", StringComparison.OrdinalIgnoreCase))
        {
            var (nome, cf) = EstraiNomeECF(ente.Denominazione);
            var comune = ente.Comune?.Trim() ?? string.Empty;

            var queries = new List<string>();

            _logger.LogAsync($"Parsing denominazione: '{ente.Denominazione}'").GetAwaiter().GetResult();
            _logger.LogAsync($"  → Nome estratto: '{nome}'").GetAwaiter().GetResult();
            _logger.LogAsync($"  → CF estratto: '{cf}'").GetAwaiter().GetResult();
            _logger.LogAsync($"  → Comune: '{comune}'").GetAwaiter().GetResult();

            if (!string.IsNullOrWhiteSpace(nome) && !string.IsNullOrWhiteSpace(cf))
            {
                AddQuery(queries, $"{nome} {cf}");
                AddQuery(queries, $"{nome} {cf} sito");
            }

            if (!string.IsNullOrWhiteSpace(nome))
            {
                AddQuery(queries, nome);
                AddQuery(queries, $"{nome} contatti");
                AddQuery(queries, $"{nome} email");
            }

            if (!string.IsNullOrWhiteSpace(comune))
            {
                AddQuery(queries, $"Pro Loco {comune}");
                AddQuery(queries, $"Pro Loco {comune} sito ufficiale");
            }

            if (queries.Count > 7)
            {
                queries = queries.Take(7).ToList();
            }

            _logger.LogAsync($"Query generate: {queries.Count}").GetAwaiter().GetResult();
            foreach (var q in queries)
            {
                _logger.LogAsync($"  - '{q}'").GetAwaiter().GetResult();
            }

            return queries;
        }

        return
        [
            $"{ente.Denominazione} sito ufficiale",
            $"{ente.Denominazione} contatti",
            $"{ente.Denominazione} {ente.Comune}"
        ];
    }

    public async Task<string> FindBestWebsiteAsync(Ente ente, bool headless = true, CancellationToken cancellationToken = default)
    {
        if (!_brightData.IsConfigured())
        {
            await _logger.LogAsync("⚠ Bright Data non configurato. Vai in Impostazioni → Configura Bright Data API", cancellationToken);
            return string.Empty;
        }

        var queries = CostruisciQuery(ente);

        foreach (var query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);
                await _logger.LogAsync($"Ricerca query: '{query}'", cancellationToken);

                await _logger.LogAsync("→ Tentativo Bing...", cancellationToken);
                var bingResults = await _brightData.SearchBingAsync(query, cancellationToken);
                await _logger.LogAsync($"  Bing: {bingResults.Count} risultati", cancellationToken);

                foreach (var url in bingResults)
                {
                    if (IsCandidateMatch(url, ente))
                    {
                        await _logger.LogAsync($"✓ MATCH TROVATO: {url}", cancellationToken);
                        await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);
                        return ExtractDomain(url);
                    }

                    await _logger.LogAsync($"  ✗ Scartato: {url}", cancellationToken);
                }

                if (bingResults.Count == 0)
                {
                    await _logger.LogAsync("→ Tentativo Google (fallback)...", cancellationToken);
                    var googleResults = await _brightData.SearchGoogleAsync(query, cancellationToken);
                    await _logger.LogAsync($"  Google: {googleResults.Count} risultati", cancellationToken);

                    foreach (var url in googleResults)
                    {
                        if (IsCandidateMatch(url, ente))
                        {
                            await _logger.LogAsync($"✓ MATCH TROVATO: {url}", cancellationToken);
                            await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);
                            return ExtractDomain(url);
                        }

                        await _logger.LogAsync($"  ✗ Scartato: {url}", cancellationToken);
                    }
                }

                await _logger.LogAsync($"✗ Nessun match con query '{query}'", cancellationToken);
                await Task.Delay(1000, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                await _logger.LogAsync($"⚠ Timeout ricerca per query '{query}'", cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await _logger.LogAsync($"Errore ricerca '{query}': {ex.Message}", cancellationToken);
            }
        }

        await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);
        await _logger.LogAsync($"❌ NESSUN SITO trovato per {ente.Denominazione}", cancellationToken);
        await _logger.LogAsync($"   Tutte le {queries.Count} query hanno fallito", cancellationToken);
        await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);
        return string.Empty;
    }

    public void Dispose()
    {
        _brightData.Dispose();
    }

    private static bool IsCandidateMatch(string url, Ente ente)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var excluded = new[]
        {
            "facebook.com", "instagram.com", "youtube.com", "linkedin.com",
            "wikipedia.org", "twitter.com", "tiktok.com",
            "paginebianche.it", "paginegialle.it",
            "google.com", "bing.com", "comune."
        };

        if (excluded.Any(ex => host.Contains(ex, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var (nome, _) = EstraiNomeECF(ente.Denominazione);
        var comune = ente.Comune?.ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal) ?? string.Empty;

        var paroleChiave = nome
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Regex.Replace(p.ToLowerInvariant(), "[^a-z0-9]+", string.Empty))
            .Where(p => p.Length >= 4 && p != "proloco" && p != "loco")
            .ToArray();

        if (host.Contains("proloco") && !string.IsNullOrEmpty(comune) && host.Contains(comune))
        {
            return true;
        }

        if (paroleChiave.Any(parola => host.Contains(parola)))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(comune) && host.Contains(comune) && (host.Contains("pro") || host.Contains("loco")))
        {
            return true;
        }

        return false;
    }

    private static (string nome, string cf) EstraiNomeECF(string denominazione)
    {
        if (string.IsNullOrWhiteSpace(denominazione))
        {
            return (string.Empty, string.Empty);
        }

        var cfMatch = Regex.Match(
            denominazione.ToUpperInvariant(),
            @"\b(\d{11}|[A-Z]{6}\d{2}[A-Z]\d{2}[A-Z]\d{3}[A-Z])\b");

        string nome;
        var cf = string.Empty;

        if (cfMatch.Success)
        {
            cf = cfMatch.Groups[1].Value;
            nome = denominazione.Substring(0, cfMatch.Index).Trim();
        }
        else
        {
            var matchNome = Regex.Match(denominazione, @"^([\p{L}\s""']+)");
            nome = matchNome.Success ? matchNome.Groups[1].Value.Trim() : denominazione.Trim();
        }

        nome = Regex.Replace(nome, @"\s+", " ").Trim();
        nome = nome.Trim('"', '\'', ' ');

        return (nome, cf);
    }

    private static void AddQuery(List<string> queries, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        if (!queries.Contains(query, StringComparer.OrdinalIgnoreCase))
        {
            queries.Add(query.Trim());
        }
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
