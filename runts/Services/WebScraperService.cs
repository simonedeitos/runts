using AngleSharp.Html.Parser;
using runts.Helpers;
using System.Collections.Concurrent;

namespace runts.Services;

/// <summary>
/// Servizio che scarica le pagine principali del sito e ne estrae i contatti.
/// </summary>
public sealed class WebScraperService
{
    private static readonly string[] Paths = ["/", "/contatti", "/contact", "/chi-siamo", "/staff", "/privacy"];
    private readonly HttpClient _httpClient;
    private readonly LoggerService _logger;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public WebScraperService(HttpClient httpClient, LoggerService logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<(IReadOnlyCollection<string> emails, IReadOnlyCollection<string> pecs, IReadOnlyCollection<string> phones)> AnalyzeAsync(string baseUrl, int delayMs, CancellationToken cancellationToken)
    {
        var allEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allPecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allPhones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return ([], [], []);
        }

        foreach (var path in Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageUrl = Combine(baseUrl, path);
            var html = await GetWithRetryAsync(pageUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(html))
            {
                continue;
            }

            var parser = new HtmlParser();
            var document = await parser.ParseDocumentAsync(html, cancellationToken);
            var content = document.DocumentElement?.TextContent ?? html;

            foreach (var email in EmailExtractor.Extract(content))
            {
                allEmails.Add(email);
                if (PecIdentifier.IsPec(email))
                {
                    allPecs.Add(email);
                }
            }

            foreach (var phone in PhoneExtractor.Extract(content))
            {
                allPhones.Add(phone);
            }

            await _logger.LogAsync($"URL visitato: {pageUrl} - Email: {allEmails.Count}", cancellationToken);
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        return (allEmails.ToArray(), allPecs.ToArray(), allPhones.ToArray());
    }

    private async Task<string> GetWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(url, out var cached))
        {
            return cached;
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    await _logger.LogAsync($"HTTP {(int)response.StatusCode} su {url}", cancellationToken);
                    if ((int)response.StatusCode is 403 or 404 or 429 or >= 500)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 150), cancellationToken);
                        continue;
                    }

                    return string.Empty;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                _cache[url] = content;
                return content;
            }
            catch (TaskCanceledException)
            {
                await _logger.LogAsync($"Timeout su {url}", cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                await _logger.LogAsync($"Errore rete {url}: {ex.Message}", cancellationToken);
            }
            catch (Exception ex)
            {
                await _logger.LogAsync($"Errore generico {url}: {ex.Message}", cancellationToken);
                return string.Empty;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 200), cancellationToken);
        }

        return string.Empty;
    }

    private static string Combine(string baseUrl, string path)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return string.Empty;
        }

        return new Uri(baseUri, path).ToString();
    }
}
