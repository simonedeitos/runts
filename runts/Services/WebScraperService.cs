using EasySearch.Helpers;
using System.Net;
using System.Text.RegularExpressions;

namespace EasySearch.Services;

/// <summary>
/// Servizio scraping siti web tramite PuppeteerSharp.
/// </summary>
public sealed partial class WebScraperService
{
    private readonly LoggerService _logger;

    public WebScraperService(LoggerService logger)
    {
        _logger = logger;
    }

    public async Task<(IReadOnlyCollection<string> emails, IReadOnlyCollection<string> pecs, IReadOnlyCollection<string> phones, string indirizzo)> AnalyzeAsync(
        string baseUrl,
        int delayMs,
        bool headless = true,
        bool searchEmail = true,
        bool searchPec = true,
        bool searchPhone = true,
        bool searchWebsite = true,
        bool searchAddress = false,
        CancellationToken cancellationToken = default)
    {
        var allEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allPecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allPhones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var indirizzo = string.Empty;

        if (string.IsNullOrWhiteSpace(baseUrl) || !searchWebsite)
        {
            return ([], [], [], string.Empty);
        }

        try
        {
            using var chrome = new PuppeteerHelper(_logger, headless);
            await chrome.InitializeAsync(cancellationToken);
            await _logger.LogAsync($"Scansione sito: {baseUrl}", cancellationToken);
            foreach (var pageUrl in GetPagesToScan(baseUrl))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (emails, html) = await chrome.ExtractPageContentAsync(pageUrl, cancellationToken);
                if (string.IsNullOrWhiteSpace(html))
                {
                    continue;
                }

                if (searchEmail || searchPec)
                {
                    foreach (var email in emails)
                    {
                        if (searchEmail)
                        {
                            allEmails.Add(email);
                        }

                        if (searchPec && PecIdentifier.IsPec(email))
                        {
                            allPecs.Add(email);
                        }
                    }
                }

                if (searchPhone)
                {
                    allPhones.UnionWith(PhoneExtractor.Extract(html));
                }

                if (searchAddress && string.IsNullOrWhiteSpace(indirizzo))
                {
                    indirizzo = ExtractAddress(html);
                }

                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
            }

            await _logger.LogAsync(
                $"Scansione completata: {allEmails.Count} email | {allPecs.Count} PEC | {allPhones.Count} telefoni | indirizzo {(string.IsNullOrWhiteSpace(indirizzo) ? "n/d" : "trovato")}",
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

        return (allEmails.ToArray(), allPecs.ToArray(), allPhones.ToArray(), indirizzo);
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

    private static string ExtractAddress(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
        text = Regex.Replace(text, "\\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var match = ItalianAddressRegex().Match(text);
        return match.Success ? match.Value.Trim(' ', ',', ';', '.', '-') : string.Empty;
    }

    [GeneratedRegex(@"\b(?:via|viale|piazza|corso|largo|vicolo|contrada|strada|piazzale|località|loc\.)\s+[A-Za-zÀ-ÿ0-9'`\-\.\s]{3,80}?\s+(?:n\.?\s*)?\d+[A-Za-z]?\s*(?:,|\-|–)?\s*(?:\d{5}\s+)?[A-Za-zÀ-ÿ'`\-\.\s]{2,60}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ItalianAddressRegex();
}
