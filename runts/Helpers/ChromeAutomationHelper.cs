using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using runts.Services;

namespace runts.Helpers;

/// <summary>
/// Helper per automazione Chrome con Selenium WebDriver.
/// Ogni istanza gestisce una sessione Chrome dedicata.
/// </summary>
public sealed class ChromeAutomationHelper : IDisposable
{
    private IWebDriver? _driver;
    private WebDriverWait? _wait;
    private readonly LoggerService _logger;
    private bool _disposed;

    public ChromeAutomationHelper(LoggerService logger, bool headless = true)
    {
        _logger = logger;
        InitializeDriver(headless);
    }

    public async Task<List<string>> SearchGoogleAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<string>();

        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                Driver.Navigate().GoToUrl($"https://www.google.com/search?q={Uri.EscapeDataString(query)}&hl=it");
                Wait.Until(d => d.FindElements(By.CssSelector("div#search")).Count > 0);

                var linkElements = Driver.FindElements(By.CssSelector("div.g a[href]"));
                foreach (var element in linkElements)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var href = element.GetDomAttribute("href");
                        if (!string.IsNullOrWhiteSpace(href) &&
                            href.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                            !href.Contains("google.", StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(href);
                        }
                    }
                    catch (StaleElementReferenceException)
                    {
                    }
                }
            }, cancellationToken);

            await _logger.LogAsync($"Google search '{query}': {results.Count} risultati", cancellationToken);
        }
        catch (WebDriverException ex)
        {
            await LogDriverExceptionAsync($"Errore ricerca Google '{query}'", ex, cancellationToken);
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<string>> SearchBingAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<string>();

        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                Driver.Navigate().GoToUrl($"https://www.bing.com/search?q={Uri.EscapeDataString(query)}");
                Wait.Until(d => d.FindElements(By.CssSelector("ol#b_results")).Count > 0);

                var linkElements = Driver.FindElements(By.CssSelector("li.b_algo h2 a[href]"));
                foreach (var element in linkElements)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var href = element.GetDomAttribute("href");
                        if (!string.IsNullOrWhiteSpace(href) && href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(href);
                        }
                    }
                    catch (StaleElementReferenceException)
                    {
                    }
                }
            }, cancellationToken);

            await _logger.LogAsync($"Bing search '{query}': {results.Count} risultati", cancellationToken);
        }
        catch (WebDriverException ex)
        {
            await LogDriverExceptionAsync($"Errore ricerca Bing '{query}'", ex, cancellationToken);
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string> NavigateAndExtractHtmlAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                Driver.Navigate().GoToUrl(url);
                Wait.Until(d => d.FindElements(By.TagName("body")).Count > 0);
                return Driver.PageSource;
            }, cancellationToken);
        }
        catch (WebDriverException ex)
        {
            await LogDriverExceptionAsync($"Errore estrazione HTML '{url}'", ex, cancellationToken);
            return string.Empty;
        }
    }

    public async Task<(List<string> emails, List<string> phones)> ScanWebsiteForContactsAsync(
        string baseUrl,
        int delayMs,
        CancellationToken cancellationToken = default)
    {
        var allEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allPhones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pageUrl in GetPagesToScan(baseUrl))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var html = await NavigateAndExtractHtmlAsync(pageUrl, cancellationToken);
                if (string.IsNullOrWhiteSpace(html))
                {
                    continue;
                }

                foreach (var email in EmailExtractor.Extract(html))
                {
                    allEmails.Add(email);
                }

                foreach (var phone in PhoneExtractor.Extract(html))
                {
                    allPhones.Add(phone);
                }

                await _logger.LogAsync($"Scansione {pageUrl}: {allEmails.Count} email, {allPhones.Count} telefoni", cancellationToken);

                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                await _logger.LogAsync($"Errore scansione {pageUrl}: {ex.Message}", cancellationToken);
            }
        }

        return (allEmails.ToList(), allPhones.ToList());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _driver?.Quit();
            _driver?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _disposed = true;
            _driver = null;
            _wait = null;
        }
    }

    private IWebDriver Driver => _driver ?? throw new ObjectDisposedException(nameof(ChromeAutomationHelper));

    private WebDriverWait Wait => _wait ?? throw new ObjectDisposedException(nameof(ChromeAutomationHelper));

    private void InitializeDriver(bool headless)
    {
        var options = new ChromeOptions();
        if (headless)
        {
            options.AddArgument("--headless=new");
        }

        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-gpu");
        options.AddArgument("--window-size=1920,1080");
        options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        options.AddArgument("--log-level=3");
        options.AddArgument("--silent");
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalOption("useAutomationExtension", false);
        options.AddUserProfilePreference("profile.default_content_setting_values.images", 2);
        options.AddUserProfilePreference("profile.default_content_setting_values.notifications", 2);

        try
        {
            _driver = new ChromeDriver(options);
            _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
            _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
            _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(15));
            _logger.LogAsync($"Sessione Chrome inizializzata (Headless: {headless})").GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogAsync($"ERRORE inizializzazione Chrome: {ex.Message}").GetAwaiter().GetResult();
            throw new InvalidOperationException("Impossibile avviare Chrome. Verificare installazione Chrome e ChromeDriver.", ex);
        }
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

    private async Task LogDriverExceptionAsync(string context, WebDriverException ex, CancellationToken cancellationToken)
    {
        await _logger.LogAsync($"{context}: {ex.Message}", cancellationToken);
        if (ex.Message.Contains("disconnected", StringComparison.OrdinalIgnoreCase))
        {
            await _logger.LogAsync("Sessione Chrome crashata, continuo con il prossimo tentativo.", cancellationToken);
        }
    }
}
