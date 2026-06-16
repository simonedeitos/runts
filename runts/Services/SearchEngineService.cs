using EasySearch.Helpers;
using EasySearch.Models;
using System.Text.RegularExpressions;

namespace EasySearch.Services;

public sealed class SearchEngineService : IDisposable
{
    private readonly LoggerService _logger;
    private PuppeteerHelper? _puppeteer;
    private bool? _headless;

    public SearchEngineService(LoggerService logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync(bool headless = true, CancellationToken cancellationToken = default)
    {
        if (_puppeteer is not null && _headless == headless)
        {
            await _puppeteer.InitializeAsync(cancellationToken);
            return;
        }

        Dispose();
        _puppeteer = new PuppeteerHelper(_logger, headless);
        _headless = headless;
        await _puppeteer.InitializeAsync(cancellationToken);
        await _logger.LogAsync("✓ Puppeteer inizializzato (browser condiviso)", cancellationToken);
    }

    public List<string> CostruisciQuery(Ente ente)
    {
        if (ente.Categoria.Equals("Pro Loco", StringComparison.OrdinalIgnoreCase))
        {
            var (nome, cf) = EstraiNomeECF(ente.Denominazione);
            var queries = new List<string>();

            _logger.LogAsync($"Parsing denominazione: '{ente.Denominazione}'").GetAwaiter().GetResult();
            _logger.LogAsync($"  → Nome estratto: '{nome}'").GetAwaiter().GetResult();
            _logger.LogAsync($"  → CF estratto: '{cf}'").GetAwaiter().GetResult();

            if (!string.IsNullOrWhiteSpace(nome) && !string.IsNullOrWhiteSpace(cf))
            {
                AddQuery(queries, $"{nome} {cf}");
            }

            if (!string.IsNullOrWhiteSpace(nome))
            {
                AddQuery(queries, nome);
                AddQuery(queries, $"{nome} sito");
                AddQuery(queries, $"{nome} contatti");
            }

            _logger.LogAsync($"Query generate: {queries.Count}").GetAwaiter().GetResult();
            foreach (var q in queries)
            {
                _logger.LogAsync($"  - '{q}'").GetAwaiter().GetResult();
            }

            return queries;
        }

        return [ente.Denominazione];
    }

    public async Task<string> FindBestWebsiteAsync(Ente ente, bool headless = true, CancellationToken cancellationToken = default)
    {
        if (_puppeteer is null || _headless != headless)
        {
            await InitializeAsync(headless, cancellationToken);
        }

        var queries = CostruisciQuery(ente).Take(3).ToList();

        foreach (var query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);
                await _logger.LogAsync($"Query: '{query}'", cancellationToken);

                var results = await _puppeteer!.SearchAsync(query, cancellationToken);
                if (results.Count == 0)
                {
                    await _logger.LogAsync($"✗ Nessun risultato per '{query}'", cancellationToken);
                    continue;
                }

                await _logger.LogAsync($"Risultati: {results.Count}", cancellationToken);

                foreach (var url in results)
                {
                    if (IsCandidateMatch(url, ente))
                    {
                        await _logger.LogAsync($"✓ SITO TROVATO: {url}", cancellationToken);
                        await _logger.LogAsync("═══════════════════════════════════════", cancellationToken);
                        return ExtractDomain(url);
                    }

                    await _logger.LogAsync($"  ✗ Scartato: {url}", cancellationToken);
                }

                await _logger.LogAsync($"✗ Nessun match con query '{query}'", cancellationToken);
                await Task.Delay(Random.Shared.Next(2000, 4000), cancellationToken);
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
        _puppeteer?.Dispose();
        _puppeteer = null;
        _headless = null;
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
            "virgilio.it", "tuttocitta.it", "cercassicurazioni.it", "cercazienda.it",
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
