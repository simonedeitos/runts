using PuppeteerSharp;
using PuppeteerSharp.Input;
using EasySearch.Services;

namespace EasySearch.Helpers;

/// <summary>
/// Helper per automazione browser con PuppeteerSharp e comportamento umano.
/// Riutilizza un solo browser e apre nuove pagine per ogni ricerca/scansione.
/// </summary>
public sealed class PuppeteerHelper : IDisposable, IAsyncDisposable
{
    private static readonly string[] SpamEmailTokens =
    [
        "example.com",
        "domain.com",
        "yourdomain",
        "noreply@",
        "no-reply@",
        "donotreply@",
        "do-not-reply@"
    ];

    private readonly LoggerService _logger;
    private readonly bool _headless;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private IBrowser? _browser;
    private bool _disposed;

    public PuppeteerHelper(LoggerService logger, bool headless = true)
    {
        _logger = logger;
        _headless = headless;
    }

    /// <summary>
    /// Inizializza browser UNA SOLA VOLTA (riutilizzato per tutte le ricerche).
    /// Download automatico Chromium se necessario.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_browser is not null)
        {
            return;
        }

        await _initializeLock.WaitAsync(ct);
        try
        {
            if (_browser is not null)
            {
                return;
            }

            await _logger.LogAsync("═══════════════════════════════════════", ct);
            await _logger.LogAsync("  PUPPETEER - MODALITÀ UMANA ATTIVA    ", ct);
            await _logger.LogAsync("═══════════════════════════════════════", ct);
            await _logger.LogAsync("[1/3] Download Chromium...", ct);

            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            ct.ThrowIfCancellationRequested();
            await _logger.LogAsync("[2/3] Avvio browser...", ct);

            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = _headless,
                DefaultViewport = new ViewPortOptions
                {
                    Width = 1920,
                    Height = 1080
                },
                Args =
                [
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-blink-features=AutomationControlled",
                    "--disable-web-security",
                    "--window-size=1920,1080",
                    "--lang=it-IT"
                ]
            });

            await _logger.LogAsync("[3/3] ✓ Browser pronto", ct);
            await _logger.LogAsync("      → navigator.webdriver = undefined", ct);
            await _logger.LogAsync("      → Anti-detection attivo", ct);
            await _logger.LogAsync("═══════════════════════════════════════", ct);
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    /// <summary>
    /// Cerca con strategia ibrida: Google → DuckDuckGo → Bing.
    /// Fallback automatico se il motore restituisce 0 risultati.
    /// </summary>
    public async Task<List<string>> SearchAsync(string query, CancellationToken ct = default, bool includeMapsLinks = false)
    {
        await InitializeAsync(ct);

        var googleResults = await SearchGoogleAsync(query, ct, includeMapsLinks);
        if (googleResults.Count > 0)
        {
            return googleResults;
        }

        await _logger.LogAsync("⚠ Google: 0 risultati, provo DuckDuckGo...", ct);
        var duckDuckGoResults = await SearchDuckDuckGoAsync(query, ct);
        if (duckDuckGoResults.Count > 0)
        {
            return duckDuckGoResults;
        }

        await _logger.LogAsync("⚠ DuckDuckGo: 0 risultati, provo Bing...", ct);
        return await SearchBingAsync(query, ct);
    }

    /// <summary>
    /// Estrae email da pagina web con regex lato JavaScript e link mailto.
    /// </summary>
    public async Task<HashSet<string>> ExtractEmailsFromPageAsync(string url, CancellationToken ct = default)
    {
        var (emails, _) = await ExtractPageDataAsync(url, ct);
        return emails;
    }

    public Task<(HashSet<string> emails, string html)> ExtractPageContentAsync(string url, CancellationToken ct = default)
        => ExtractPageDataAsync(url, ct);

    private async Task<List<string>> SearchGoogleAsync(string query, CancellationToken ct, bool includeMapsLinks = false)
    {
        await _logger.LogAsync($"🔍 Google: '{query}'", ct);
        await using var page = await CreatePageAsync(ct);

        try
        {
            await page.GoToAsync("https://www.google.com/?hl=it",
                new NavigationOptions
                {
                    WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                    Timeout = 20000
                });

            await TryAcceptGoogleCookiesAsync(page, ct);

            var searchBox = await page.WaitForSelectorAsync(
                "textarea[name='q'], input[name='q']",
                new WaitForSelectorOptions
                {
                    Visible = true,
                    Timeout = 10000
                });

            if (searchBox is null)
            {
                return [];
            }

            await searchBox.ClickAsync();
            await DelayAsync(300, ct);
            await searchBox.TypeAsync(query, new TypeOptions { Delay = 80 });
            await page.Keyboard.PressAsync("Enter");
            await DelayAsync(2500, ct);
            await page.EvaluateExpressionAsync("window.scrollTo({ top: document.body.scrollHeight * 0.35, behavior: 'smooth' })");
            await DelayAsync(1200, ct);

            // Build the Google-domain filter: when includeMapsLinks is true, allow maps.google.com paths
            var googleFilter = includeMapsLinks
                ? "href.includes('youtube.')"
                : "href.includes('google.') || href.includes('youtube.')";

            var urls = await page.EvaluateFunctionAsync<string[]>(
                $@"() => {{
                    const results = [];
                    const selectors = [
                        'div.g a[href]',
                        'div[data-snc] a[href]',
                        'a[jsname][data-ved]'
                    ];

                    for (const selector of selectors) {{
                        for (const link of document.querySelectorAll(selector)) {{
                            const href = link.href;
                            if (!href || !href.startsWith('http')) {{
                                continue;
                            }}

                            if ({googleFilter}) {{
                                continue;
                            }}

                            results.push(href);
                        }}
                    }}

                    return [...new Set(results)].slice(0, 10);
                }}");

            var filtered = NormalizeUrls(urls);
            await _logger.LogAsync($"✓ Google: {filtered.Count} risultati", ct);
            return filtered;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _logger.LogAsync($"⚠ Google errore: {ex.Message}", ct);
            return [];
        }
    }

    private async Task<List<string>> SearchDuckDuckGoAsync(string query, CancellationToken ct)
    {
        await _logger.LogAsync($"🦆 DuckDuckGo: '{query}'", ct);
        await using var page = await CreatePageAsync(ct);

        try
        {
            var url = $"https://duckduckgo.com/?q={Uri.EscapeDataString(query)}&kl=it-it";
            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                Timeout = 20000
            });
            await DelayAsync(2000, ct);

            var urls = await page.EvaluateFunctionAsync<string[]>(
                @"() => {
                    const results = [];
                    const selectors = [
                        'a[data-testid=""result-title-a""][href]',
                        '.result__title a[href]',
                        'article a[href]'
                    ];

                    for (const selector of selectors) {
                        for (const link of document.querySelectorAll(selector)) {
                            const href = link.href;
                            if (!href || !href.startsWith('http')) {
                                continue;
                            }

                            if (href.includes('duckduckgo.com')) {
                                continue;
                            }

                            results.push(href);
                        }
                    }

                    return [...new Set(results)].slice(0, 10);
                }");

            var filtered = NormalizeUrls(urls);
            await _logger.LogAsync($"✓ DuckDuckGo: {filtered.Count} risultati", ct);
            return filtered;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _logger.LogAsync($"⚠ DuckDuckGo errore: {ex.Message}", ct);
            return [];
        }
    }

    private async Task<List<string>> SearchBingAsync(string query, CancellationToken ct)
    {
        await _logger.LogAsync($"🔷 Bing: '{query}'", ct);
        await using var page = await CreatePageAsync(ct);

        try
        {
            var url = $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}&setlang=it-IT";
            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                Timeout = 20000
            });
            await DelayAsync(2000, ct);

            var urls = await page.EvaluateFunctionAsync<string[]>(
                @"() => {
                    const results = [];

                    for (const link of document.querySelectorAll('li.b_algo h2 a[href], li.b_algo a[href]')) {
                        const href = link.href;
                        if (href && href.startsWith('http') && !href.includes('bing.com')) {
                            results.push(href);
                        }
                    }

                    for (const cite of document.querySelectorAll('li.b_algo cite')) {
                        const text = (cite.textContent || '').trim()
                            .replace(/^https?:\/\//i, '')
                            .replace(/\/.*$/, '');
                        if (text) {
                            results.push(`https://${text}`);
                        }
                    }

                    return [...new Set(results)].slice(0, 10);
                }");

            var filtered = NormalizeUrls(urls);
            await _logger.LogAsync($"✓ Bing: {filtered.Count} risultati", ct);
            return filtered;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _logger.LogAsync($"⚠ Bing errore: {ex.Message}", ct);
            return [];
        }
    }

    private async Task<(HashSet<string> emails, string html)> ExtractPageDataAsync(string url, CancellationToken ct)
    {
        await InitializeAsync(ct);
        await using var page = await CreatePageAsync(ct);

        try
        {
            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                Timeout = 20000
            });
            await DelayAsync(1500, ct);
            await page.EvaluateExpressionAsync("window.scrollTo({ top: document.body.scrollHeight * 0.5, behavior: 'smooth' })");
            await DelayAsync(800, ct);

            var emails = await page.EvaluateFunctionAsync<string[]>(
                @"() => {
                    const regex = /[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}/g;
                    const text = document.body?.innerText || document.body?.textContent || '';
                    const found = text.match(regex) || [];
                    const mailto = Array.from(document.querySelectorAll('a[href^=""mailto:""]'))
                        .map(a => (a.getAttribute('href') || '').replace(/^mailto:/i, '').split('?')[0].trim());

                    return [...new Set([...found, ...mailto])];
                }");

            var html = await page.GetContentAsync();
            return (NormalizeEmails(emails), html);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _logger.LogAsync($"Errore estrazione email '{url}': {ex.Message}", ct);
            return ([], string.Empty);
        }
    }

    private async Task<IPage> CreatePageAsync(CancellationToken ct)
    {
        await InitializeAsync(ct);

        var page = await Browser.NewPageAsync();
        await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
        await page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
        {
            ["Accept-Language"] = "it-IT,it;q=0.9,en-US;q=0.8,en;q=0.7"
        });
        await page.SetUserAgentAsync(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        await page.EvaluateFunctionOnNewDocumentAsync(
            @"() => {
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
                Object.defineProperty(navigator, 'languages', { get: () => ['it-IT', 'it', 'en-US', 'en'] });
            }");

        return page;
    }

    private async Task TryAcceptGoogleCookiesAsync(IPage page, CancellationToken ct)
    {
        try
        {
            var accepted = await page.EvaluateFunctionAsync<bool>(
                @"() => {
                    const labels = ['accetta', 'accept', 'accetta tutto', 'accept all'];
                    const buttons = Array.from(document.querySelectorAll('button, [role=""button""]'));
                    const button = buttons.find(candidate => {
                        const text = (candidate.textContent || '').trim().toLowerCase();
                        return candidate.id === 'L2AGLb' || labels.some(label => text.includes(label));
                    });

                    if (!button) {
                        return false;
                    }

                    button.click();
                    return true;
                }");

            if (accepted)
            {
                await DelayAsync(500, ct);
                await _logger.LogAsync("✓ Cookie accettati", ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _logger.LogAsync($"⚠ Cookie banner non gestito: {ex.Message}", ct);
        }
    }

    private static List<string> NormalizeUrls(IEnumerable<string>? urls)
    {
        return (urls ?? [])
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private static HashSet<string> NormalizeEmails(IEnumerable<string>? emails)
    {
        return (emails ?? [])
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email.Trim().TrimEnd('.', ',', ';', ':', ')', ']', '}').ToLowerInvariant())
            .Where(email => email.Count(ch => ch == '@') == 1)
            .Where(email => SpamEmailTokens.All(token => !email.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Task DelayAsync(int milliseconds, CancellationToken ct)
        => milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds, ct);

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            await _browser.DisposeAsync();
            _browser = null;
        }

        _initializeLock.Dispose();
    }

    private IBrowser Browser => _browser ?? throw new ObjectDisposedException(nameof(PuppeteerHelper));

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PuppeteerHelper));
        }
    }
}
