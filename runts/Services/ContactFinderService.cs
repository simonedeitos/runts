using runts.Models;
using System.Threading.Channels;
using OpenQA.Selenium;

namespace runts.Services;

/// <summary>
/// Orchestratore producer/consumer per ricerca sito, scraping contatti e salvataggio continuo su CSV.
/// </summary>
public sealed class ContactFinderService
{
    private readonly SearchEngineService _searchEngineService;
    private readonly WebScraperService _webScraperService;
    private readonly CsvManager _csvManager;
    private readonly LoggerService _logger;
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    private SemaphoreSlim _workerSemaphore = new(1, 1);

    public ContactFinderService(SearchEngineService searchEngineService, WebScraperService webScraperService, CsvManager csvManager, LoggerService logger)
    {
        _searchEngineService = searchEngineService;
        _webScraperService = webScraperService;
        _csvManager = csvManager;
        _logger = logger;
    }

    public void Pause() => _pauseEvent.Reset();

    public void Resume() => _pauseEvent.Set();

    public async Task ProcessRegionAsync(
        string regione,
        int workerCount,
        int delayMs,
        bool headless,
        IProgress<(Ente ente, EnteStatistiche stats)> progress,
        CancellationToken cancellationToken)
    {
        var all = await _csvManager.LoadAsync(cancellationToken);
        var items = all.Where(x => x.Regione.Equals(regione, StringComparison.OrdinalIgnoreCase)).ToList();
        var stats = new EnteStatistiche { TotaleEnti = items.Count };
        _workerSemaphore = new SemaphoreSlim(Math.Max(workerCount, 1), Math.Max(workerCount, 1));

        var channel = Channel.CreateBounded<Ente>(new BoundedChannelOptions(Math.Max(workerCount * 2, 4))
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        var producer = Task.Run(async () =>
        {
            foreach (var ente in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await channel.Writer.WriteAsync(ente, cancellationToken);
            }

            channel.Writer.Complete();
        }, cancellationToken);

        var consumers = Enumerable.Range(0, Math.Max(workerCount, 1)).Select(_ => Task.Run(async () =>
        {
            await foreach (var ente in channel.Reader.ReadAllAsync(cancellationToken))
            {
                _pauseEvent.Wait(cancellationToken);
                await ProcessEnteAsync(ente, delayMs, headless, stats, progress, cancellationToken);
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(consumers.Append(producer));
    }

    private async Task ProcessEnteAsync(Ente ente, int delayMs, bool headless, EnteStatistiche stats, IProgress<(Ente ente, EnteStatistiche stats)> progress, CancellationToken cancellationToken)
    {
        await _workerSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(ente.SitoWeb))
            {
                ente.SitoWeb = await _searchEngineService.FindBestWebsiteAsync(ente, headless, cancellationToken);
                ente.Stato = string.IsNullOrWhiteSpace(ente.SitoWeb) ? StatoEnte.DA_ELABORARE : StatoEnte.SITO_TROVATO;
            }

            var result = await _webScraperService.AnalyzeAsync(ente.SitoWeb, delayMs, headless, cancellationToken);
            if (result.emails.Count > 0)
            {
                ente.Email = string.Join(';', result.emails);
                ente.Stato = StatoEnte.EMAIL_TROVATA;
            }

            if (result.pecs.Count > 0)
            {
                ente.PEC = string.Join(';', result.pecs);
            }

            if (result.phones.Count > 0)
            {
                ente.Telefono = string.Join(';', result.phones);
            }

            ente.DataUltimoControllo = DateTime.Now;
            ente.Stato = ente.Stato == StatoEnte.ERRORE ? StatoEnte.ERRORE : StatoEnte.COMPLETATO;
            await _csvManager.UpdateAsync(ente, cancellationToken);

            lock (stats)
            {
                stats.Elaborati++;
                if (!string.IsNullOrWhiteSpace(ente.SitoWeb)) stats.SitiTrovati++;
                if (!string.IsNullOrWhiteSpace(ente.Email)) stats.EmailTrovate++;
                if (!string.IsNullOrWhiteSpace(ente.PEC)) stats.PecTrovate++;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
        {
            throw;
        }
        catch (WebDriverException ex) when (ex.Message.Contains("chrome", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("chromedriver", StringComparison.OrdinalIgnoreCase))
        {
            throw;
        }
        catch (Exception ex)
        {
            ente.Stato = StatoEnte.ERRORE;
            ente.DataUltimoControllo = DateTime.Now;
            await _csvManager.UpdateAsync(ente, cancellationToken);
            await _logger.LogAsync($"Errore ente {ente.CodiceFiscale}: {ex.Message}", cancellationToken);

            lock (stats)
            {
                stats.Elaborati++;
                stats.Errori++;
            }
        }
        finally
        {
            _workerSemaphore.Release();
            progress.Report((ente, new EnteStatistiche
            {
                TotaleEnti = stats.TotaleEnti,
                Elaborati = stats.Elaborati,
                SitiTrovati = stats.SitiTrovati,
                EmailTrovate = stats.EmailTrovate,
                PecTrovate = stats.PecTrovate,
                Errori = stats.Errori
            }));
        }
    }
}
