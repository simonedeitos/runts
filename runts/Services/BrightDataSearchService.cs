using AngleSharp;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using runts.Helpers;

namespace runts.Services;

/// <summary>
/// Servizio ricerca web tramite Bright Data Web Unlocker API.
/// </summary>
public sealed class BrightDataSearchService : IDisposable
{
    private const string BrightDataProxyUrl = "http://brd.superproxy.io:33335";
    private readonly LoggerService _logger;
    private bool _disposed;

    public BrightDataSearchService(LoggerService logger)
    {
        _logger = logger;
    }

    public bool IsConfigured()
    {
        return RegistrySettingsManager.IsBrightDataConfigured();
    }

    public async Task<List<string>> SearchGoogleAsync(string query, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            await _logger.LogAsync("⚠ Bright Data non configurato, ricerca Google saltata", cancellationToken);
            return [];
        }

        try
        {
            var googleUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}&hl=it&gl=it";
            await _logger.LogAsync($"Bright Data → Google: '{query}'", cancellationToken);
            var html = await FetchWithBrightDataAsync(googleUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(html))
            {
                return [];
            }

            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html), cancellationToken);
            var results = document.QuerySelectorAll("div#search div.g a[href], div.g a[href]")
                .Select(a => NormalizeResultUrl(a.GetAttribute("href")))
                .Where(href => !string.IsNullOrWhiteSpace(href) &&
                               href.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                               !href.Contains("google.", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            await _logger.LogAsync($"Google risultati: {results.Count}", cancellationToken);
            return results;
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Errore ricerca Google: {ex.Message}", cancellationToken);
            return [];
        }
    }

    public async Task<List<string>> SearchBingAsync(string query, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            await _logger.LogAsync("⚠ Bright Data non configurato, ricerca Bing saltata", cancellationToken);
            return [];
        }

        try
        {
            var bingUrl = $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}";
            await _logger.LogAsync($"Bright Data → Bing: '{query}'", cancellationToken);
            var html = await FetchWithBrightDataAsync(bingUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(html))
            {
                return [];
            }

            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html), cancellationToken);
            var results = document.QuerySelectorAll("li.b_algo h2 a[href]")
                .Select(a => a.GetAttribute("href") ?? string.Empty)
                .Where(href => !string.IsNullOrWhiteSpace(href) &&
                               href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            await _logger.LogAsync($"Bing risultati: {results.Count}", cancellationToken);
            return results;
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Errore ricerca Bing: {ex.Message}", cancellationToken);
            return [];
        }
    }

    public async Task<string> FetchPageAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            await _logger.LogAsync("⚠ Bright Data non configurato, fetch pagina saltato", cancellationToken);
            return string.Empty;
        }

        return await FetchWithBrightDataAsync(url, cancellationToken);
    }

    private async Task<string> FetchWithBrightDataAsync(string targetUrl, CancellationToken cancellationToken)
    {
        var apiKey = RegistrySettingsManager.GetBrightDataApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return string.Empty;
        }

        try
        {
            using var handler = new HttpClientHandler
            {
                Proxy = new WebProxy(BrightDataProxyUrl),
                Credentials = new NetworkCredential(apiKey, string.Empty),
                UseProxy = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            request.Headers.AcceptLanguage.ParseAdd("it-IT,it;q=0.9,en;q=0.8");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:")));

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await _logger.LogAsync($"Bright Data HTTP {(int)response.StatusCode}: {targetUrl}", cancellationToken);
                return string.Empty;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            await _logger.LogAsync($"✓ Bright Data fetch: {html.Length} byte", cancellationToken);
            return html;
        }
        catch (HttpRequestException ex)
        {
            await _logger.LogAsync($"Errore HTTP Bright Data: {ex.Message}", cancellationToken);
            return string.Empty;
        }
        catch (TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            await _logger.LogAsync($"Timeout Bright Data: {targetUrl}", cancellationToken);
            return string.Empty;
        }
    }

    private static string NormalizeResultUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return string.Empty;
        }

        if (href.StartsWith("/url?", StringComparison.OrdinalIgnoreCase))
        {
            var value = href.TrimStart('/');
            var query = value.Split('?', 2).Skip(1).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(query))
            {
                foreach (var parameter in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = parameter.Split('=', 2);
                    if (parts.Length == 2 && parts[0].Equals("q", StringComparison.OrdinalIgnoreCase))
                    {
                        return Uri.UnescapeDataString(parts[1]);
                    }
                }
            }
        }

        return href;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
