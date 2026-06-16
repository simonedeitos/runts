using EasySearch.Helpers;
using EasySearch.Models;
using EasySearch.Services;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace EasySearch.Forms;

public partial class MainForm : Form
{
    private readonly CsvManager _csvManager;
    private readonly ExportService _exportService;
    private readonly LoggerService _logger;
    private readonly IstatComuniImporter _istatComuniImporter;
    private readonly WebScraperService _webScraperService;
    private readonly BindingList<ComuneImportato> _comuniImportati = [];
    private readonly BindingList<Ente> _rows = [];
    private readonly Queue<string> _statusLines = new();
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    private CancellationTokenSource? _cts;

    public MainForm(
        CsvManager csvManager,
        ExportService exportService,
        LoggerService logger,
        IstatComuniImporter istatComuniImporter,
        WebScraperService webScraperService)
    {
        _csvManager = csvManager;
        _exportService = exportService;
        _logger = logger;
        _istatComuniImporter = istatComuniImporter;
        _webScraperService = webScraperService;

        InitializeComponent();

        gridComuniImportati.DataSource = _comuniImportati;
        gridRisultati.DataSource = _rows;
        gridComuniImportati.CellFormatting += GridComuniImportati_CellFormatting;
        gridComuniImportati.DataBindingComplete += (_, _) => ConfigureGridColumns();
        gridRisultati.DataBindingComplete += (_, _) => ConfigureGridColumns();
        btnBrowseCsvComuni.Click += BtnBrowseCsvComuni_Click;
        btnImportaComuni.Click += async (_, _) => await ImportaComuniAsync();
        btnAvvia.Click += async (_, _) => await AvviaRicercaAsync();
        btnPausa.Click += (_, _) => PauseProcessing();
        btnRiprendi.Click += (_, _) => ResumeProcessing();
        btnFerma.Click += (_, _) => _cts?.Cancel();
        btnExportCsv.Click += async (_, _) => await ExportCsvAsync();
        btnExportExcel.Click += async (_, _) => await ExportExcelAsync();
        menuConfiguraBrightData.Click += MenuConfiguraBrightData_Click;
        Load += async (_, _) => await LoadDataAsync();

        SetProcessingControls(isProcessing: false);
        UpdateStats(new EnteStatistiche());
    }

    private Task LoadDataAsync()
    {
        txtCsvComuni.Text = RegistrySettingsManager.GetComuniCsvPath() ?? string.Empty;
        txtParolaCerca.Text = string.IsNullOrWhiteSpace(txtParolaCerca.Text) ? "Pro Loco" : txtParolaCerca.Text;
        lblFonte.Text = "Fonte dati: selezionare un CSV e importare i comuni";
        return Task.CompletedTask;
    }

    private async Task RefreshGridAsync(CancellationToken cancellationToken = default)
    {
        var all = await _csvManager.LoadAsync(cancellationToken);
        SafeUiInvoke(() =>
        {
            _rows.Clear();
            foreach (var row in all.OrderBy(x => x.Regione).ThenBy(x => x.Comune).ThenBy(x => x.Denominazione))
            {
                _rows.Add(row);
            }

            ConfigureGridColumns();

            UpdateStats(new EnteStatistiche
            {
                TotaleEnti = all.Count,
                Elaborati = all.Count(x => x.DataUltimoControllo.HasValue || x.Stato == StatoEnte.COMPLETATO || x.Stato == StatoEnte.ERRORE),
                SitiTrovati = all.Count(x => !string.IsNullOrWhiteSpace(x.SitoWeb)),
                EmailTrovate = all.Count(x => !string.IsNullOrWhiteSpace(x.Email)),
                PecTrovate = all.Count(x => !string.IsNullOrWhiteSpace(x.PEC)),
                Errori = all.Count(x => x.Stato == StatoEnte.ERRORE)
            });
        });
    }

