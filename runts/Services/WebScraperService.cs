using runts.Helpers;

namespace runts.Services;

/// <summary>
/// Servizio scraping siti web tramite Bright Data Web Unlocker.
/// </summary>
public sealed class WebScraperService
{
    private readonly LoggerService _logger;

    public WebScraperService(LoggerService logger)
    {
        _logger = logger;
    }

    public async Task<(IReadOnlyCollection<string> emails, IReadOnlyCollection<string> pecs, IReadOnlyCollection<string> phones)> AnalyzeAsync(
        string baseUrl,
        int delayMs,
        bool headless = true,
        CancellationToken cancellationToken = default)
    {
        var allEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allPecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allPhones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return ([], [], []);
        }

        try
        {
            using var brightData = new BrightDataSearchService(_logger);
            await _logger.LogAsync($"Scansione sito: {baseUrl}", cancellationToken);
            foreach (var pageUrl in GetPagesToScan(baseUrl))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var html = await brightData.FetchPageAsync(pageUrl, cancellationToken);
                if (string.IsNullOrWhiteSpace(html))
                {
                    continue;
                }

                foreach (var email in EmailExtractor.Extract(html))
                {
                    allEmails.Add(email);
                    if (PecIdentifier.IsPec(email))
                    {
                        allPecs.Add(email);
                    }
                }

                allPhones.UnionWith(PhoneExtractor.Extract(html));

                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
            }

            await _logger.LogAsync(
                $"Scansione completata: {allEmails.Count} email | {allPecs.Count} PEC | {allPhones.Count} telefoni",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Errore scansione {baseUrl}: {ex.Message}", cancellationToken);
        }

        return (allEmails.ToArray(), allPecs.ToArray(), allPhones.ToArray());
    }

    private static IEnumerable<string> GetPagesToScan(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            yield break;
        }

        var root = uri.GetLeftPart(UriPartial.Authority);
        var paths = new[] { "/", "/contatti", "/contact", "/chi-siamo", "/about", "/staff", "/privacy", "/footer" };
        foreach (var path in paths)
        {
            yield return new Uri(new Uri(root), path).ToString();
        }
    }
}
