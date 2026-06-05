using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using runts.Services;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using WebDriverManager.Helpers;

namespace runts.Helpers;

/// <summary>
/// Helper per automazione Chrome reale con comportamento umano e anti-detection.
/// </summary>
public sealed class UndetectedChromeHelper : IDisposable
{
    private IWebDriver? _driver;
    private WebDriverWait? _wait;
    private Actions? _actions;
    private readonly LoggerService _logger;
    private bool _disposed;

    public UndetectedChromeHelper(LoggerService logger, bool headless = true)
    {
        _logger = logger;
        InitializeDriver(headless);
    }

    public async Task<List<string>> SearchGoogleAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await _logger.LogAsync($"🔍 Google search: '{query}'", cancellationToken);
            await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Driver.Navigate().GoToUrl("https://www.google.com/?hl=it");
                await Task.Delay(3000, cancellationToken);

                try
                {
                    var screenshot = ((ITakesScreenshot)Driver).GetScreenshot();
                    var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    if (!string.IsNullOrWhiteSpace(desktopPath))
                    {
                        var screenshotPath = Path.Combine(desktopPath, $"google_debug_{DateTime.Now:HHmmss}.png");
                        screenshot.SaveAsFile(screenshotPath);
                        await _logger.LogAsync($"📸 Screenshot salvato: {screenshotPath}", cancellationToken);
                    }
                }
                catch
                {
                }