    private async Task AvviaRicercaAsync()
    {
        try
        {
            if (_comuniImportati.Count == 0)
            {
                MessageBox.Show(
                    "Nessun comune da elaborare.\n\nClicca prima su 'Importa Comuni' per caricare i comuni dalla regione desiderata.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var searchWord = GetSearchWord();
            var outputFile = GetOutputCsvPath();
            if (string.IsNullOrWhiteSpace(outputFile))
            {
                return;
            }

            var comuni = _comuniImportati
                .Select(ci => new ComuneIstat
                {
                    Nome = ci.Comune,
                    Regione = ci.Regione,
                    Provincia = ci.Provincia,
                    SiglaProvincia = ci.Provincia
                })
                .Distinct(new ComuneIstatComparer())
                .ToList();

            if (comuni.Count == 0)
            {
                MessageBox.Show(
                    "I dati importati non contengono comuni validi per avviare la ricerca.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _pauseEvent.Set();

            SafeUiInvoke(() => _rows.Clear());

            SetProcessingControls(true);
            ResetProgress(outputFile, comuni.Count);

            var headless = !chkShowChrome.Checked;
            using var puppeteer = new PuppeteerHelper(_logger, headless);
            await puppeteer.InitializeAsync(_cts.Token);
            var comuniSearchEngine = new ComuniSearchEngine(_logger, puppeteer);
            await using var csvWriter = new CsvWriterService(outputFile);
            var stats = new EnteStatistiche { TotaleEnti = comuni.Count };
            var workerCount = (int)numThread.Value;
            var nextIndex = -1;
            var statsLock = new object();
            var seenEmails = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            var options = GetSearchOptions();

            UpdateStatus($"Avvio ricerca per {comuni.Count} comuni", 0, resetQueue: true);
            lblFonte.Text = $"Salvando risultati in: {Path.GetFileName(outputFile)}";

            var workers = Enumerable.Range(0, workerCount).Select(_ => Task.Run(async () =>
            {
                while (true)
                {
                    var index = Interlocked.Increment(ref nextIndex);
                    if (index >= comuni.Count)
                    {
                        break;
                    }

                    _pauseEvent.Wait(_cts.Token);
                    var comune = comuni[index];
                    var result = await ProcessComuneAsync(comune, searchWord, comuniSearchEngine, options, headless, seenEmails, _cts.Token);
                    await _csvManager.UpsertManyAsync(result.Rows, _cts.Token);
                    foreach (var row in result.Rows)
                    {
                        await csvWriter.WriteRowAsync(row, _cts.Token);
                    }

                    lock (statsLock)
                    {
                        stats.Elaborati++;
                        stats.SitiTrovati += result.SiteCount;
                        stats.EmailTrovate += result.EmailCount;
                        stats.PecTrovate += result.PecCount;
                        stats.Errori += result.HasError ? 1 : 0;
                    }

                    SafeUiInvoke(() =>
                    {
                        ApplyRows(result.Rows);
                        UpdateStats(new EnteStatistiche
                        {
                            TotaleEnti = stats.TotaleEnti,
                            Elaborati = stats.Elaborati,
                            SitiTrovati = stats.SitiTrovati,
                            EmailTrovate = stats.EmailTrovate,
                            PecTrovate = stats.PecTrovate,
                            Errori = stats.Errori
                        });
                        var percentage = stats.TotaleEnti == 0 ? 0 : (int)Math.Round(stats.Elaborati / (double)stats.TotaleEnti * 100);
                        lblFonte.Text = $"[{stats.Elaborati}/{stats.TotaleEnti}] {comune.Nome} ({comune.SiglaProvincia}) → {Path.GetFileName(outputFile)}";
                        UpdateStatus($"{result.StatusMessage} ({percentage}%)", stats.Elaborati);
                    });
                }
            }, _cts.Token)).ToArray();

            await Task.WhenAll(workers);
            lblFonte.Text = $"Risultati salvati in: {Path.GetFileName(outputFile)}";
            UpdateStatus("Ricerca completata", progressBar.Maximum);
            MessageBox.Show(
                $"✓ Ricerca completata con successo!\n\n" +
                $"Comuni elaborati: {stats.Elaborati}\n" +
                $"Siti trovati: {stats.SitiTrovati}\n" +
                $"Email trovate: {stats.EmailTrovate}\n\n" +
                $"Risultati salvati in:\n{outputFile}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Elaborazione annullata", progressBar.Value);
            MessageBox.Show("Elaborazione fermata.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante l'elaborazione:\n\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetProcessingControls(false);
        }
    }

    private async Task<ComuneProcessResult> ProcessComuneAsync(
        ComuneIstat comune,
        string searchWord,
        ComuniSearchEngine comuniSearchEngine,
        SearchOptions options,
        bool headless,
        ConcurrentDictionary<string, byte> seenEmails,
        CancellationToken cancellationToken)
    {
        try
        {
            UpdateComuneStato(comune, StatoElaborazione.IN_ELABORAZIONE);

            var urls = options.MultiResult
                ? await comuniSearchEngine.FindMultipleForComuneAsync(comune, searchWord, options.SearchGoogleMaps, cancellationToken)
                : [await comuniSearchEngine.FindProLocoForComuneAsync(comune, searchWord, options.SearchGoogleMaps, cancellationToken)];

            var cleanedUrls = urls.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (cleanedUrls.Count == 0)
            {
                var emptyRow = CreateEnteFromComune(comune, searchWord);
                emptyRow.Stato = StatoEnte.DA_ELABORARE;
                emptyRow.DataUltimoControllo = DateTime.Now;
                UpdateComuneStato(comune, StatoElaborazione.COMPLETATO);
                return new ComuneProcessResult([emptyRow], 0, 0, 0, false, $"{comune.Nome}: nessun sito trovato");
            }

            var rows = new List<Ente>();
            var siteCount = 0;
            var emailCount = 0;
            var pecCount = 0;

            for (var i = 0; i < cleanedUrls.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var url = cleanedUrls[i];
                var ente = CreateEnteFromComune(comune, searchWord, options.MultiResult ? i + 1 : null);
                ente.SitoWeb = url;
                ente.Stato = StatoEnte.SITO_TROVATO;
                siteCount++;

                var analysis = await _webScraperService.AnalyzeAsync(
                    url,
                    options.DelayMs,
                    headless,
                    options.SearchEmail,
                    options.SearchPec,
                    options.SearchPhone,
                    options.SearchWebsite,
                    options.SearchAddress,
                    cancellationToken);

                if (analysis.emails.Count > 0)
                {
                    var uniqueEmails = analysis.emails
                        .Where(e => seenEmails.TryAdd(e, 0))
                        .ToList();

                    var skipped = analysis.emails.Count - uniqueEmails.Count;
                    if (skipped > 0)
                    {
                        await _logger.LogAsync($"⚠ {skipped} email duplicate scartate per {comune.Nome}", cancellationToken);
                    }

                    if (uniqueEmails.Count > 0)
                    {
                        ente.Email = string.Join(';', uniqueEmails);
                        ente.Stato = StatoEnte.EMAIL_TROVATA;
                        emailCount++;
                    }
                }

                if (analysis.pecs.Count > 0)
                {
                    ente.PEC = string.Join(';', analysis.pecs);
                    pecCount++;
                }

                if (analysis.phones.Count > 0)
                {
                    ente.Telefono = string.Join(';', analysis.phones);
                }

                ente.Indirizzo = analysis.indirizzo;
                ente.DataUltimoControllo = DateTime.Now;
                ente.Stato = StatoEnte.COMPLETATO;
                rows.Add(ente);
            }

            UpdateComuneStato(comune, StatoElaborazione.COMPLETATO);
            return new ComuneProcessResult(rows, siteCount, emailCount, pecCount, false, $"{comune.Nome}: {rows.Count} risultato/i");
        }
        catch (OperationCanceledException)
        {
            UpdateComuneStato(comune, StatoElaborazione.DA_ELABORARE);
            throw;
        }
        catch (Exception ex)
        {
            UpdateComuneStato(comune, StatoElaborazione.ERRORE);
            await _logger.LogAsync($"Errore comune {comune.Nome}: {ex.Message}", cancellationToken);
            var errorRow = CreateEnteFromComune(comune, searchWord);
            errorRow.Stato = StatoEnte.ERRORE;
            errorRow.DataUltimoControllo = DateTime.Now;
            return new ComuneProcessResult([errorRow], 0, 0, 0, true, $"{comune.Nome}: errore");
        }
    }

    private async Task ExportCsvAsync()
    {
        if (_rows.Count == 0)
        {
            MessageBox.Show("Nessun dato da esportare.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = BuildDefaultExportCsvFileName()
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await _exportService.ExportCsvAsync(_rows.ToList(), dialog.FileName);
        MessageBox.Show($"CSV esportato: {dialog.FileName}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ExportExcelAsync()
    {
        if (_rows.Count == 0)
        {
            MessageBox.Show("Nessun dato da esportare.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx",
            FileName = $"easysearch_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await _exportService.ExportExcelAsync(_rows.ToList(), dialog.FileName);
        MessageBox.Show($"Excel esportato: {dialog.FileName}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void PauseProcessing()
    {
        _pauseEvent.Reset();
        UpdateStatus("Elaborazione in pausa", progressBar.Value);
    }

    private void ResumeProcessing()
    {
        _pauseEvent.Set();
        UpdateStatus("Elaborazione ripresa", progressBar.Value);
    }

    private void BtnBrowseCsvComuni_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Seleziona CSV Comuni ISTAT"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        txtCsvComuni.Text = dialog.FileName;
        RegistrySettingsManager.SaveComuniCsvPath(dialog.FileName);
        lblFonte.Text = $"Fonte dati: CSV ISTAT ({Path.GetFileName(dialog.FileName)})";
    }

    private void MenuConfiguraBrightData_Click(object? sender, EventArgs e)
    {
        using var settingsForm = new SettingsForm();
        settingsForm.ShowDialog(this);
    }

    private async Task ImportaComuniAsync()
    {
        using var importCts = new CancellationTokenSource();
        var cancellationToken = importCts.Token;

        try
        {
            var csvPath = GetCsvComuniPath();
            var searchWord = GetSearchWord();
            var selectedRegione = GetSelectedRegione();
            var isAllRegions = selectedRegione.Equals("Tutte le regioni", StringComparison.OrdinalIgnoreCase);

            SetImportControls(false);
            SafeUiInvoke(() =>
            {
                progressBar.Visible = true;
                progressBar.Minimum = 0;
                progressBar.Maximum = 100;
                progressBar.Value = 0;
            });
            UpdateStatus("Caricamento CSV comuni ISTAT...", 10, resetQueue: true);

            var (enti, regioneFiltrata) = await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var comuniCaricati = await _istatComuniImporter.LoadComuniAsync(csvPath, cancellationToken);
                SafeUiInvoke(() => UpdateStatus($"Caricati {comuniCaricati.Count} comuni dal CSV", 30));

                var comuniFiltrati = isAllRegions
                    ? comuniCaricati
                    : await _istatComuniImporter.FilterByRegioneAsync(comuniCaricati, selectedRegione, cancellationToken);
                SafeUiInvoke(() => UpdateStatus($"Filtrati {comuniFiltrati.Count} comuni per {selectedRegione}", 50));

                var entiCreati = comuniFiltrati
                    .Select(comune => CreateEnteFromComune(comune, searchWord))
                    .ToList();
                SafeUiInvoke(() => UpdateStatus($"Creati {entiCreati.Count} enti", 70));

                return (entiCreati, selectedRegione);
            }, cancellationToken);

            SafeUiInvoke(() => _rows.Clear());
            UpdateStatus("Aggiornamento griglia comuni...", 80);
            PopulateComuniImportati(enti);

            RegistrySettingsManager.SaveComuniCsvPath(csvPath);
            lblFonte.Text = $"Fonte dati: CSV ISTAT ({Path.GetFileName(csvPath)})";
            UpdateStatus($"✓ Importati {enti.Count} comuni per {regioneFiltrata}", 100);
            MessageBox.Show(
                $"✓ Importazione completata{Environment.NewLine}{Environment.NewLine}" +
                $"Regione: {regioneFiltrata}{Environment.NewLine}" +
                $"Comuni importati: {enti.Count}{Environment.NewLine}{Environment.NewLine}" +
                "Ora puoi avviare la ricerca cliccando 'Avvia Ricerca'.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (FileNotFoundException ex)
        {
            MessageBox.Show(
                $"File CSV ISTAT non trovato:{Environment.NewLine}{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}" +
                "Scarica il file da:" + Environment.NewLine +
                "https://www.istat.it/storage/codici-unita-amministrative/Elenco-comuni-italiani.csv",
                "File Mancante",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Importazione annullata", progressBar.Value);
            MessageBox.Show("Importazione annullata.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Errore durante l'importazione:{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Errore",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetImportControls(true);
        }
    }

    private SearchOptions GetSearchOptions() => new(
        chkEmail.Checked,
        chkPec.Checked,
        chkTelefono.Checked,
        chkSitoWeb.Checked,
        chkIndirizzo.Checked,
        rbMultiRisultato.Checked,
        (int)numDelay.Value,
        chkGoogleMaps.Checked);

    private string GetSearchWord() => string.IsNullOrWhiteSpace(txtParolaCerca.Text) ? "Pro Loco" : txtParolaCerca.Text.Trim();

    private string GetSelectedRegione() => cmbRegione.SelectedItem?.ToString() ?? "Tutte le regioni";

    private string BuildDefaultExportCsvFileName()
    {
        var nomeRicerca = NormalizeFileNamePart(GetSearchWord());
        var nomeComune = _rows
            .Select(x => x.Comune)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeFileNamePart)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() switch
            {
                { Count: 1 } comuni => comuni[0],
                _ => "tutti_comuni"
            };

        return $"{nomeRicerca}_{nomeComune}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
    }

    private string GetOutputCsvPath()
    {
        var searchWord = NormalizeFileNamePart(GetSearchWord());
        var regione = _comuniImportati.FirstOrDefault()?.Regione ?? "italia";
        var regioneNormalized = NormalizeFileNamePart(regione);

        using var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"{searchWord}_{regioneNormalized}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            Title = "Salva risultati ricerca",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : string.Empty;
    }

    private static string NormalizeFileNamePart(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray());

        normalized = string.Join('_', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? "ricerca" : normalized;
    }

    private string GetCsvComuniPath()
    {
        var csvPath = txtCsvComuni.Text.Trim();
        if (string.IsNullOrWhiteSpace(csvPath))
        {
            throw new InvalidOperationException("Selezionare il CSV ufficiale ISTAT dei comuni italiani.");
        }

        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("File CSV ISTAT non trovato.", csvPath);
        }

        return csvPath;
    }

    private void SetImportControls(bool enabled)
    {
        btnImportaComuni.Enabled = enabled;
        btnBrowseCsvComuni.Enabled = enabled;
        txtCsvComuni.Enabled = enabled;
        cmbRegione.Enabled = enabled;
        txtParolaCerca.Enabled = enabled;
        btnAvvia.Enabled = enabled && _comuniImportati.Count > 0;
    }

    private void SetProcessingControls(bool isProcessing)
    {
        btnAvvia.Enabled = !isProcessing && _comuniImportati.Count > 0;
        btnImportaComuni.Enabled = !isProcessing;
        btnBrowseCsvComuni.Enabled = !isProcessing;
        btnExportCsv.Enabled = !isProcessing;
        btnExportExcel.Enabled = !isProcessing;
        txtCsvComuni.Enabled = !isProcessing;
        txtParolaCerca.Enabled = !isProcessing;
        cmbRegione.Enabled = !isProcessing;
        rbRisultatoUnivoco.Enabled = !isProcessing;
        rbMultiRisultato.Enabled = !isProcessing;
        chkEmail.Enabled = !isProcessing;
        chkPec.Enabled = !isProcessing;
        chkTelefono.Enabled = !isProcessing;
        chkSitoWeb.Enabled = !isProcessing;
        chkIndirizzo.Enabled = !isProcessing;
        chkGoogleMaps.Enabled = !isProcessing;
        numThread.Enabled = !isProcessing;
        numDelay.Enabled = !isProcessing;
        chkShowChrome.Enabled = !isProcessing;
        btnPausa.Enabled = isProcessing;
        btnRiprendi.Enabled = isProcessing;
        btnFerma.Enabled = isProcessing;
    }

    private void ResetProgress(string csvPath, int totale)
    {
        SafeUiInvoke(() =>
        {
            _statusLines.Clear();
            progressBar.Minimum = 0;
            progressBar.Maximum = Math.Max(totale, 1);
            progressBar.Value = 0;
            lblFonte.Text = $"Fonte dati: CSV ISTAT ({Path.GetFileName(csvPath)})";
            lblStatusComuni.Text = "Preparazione ricerca...";
            UpdateStats(new EnteStatistiche { TotaleEnti = totale });
        });
    }

    private void UpdateStatus(string message, int progressValue, bool resetQueue = false)
    {
        if (resetQueue)
        {
            _statusLines.Clear();
        }

        _statusLines.Enqueue(message);
        while (_statusLines.Count > 3)
        {
            _statusLines.Dequeue();
        }

        progressBar.Value = Math.Clamp(progressValue, progressBar.Minimum, progressBar.Maximum);
        lblStatusComuni.Text = string.Join(Environment.NewLine, _statusLines);
    }

    private void ConfigureGridColumns()
    {
        if (gridComuniImportati.Columns.Count > 0)
        {
            if (gridComuniImportati.Columns.Contains("Regione"))
            {
                gridComuniImportati.Columns["Regione"].Width = 120;
                gridComuniImportati.Columns["Regione"].HeaderText = "Regione";
            }

            if (gridComuniImportati.Columns.Contains("Provincia"))
            {
                gridComuniImportati.Columns["Provincia"].Width = 60;
                gridComuniImportati.Columns["Provincia"].HeaderText = "Prov";
            }

            if (gridComuniImportati.Columns.Contains("Comune"))
            {
                gridComuniImportati.Columns["Comune"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                gridComuniImportati.Columns["Comune"].HeaderText = "Comune";
            }

            if (gridComuniImportati.Columns.Contains("Stato"))
            {
                gridComuniImportati.Columns["Stato"].Visible = false;
            }

            if (gridComuniImportati.Columns.Contains("Chiave"))
            {
                gridComuniImportati.Columns["Chiave"].Visible = false;
            }
        }

        if (gridRisultati.Columns.Count > 0)
        {
            if (gridRisultati.Columns.Contains("Provincia"))
            {
                gridRisultati.Columns["Provincia"].Width = 60;
            }

            if (gridRisultati.Columns.Contains("Comune"))
            {
                gridRisultati.Columns["Comune"].Width = 150;
            }

            if (gridRisultati.Columns.Contains("Categoria"))
            {
                gridRisultati.Columns["Categoria"].Width = 100;
            }

            if (gridRisultati.Columns.Contains("SitoWeb"))
            {
                gridRisultati.Columns["SitoWeb"].Width = 200;
                gridRisultati.Columns["SitoWeb"].HeaderText = "Sito web";
            }

            if (gridRisultati.Columns.Contains("Email"))
            {
                gridRisultati.Columns["Email"].Width = 180;
            }

            if (gridRisultati.Columns.Contains("PEC"))
            {
                gridRisultati.Columns["PEC"].Width = 180;
            }

            if (gridRisultati.Columns.Contains("Telefono"))
            {
                gridRisultati.Columns["Telefono"].Width = 120;
            }

            if (gridRisultati.Columns.Contains("Indirizzo"))
            {
                gridRisultati.Columns["Indirizzo"].Width = 200;
            }

            if (gridRisultati.Columns.Contains("DataUltimoControllo"))
            {
                gridRisultati.Columns["DataUltimoControllo"].Width = 130;
                gridRisultati.Columns["DataUltimoControllo"].HeaderText = "Data ultima modifica";
            }

            if (gridRisultati.Columns.Contains("Regione"))
            {
                gridRisultati.Columns["Regione"].Visible = false;
            }

            if (gridRisultati.Columns.Contains("CodiceFiscale"))
            {
                gridRisultati.Columns["CodiceFiscale"].Visible = false;
            }

            if (gridRisultati.Columns.Contains("Denominazione"))
            {
                gridRisultati.Columns["Denominazione"].Visible = false;
            }

            if (gridRisultati.Columns.Contains("Stato"))
            {
                gridRisultati.Columns["Stato"].Visible = false;
            }
        }
    }

    private void UpdateStats(EnteStatistiche stats)
    {
        progressBar.Maximum = Math.Max(stats.TotaleEnti, 1);
        progressBar.Value = Math.Min(stats.Elaborati, progressBar.Maximum);
        lblTotale.Text = $"Totale: {stats.TotaleEnti}";
        lblElaborati.Text = $"Elaborati: {stats.Elaborati}";
        lblSiti.Text = $"Siti: {stats.SitiTrovati}";
        lblEmail.Text = $"Email: {stats.EmailTrovate}";
        lblPec.Text = $"PEC: {stats.PecTrovate}";
        lblErrori.Text = $"Errori: {stats.Errori}";
    }

    private void ApplyRows(IEnumerable<Ente> rows)
    {
        foreach (var row in rows)
        {
            var key = BuildEntityKey(row);
            var index = _rows.ToList().FindIndex(x => BuildEntityKey(x).Equals(key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _rows[index] = row;
            }
            else
            {
                _rows.Add(row);
            }
        }
    }

    private static string BuildEntityKey(Ente ente)
    {
        if (!string.IsNullOrWhiteSpace(ente.CodiceFiscale))
        {
            return $"CF:{ente.CodiceFiscale.Trim().ToUpperInvariant()}";
        }

        return string.Join('|',
            "ALT",
            ente.Regione.Trim().ToUpperInvariant(),
            ente.Comune.Trim().ToUpperInvariant(),
            ente.Denominazione.Trim().ToUpperInvariant(),
            ente.SitoWeb.Trim().ToUpperInvariant());
    }

    private static Ente CreateEnteFromComune(ComuneIstat comune, string searchWord, int? resultIndex = null)
    {
        var suffix = resultIndex.HasValue ? $" #{resultIndex.Value}" : string.Empty;
        return new Ente
        {
            Regione = comune.Regione,
            Provincia = string.IsNullOrWhiteSpace(comune.SiglaProvincia) ? comune.Provincia : comune.SiglaProvincia,
            Comune = comune.Nome,
            Denominazione = $"{searchWord} {comune.Nome}{suffix}".Trim(),
            Categoria = searchWord,
            Stato = StatoEnte.DA_ELABORARE
        };
    }

    private void PopulateComuniImportati(IEnumerable<Ente> enti)
    {
        SafeUiInvoke(() =>
        {
            _comuniImportati.Clear();
            foreach (var ente in enti
                .OrderBy(x => x.Regione)
                .ThenBy(x => x.Provincia)
                .ThenBy(x => x.Comune)
                .GroupBy(x => $"{x.Regione}|{x.Provincia}|{x.Comune}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()))
            {
                _comuniImportati.Add(new ComuneImportato
                {
                    Regione = ente.Regione,
                    Provincia = ente.Provincia,
                    Comune = ente.Comune,
                    Stato = StatoElaborazione.DA_ELABORARE
                });
            }

            ConfigureGridColumns();
            gridComuniImportati.Refresh();
        });
    }

    private void UpdateComuneStato(ComuneIstat comune, StatoElaborazione nuovoStato)
    {
        SafeUiInvoke(() =>
        {
            var chiave = $"{comune.Regione}|{comune.SiglaProvincia}|{comune.Nome}";
            var index = _comuniImportati.ToList().FindIndex(x => x.Chiave.Equals(chiave, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            _comuniImportati[index].Stato = nuovoStato;
            if (index < gridComuniImportati.Rows.Count)
            {
                gridComuniImportati.InvalidateRow(index);
            }
        });
    }

    private void GridComuniImportati_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _comuniImportati.Count)
        {
            return;
        }

        var row = gridComuniImportati.Rows[e.RowIndex];
        var comune = _comuniImportati[e.RowIndex];
        switch (comune.Stato)
        {
            case StatoElaborazione.DA_ELABORARE:
                row.DefaultCellStyle.BackColor = Color.White;
                row.DefaultCellStyle.ForeColor = Color.Black;
                row.DefaultCellStyle.SelectionBackColor = Color.White;
                row.DefaultCellStyle.SelectionForeColor = Color.Black;
                break;
            case StatoElaborazione.IN_ELABORAZIONE:
                row.DefaultCellStyle.BackColor = Color.Yellow;
                row.DefaultCellStyle.ForeColor = Color.Black;
                row.DefaultCellStyle.SelectionBackColor = Color.Yellow;
                row.DefaultCellStyle.SelectionForeColor = Color.Black;
                break;
            case StatoElaborazione.COMPLETATO:
                row.DefaultCellStyle.BackColor = Color.LightGreen;
                row.DefaultCellStyle.ForeColor = Color.Black;
                row.DefaultCellStyle.SelectionBackColor = Color.LightGreen;
                row.DefaultCellStyle.SelectionForeColor = Color.Black;
                break;
            case StatoElaborazione.ERRORE:
                row.DefaultCellStyle.BackColor = Color.LightCoral;
                row.DefaultCellStyle.ForeColor = Color.White;
                row.DefaultCellStyle.SelectionBackColor = Color.LightCoral;
                row.DefaultCellStyle.SelectionForeColor = Color.White;
                break;
        }
    }

    private sealed class ComuneIstatComparer : IEqualityComparer<ComuneIstat>
    {
        public bool Equals(ComuneIstat? x, ComuneIstat? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.Nome, y.Nome, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Regione, y.Regione, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(ComuneIstat obj)
        {
            return HashCode.Combine(
                obj.Nome.ToUpperInvariant(),
                obj.Regione.ToUpperInvariant());
        }
    }

    private void SafeUiInvoke(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }

    private sealed record SearchOptions(bool SearchEmail, bool SearchPec, bool SearchPhone, bool SearchWebsite, bool SearchAddress, bool MultiResult, int DelayMs, bool SearchGoogleMaps);

    private sealed record ComuneProcessResult(
        List<Ente> Rows,
        int SiteCount,
        int EmailCount,
        int PecCount,
        bool HasError,
        string StatusMessage);

    private sealed class ComuneImportato
    {
        public string Regione { get; set; } = string.Empty;
        public string Provincia { get; set; } = string.Empty;
        public string Comune { get; set; } = string.Empty;
        public StatoElaborazione Stato { get; set; } = StatoElaborazione.DA_ELABORARE;

        public string Chiave => $"{Regione}|{Provincia}|{Comune}";
    }

    private enum StatoElaborazione
    {
        DA_ELABORARE,
        IN_ELABORAZIONE,
        COMPLETATO,
        ERRORE
    }
}
