using OpenQA.Selenium;
using runts.Helpers;

namespace runts.Services;

/// <summary>
/// Servizio scraping siti web tramite Chrome automation.
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
            using var chrome = new ChromeAutomationHelper(_logger, headless);
            await _logger.LogAsync($"Scansione sito: {baseUrl}", cancellationToken);
            var (emails, phones) = await chrome.ScanWebsiteForContactsAsync(baseUrl, delayMs, cancellationToken);

            foreach (var email in emails)
            {
                allEmails.Add(email);
                if (PecIdentifier.IsPec(email))
                {
                    allPecs.Add(email);
                }
            }

            foreach (var phone in phones)
            {
                allPhones.Add(phone);
            }

            await _logger.LogAsync(
                $"Scansione completata: {allEmails.Count} email | {allPecs.Count} PEC | {allPhones.Count} telefoni",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WebDriverException ex)
        {
            await _logger.LogAsync($"Errore Selenium scansione {baseUrl}: {ex.Message}", cancellationToken);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Errore scansione {baseUrl}: {ex.Message}", cancellationToken);
        }

        return (allEmails.ToArray(), allPecs.ToArray(), allPhones.ToArray());
    }
}