                var wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(10));
                var acceptSelectors = new[]
                {
                    "//button[contains(text(), 'Accetta')]",
                    "//button[contains(text(), 'Accept')]",
                    "//button[@id='L2AGLb']",
                    "//button[@id='W0wltc']",
                    "//button[contains(@aria-label, 'Accetta')]",
                    "//div[contains(text(), 'Accetta tutto')]/ancestor::button"
                };

                var cookiesAccepted = false;
                foreach (var selector in acceptSelectors)
                {
                    try
                    {
                        var acceptButton = wait.Until(d =>
                        {
                            var element = d.FindElement(By.XPath(selector));
                            return element.Displayed && element.Enabled ? element : null;
                        });
                        if (acceptButton is null)
                        {
                            continue;
                        }

                        await _logger.LogAsync("✓ Banner cookie trovato, click Accetta...", cancellationToken);
                        acceptButton.Click();
                        cookiesAccepted = true;
                        await Task.Delay(1000, cancellationToken);
                        break;
                    }
                    catch
                    {
                    }
                }

                if (cookiesAccepted)
                {
                    await _logger.LogAsync("✓ Cookie accettati", cancellationToken);
                }

                IWebElement? searchBox = null;
                var searchSelectors = new[]
                {
                    By.Name("q"),
                    By.CssSelector("input[name='q']"),
                    By.CssSelector("textarea[name='q']"),
                    By.Id("APjFqb")
                };

                foreach (var selector in searchSelectors)
                {
                    try
                    {
                        searchBox = wait.Until(d =>
                        {
                            var element = d.FindElement(selector);
                            return element.Displayed && element.Enabled ? element : null;
                        });

                        if (searchBox is not null)
                        {
                            await _logger.LogAsync($"✓ Search box trovato: {selector}", cancellationToken);
                            break;
                        }
                    }
                    catch
                    {
                    }
                }

                if (searchBox is null)
                {
                    await _logger.LogAsync("❌ Search box NON trovato dopo 10s", cancellationToken);
                    return;
                }

                if (Driver is IJavaScriptExecutor js)
                {
                    js.ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", searchBox);
                }
                await Task.Delay(500, cancellationToken);

                try
                {
                    _actions?.MoveToElement(searchBox).Build().Perform();
                    await Task.Delay(300, cancellationToken);
                }
                catch
                {
                }

                try
                {
                    searchBox.Click();
                }
                catch
                {
                    if (Driver is IJavaScriptExecutor jsClick)
                    {
                        jsClick.ExecuteScript("arguments[0].click();", searchBox);
                    }
                }
                await Task.Delay(500, cancellationToken);

                try
                {
                    searchBox.Clear();
                }
                catch
                {
                }

                await TypeLikeHuman(searchBox, query, cancellationToken);
                searchBox.SendKeys(OpenQA.Selenium.Keys.Enter);

                Wait.Until(d => d.FindElements(By.CssSelector("div#search")).Count > 0);
                await Task.Delay(4000, cancellationToken);
                await ScrollLikeHuman(cancellationToken);

                try
                {
                    var screenshot = ((ITakesScreenshot)Driver).GetScreenshot();
                    var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    if (!string.IsNullOrWhiteSpace(desktopPath))
                    {
                        var screenshotPath = Path.Combine(desktopPath, $"results_debug_{DateTime.Now:HHmmss}.png");
                        screenshot.SaveAsFile(screenshotPath);
                        await _logger.LogAsync($"📸 Risultati screenshot: {screenshotPath}", cancellationToken);
                    }
                }
                catch
                {
                }

                var linkElements = Driver.FindElements(By.CssSelector("div#search a[href], div.g a[href]"));
                foreach (var element in linkElements)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var href = element.GetDomAttribute("href");
                    if (string.IsNullOrWhiteSpace(href) || !href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (href.Contains("google.", StringComparison.OrdinalIgnoreCase) ||
                        href.Contains("/search?", StringComparison.OrdinalIgnoreCase) ||
                        href.Contains("/policies/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    results.Add(href);
                }
            }, cancellationToken);
            await _logger.LogAsync($"✓ Trovati {results.Count} risultati Google", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _logger.LogAsync($"Errore ricerca Google '{query}': {ex.Message}", cancellationToken);
        }

        return results.ToList();
    }

    public async Task<(IReadOnlyCollection<string> emails, string html)> ExtractEmailsFromPage(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Driver.Navigate().GoToUrl(url);
                Wait.Until(d => d.FindElements(By.TagName("body")).Count > 0);

                await HumanDelay(cancellationToken);
                await ScrollLikeHuman(cancellationToken);
                await HumanDelay(cancellationToken);

                var html = Driver.PageSource;
                var emails = EmailExtractor.Extract(html)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return ((IReadOnlyCollection<string>)emails, html);
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _logger.LogAsync($"Errore estrazione email '{url}': {ex.Message}", cancellationToken);
            return (Array.Empty<string>(), string.Empty);
        }
    }

    public async Task MoveMouseToElement(IWebElement element, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Driver is IJavaScriptExecutor js)
        {
            js.ExecuteScript("arguments[0].scrollIntoView({block: 'center', inline: 'nearest'});", element);
            await Task.Delay(Random.Shared.Next(15, 35), cancellationToken);
            js.ExecuteScript("arguments[0].focus();", element);
        }

        await Task.Delay(Random.Shared.Next(30, 80), cancellationToken);
    }

    public async Task TypeLikeHuman(IWebElement element, string text, CancellationToken cancellationToken = default)
    {
        element.Clear();
        foreach (var ch in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            element.SendKeys(ch.ToString());
            await Task.Delay(Random.Shared.Next(60, 151), cancellationToken);
        }
    }

    public async Task ScrollLikeHuman(CancellationToken cancellationToken = default)
    {
        if (Driver is not IJavaScriptExecutor js)
        {
            return;
        }

        var steps = Random.Shared.Next(3, 7);
        for (var i = 0; i < steps; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var distance = Random.Shared.Next(160, 420);
            js.ExecuteScript("window.scrollBy({top: arguments[0], behavior: 'smooth'});", distance);
            await Task.Delay(Random.Shared.Next(180, 420), cancellationToken);
        }
    }

    public async Task HumanDelay(CancellationToken cancellationToken = default)
    {
        await Task.Delay(Random.Shared.Next(400, 1201), cancellationToken);
    }

    private void InitializeDriver(bool headless)
    {
        _logger.LogAsync("═══════════════════════════════════════").GetAwaiter().GetResult();
        _logger.LogAsync("  CHROME REALE - MODALITÀ UMANA ATTIVA  ").GetAwaiter().GetResult();
        _logger.LogAsync("═══════════════════════════════════════").GetAwaiter().GetResult();

        var options = new ChromeOptions();
        _logger.LogAsync("[1/4] Avvio Chrome...").GetAwaiter().GetResult();
        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalOption("useAutomationExtension", false);
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--window-size=1920,1080");
        options.AddArgument("--lang=it-IT");
        options.AddArgument("--disable-gpu");
        options.AddArgument("--disable-notifications");
        options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        options.AddUserProfilePreference("credentials_enable_service", false);
        options.AddUserProfilePreference("profile.password_manager_enabled", false);
        options.AddUserProfilePreference("intl.accept_languages", "it-IT,it");

        if (headless)
        {
            options.AddArgument("--headless=new");
        }

        _logger.LogAsync("[2/4] Rimozione flag automazione JavaScript...").GetAwaiter().GetResult();
        new DriverManager().SetUpDriver(new ChromeConfig(), VersionResolveStrategy.MatchingBrowser);
        _logger.LogAsync("[3/4] Configurazione Actions API...").GetAwaiter().GetResult();

        var service = ChromeDriverService.CreateDefaultService();
        service.SuppressInitialDiagnosticInformation = true;
        service.HideCommandPromptWindow = true;

        _driver = new ChromeDriver(service, options, TimeSpan.FromSeconds(60));
        _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(45);
        _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(3);
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(20));
        _actions = new Actions(_driver);

        try
        {
            _driver.Manage().Window.Maximize();
            var handle = _driver.CurrentWindowHandle;
            _driver.SwitchTo().Window(handle);
            _logger.LogAsync("✓ Finestra Chrome in primo piano").GetAwaiter().GetResult();
        }
        catch
        {
        }

        ApplyAntiDetectionScripts();
        _logger.LogAsync("[4/4] ✓ Chrome pronto (modalità UMANA)").GetAwaiter().GetResult();
        _logger.LogAsync("═══════════════════════════════════════").GetAwaiter().GetResult();
    }

    private void ApplyAntiDetectionScripts()
    {
        if (_driver is not ChromeDriver chromeDriver)
        {
            return;
        }

        chromeDriver.ExecuteCdpCommand("Page.addScriptToEvaluateOnNewDocument", new Dictionary<string, object>
        {
            ["source"] = @"
Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
Object.defineProperty(navigator, 'platform', { get: () => 'Win32' });
Object.defineProperty(navigator, 'languages', { get: () => ['it-IT', 'it', 'en-US', 'en'] });
Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
window.chrome = window.chrome || { runtime: {} };
"
        });
    }

    private IWebDriver Driver => _driver ?? throw new ObjectDisposedException(nameof(UndetectedChromeHelper));

    private WebDriverWait Wait => _wait ?? throw new ObjectDisposedException(nameof(UndetectedChromeHelper));

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
            _driver = null;
            _wait = null;
            _actions = null;
            _disposed = true;
        }
    }
}
