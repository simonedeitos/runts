using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using runts.Services;
using System.Diagnostics;
using System.Text;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using WebDriverManager.Helpers;

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

    public static (bool isInstalled, string? chromePath, string? errorMessage) VerifyChromeInstallation()
    {
        var chromeExe = GetChromeExecutablePath();
        if (string.IsNullOrWhiteSpace(chromeExe))
        {
            return (false, null, "Google Chrome non è installato in paths standard.");
        }

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(chromeExe);
            var version = versionInfo.FileVersion;
            return (true, chromeExe, string.IsNullOrWhiteSpace(version) ? null : $"Versione Chrome rilevata: {version}");
        }
        catch (Exception ex)
        {
            return (false, chromeExe, $"Impossibile leggere versione Chrome: {ex.Message}");
        }
    }

    private void InitializeDriver(bool headless)
    {
        _logger.LogAsync("═══════════════════════════════════════").GetAwaiter().GetResult();
        _logger.LogAsync("  INIZIALIZZAZIONE CHROME AUTOMATION  ").GetAwaiter().GetResult();
        _logger.LogAsync("═══════════════════════════════════════").GetAwaiter().GetResult();

        var options = new ChromeOptions();
        _logger.LogAsync("[1/4] Rilevamento Chrome installato...").GetAwaiter().GetResult();
        var chromeExe = GetChromeExecutablePath();
        string? chromeVersion = null;
        string? chromeMajorVersion = null;
        if (!string.IsNullOrWhiteSpace(chromeExe))
        {
            options.BinaryLocation = chromeExe;
            chromeVersion = GetChromeVersion(chromeExe);
            chromeMajorVersion = GetMajorVersion(chromeVersion);
            _logger.LogAsync($"✓ Chrome trovato: {chromeExe}").GetAwaiter().GetResult();
            _logger.LogAsync($"✓ Versione Chrome: {chromeVersion ?? "sconosciuta"}").GetAwaiter().GetResult();
        }
        else
        {
            _logger.LogAsync("⚠ Chrome non trovato in paths standard").GetAwaiter().GetResult();
        }

        _logger.LogAsync("[2/4] Configurazione opzioni Chrome...").GetAwaiter().GetResult();
        if (headless)
        {
            options.AddArgument("--headless=new");
            _logger.LogAsync("✓ Modalità headless attivata").GetAwaiter().GetResult();
        }
        else
        {
            _logger.LogAsync("✓ Modalità visibile attivata").GetAwaiter().GetResult();
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
            _logger.LogAsync("[3/4] Setup ChromeDriver automatico...").GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(chromeMajorVersion))
            {
                _logger.LogAsync($"Richiesta ChromeDriver versione {chromeMajorVersion} (compatibile con Chrome installato)").GetAwaiter().GetResult();
                try
                {
                    new DriverManager().SetUpDriver(new ChromeConfig(), VersionResolveStrategy.MatchingBrowser);
                    _logger.LogAsync($"✓ ChromeDriver v{chromeMajorVersion} configurato correttamente").GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogAsync($"⚠ Setup con MatchingBrowser fallito: {ex.Message}").GetAwaiter().GetResult();
                    if (!TryDownloadSpecificChromeDriver(chromeMajorVersion).GetAwaiter().GetResult())
                    {
                        _logger.LogAsync("Tentativo con versione latest...").GetAwaiter().GetResult();
                        new DriverManager().SetUpDriver(new ChromeConfig());
                    }
                }
            }
            else
            {
                _logger.LogAsync("Versione Chrome non rilevata, uso configurazione automatica").GetAwaiter().GetResult();
                try
                {
                    new DriverManager().SetUpDriver(new ChromeConfig(), VersionResolveStrategy.MatchingBrowser);
                }
                catch (Exception ex)
                {
                    _logger.LogAsync($"⚠ Setup MatchingBrowser non disponibile: {ex.Message}").GetAwaiter().GetResult();
                    new DriverManager().SetUpDriver(new ChromeConfig());
                }
            }

            var driverExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chromedriver.exe");
            if (File.Exists(driverExePath))
            {
                _logger.LogAsync($"ChromeDriver path: {driverExePath}").GetAwaiter().GetResult();
                _logger.LogAsync($"ChromeDriver size: {new FileInfo(driverExePath).Length / 1024} KB").GetAwaiter().GetResult();
                _logger.LogAsync($"ChromeDriver modificato: {File.GetLastWriteTime(driverExePath)}").GetAwaiter().GetResult();
                try
                {
                    var driverVersionInfo = FileVersionInfo.GetVersionInfo(driverExePath);
                    var driverVersion = driverVersionInfo.FileVersion ?? driverVersionInfo.ProductVersion;
                    _logger.LogAsync($"ChromeDriver scaricato: v{driverVersion}").GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(chromeMajorVersion) && !string.IsNullOrWhiteSpace(driverVersion))
                    {
                        var driverMajor = GetMajorVersion(driverVersion);
                        if (!string.Equals(chromeMajorVersion, driverMajor, StringComparison.Ordinal))
                        {
                            _logger.LogAsync($"⚠ ATTENZIONE: Chrome v{chromeMajorVersion} ma ChromeDriver v{driverMajor} - potrebbero esserci incompatibilità").GetAwaiter().GetResult();
                        }
                        else
                        {
                            _logger.LogAsync($"✓ Compatibilità verificata: Chrome {chromeMajorVersion} = ChromeDriver {driverMajor}").GetAwaiter().GetResult();
                        }
                    }
                }
                catch
                {
                }
            }
            else
            {
                _logger.LogAsync($"⚠ ChromeDriver non trovato in: {driverExePath}").GetAwaiter().GetResult();
            }

            _logger.LogAsync("[4/4] Avvio sessione Chrome...").GetAwaiter().GetResult();
            var service = ChromeDriverService.CreateDefaultService();
            service.SuppressInitialDiagnosticInformation = true;
            service.HideCommandPromptWindow = true;

            _driver = new ChromeDriver(service, options, TimeSpan.FromSeconds(60));
            _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
            _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
            _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(15));
            _logger.LogAsync($"✓ Sessione Chrome inizializzata con successo (Headless: {headless})").GetAwaiter().GetResult();
            _logger.LogAsync("✓ Chrome pronto per l'automazione!").GetAwaiter().GetResult();
        }
        catch (WebDriverException ex) when (ex.Message.Contains("This version of ChromeDriver only supports Chrome version", StringComparison.OrdinalIgnoreCase))
        {
            var errorMsg = BuildVersionMismatchErrorMessage(ex, chromeVersion, chromeExe);
            _logger.LogAsync("❌ ERRORE INCOMPATIBILITÀ VERSIONE").GetAwaiter().GetResult();
            _logger.LogAsync(errorMsg).GetAwaiter().GetResult();
            throw new InvalidOperationException(errorMsg, ex);
        }
        catch (WebDriverException ex)
        {
            var errorMsg = BuildDetailedErrorMessage(ex, chromeExe);
            _logger.LogAsync($"❌ ERRORE WebDriver: {ex.Message}").GetAwaiter().GetResult();
            _logger.LogAsync(errorMsg).GetAwaiter().GetResult();
            throw new InvalidOperationException(errorMsg, ex);
        }
        catch (Exception ex)
        {
            var errorMsg = BuildDetailedErrorMessage(ex, chromeExe);
            _logger.LogAsync($"❌ ERRORE inizializzazione Chrome: {ex.Message}").GetAwaiter().GetResult();
            _logger.LogAsync(errorMsg).GetAwaiter().GetResult();
            throw new InvalidOperationException(errorMsg, ex);
        }
    }

    private static string? GetChromeExecutablePath()
    {
        var chromePaths = new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
            "/usr/bin/google-chrome",
            "/usr/bin/google-chrome-stable",
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
        };

        return chromePaths.FirstOrDefault(File.Exists);
    }

    private static string? GetChromeVersion(string? chromeExePath)
    {
        if (string.IsNullOrEmpty(chromeExePath) || !File.Exists(chromeExePath))
        {
            return null;
        }

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(chromeExePath);
            return versionInfo.FileVersion ?? versionInfo.ProductVersion;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetMajorVersion(string? fullVersion)
    {
        if (string.IsNullOrWhiteSpace(fullVersion))
        {
            return null;
        }

        var parts = fullVersion.Split('.');
        return parts.Length > 0 ? parts[0] : null;
    }

    private static string BuildDetailedErrorMessage(Exception ex, string? chromeExePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════");
        sb.AppendLine("ERRORE AVVIO GOOGLE CHROME");
        sb.AppendLine("═══════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("DETTAGLI ERRORE:");
        sb.AppendLine($"  {ex.Message}");
        sb.AppendLine();
        sb.AppendLine("DIAGNOSTICA:");
        sb.AppendLine($"  Chrome installato: {(string.IsNullOrEmpty(chromeExePath) ? "❌ NO" : $"✓ {chromeExePath}")}");
        sb.AppendLine($"  Directory applicazione: {AppDomain.CurrentDomain.BaseDirectory}");
        sb.AppendLine($"  Sistema operativo: {Environment.OSVersion}");
        sb.AppendLine($"  .NET Runtime: {Environment.Version}");
        sb.AppendLine();
        sb.AppendLine("POSSIBILI CAUSE:");
        sb.AppendLine("  1. Chrome non installato o versione troppo vecchia");
        sb.AppendLine("  2. ChromeDriver incompatibile (WebDriverManager dovrebbe risolvere)");
        sb.AppendLine("  3. Antivirus/Firewall blocca l'esecuzione");
        sb.AppendLine("  4. Permessi insufficienti (provare come Amministratore)");
        sb.AppendLine("  5. Processo Chrome già in esecuzione in modalità incompatibile");
        sb.AppendLine();
        sb.AppendLine("SOLUZIONI:");
        sb.AppendLine("  1. Aggiorna Google Chrome all'ultima versione");
        sb.AppendLine("     Download: https://www.google.com/chrome/");
        sb.AppendLine("  2. Esegui l'applicazione come Amministratore");
        sb.AppendLine("  3. Disabilita temporaneamente antivirus/firewall");
        sb.AppendLine("  4. Chiudi tutte le finestre Chrome aperte");
        sb.AppendLine("  5. Riavvia il computer");
        sb.AppendLine();
        sb.AppendLine("Se il problema persiste, contatta il supporto tecnico");
        sb.AppendLine("con il log completo presente in Data\\Logs\\");
        sb.AppendLine("═══════════════════════════════════════════════════");

        return sb.ToString();
    }

    private static string BuildVersionMismatchErrorMessage(Exception ex, string? chromeVersion, string? chromeExePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════");
        sb.AppendLine("ERRORE: INCOMPATIBILITÀ VERSIONE CHROME/CHROMEDRIVER");
        sb.AppendLine("═══════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("PROBLEMA:");
        sb.AppendLine($"  Chrome installato: v{chromeVersion ?? "sconosciuta"}");
        sb.AppendLine("  ChromeDriver scaricato: versione NON compatibile");
        sb.AppendLine();
        sb.AppendLine("DETTAGLI ERRORE:");
        sb.AppendLine($"  {ex.Message}");
        sb.AppendLine();
        sb.AppendLine("SOLUZIONI IMMEDIATE:");
        sb.AppendLine("  1. AGGIORNA CHROME all'ultima versione stabile");
        sb.AppendLine("     Download: https://www.google.com/chrome/");
        sb.AppendLine();
        sb.AppendLine("  2. RIAVVIA L'APPLICAZIONE dopo aver aggiornato Chrome");
        sb.AppendLine("     Il sistema scaricherà automaticamente il driver compatibile");
        sb.AppendLine();
        sb.AppendLine("  3. Se il problema persiste:");
        sb.AppendLine("     - Chiudi tutte le finestre Chrome");
        sb.AppendLine("     - Elimina manualmente ChromeDriver:");
        sb.AppendLine($"       {Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chromedriver.exe")}");
        sb.AppendLine("     - Riavvia l'applicazione");
        sb.AppendLine();
        sb.AppendLine("DIAGNOSTICA:");
        sb.AppendLine($"  Chrome path: {chromeExePath ?? "non trovato"}");
        sb.AppendLine($"  Directory app: {AppDomain.CurrentDomain.BaseDirectory}");
        sb.AppendLine("═══════════════════════════════════════════════════");
        return sb.ToString();
    }

    private async Task<bool> TryDownloadSpecificChromeDriver(string chromeMajorVersion)
    {
        try
        {
            await _logger.LogAsync($"Tentativo download manuale ChromeDriver v{chromeMajorVersion}...");
            await _logger.LogAsync("Download manuale non implementato, usa WebDriverManager");
            return false;
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Download manuale fallito: {ex.Message}");
            return false;
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
