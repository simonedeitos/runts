using runts.Models;
using runts.Services;
using runts.Helpers;
using System.ComponentModel;

namespace runts.Forms;

/// <summary>
/// Form principale con gestione import, ricerca contatti, export e statistiche in tempo reale.
/// </summary>
public partial class MainForm : Form
{
    private const string TutteLeRegioniLabel = "Tutte le regioni";

    private static readonly string[] RegioniPredefinite =
    [
        "Abruzzo", "Basilicata", "Calabria", "Campania", "Emilia-Romagna", "Friuli-Venezia Giulia", "Lazio", "Liguria", "Lombardia", "Marche", "Molise", "Piemonte", "Puglia", "Sardegna", "Sicilia", "Toscana", "Trentino-Alto Adige", "Umbria", "Valle d'Aosta", "Veneto"
    ];

    private readonly RuntsImporter _importer;
    private readonly CsvManager _csvManager;
    private readonly ContactFinderService _contactFinder;
    private readonly ExportService _exportService;
    private readonly LoggerService _logger;
    private readonly IstatComuniImporter _istatComuniImporter;
    private readonly WebScraperService _webScraperService;
    private readonly BindingList<Ente> _rows = [];
    private List<string> _regioniDisponibili = [];
    private CancellationTokenSource? _cts;
    private readonly Queue<string> _comuniStatusLines = new();

    public MainForm(
        RuntsImporter importer,
        CsvManager csvManager,
        ContactFinderService contactFinder,
        ExportService exportService,
        LoggerService logger,
        IstatComuniImporter istatComuniImporter,
        WebScraperService webScraperService)
    {
        _importer = importer;
        _csvManager = csvManager;
        _contactFinder = contactFinder;
        _exportService = exportService;
        _logger = logger;
        _istatComuniImporter = istatComuniImporter;
        _webScraperService = webScraperService;

        InitializeComponent();
        InitializeSettingsMenu();

        gridEnti.DataSource = _rows;
        btnImporta.Click += async (_, _) => await ImportRegioneAsync();
        btnAvvia.Click += async (_, _) => await AvviaRicercaAsync();
        btnPausa.Click += (_, _) => _contactFinder.Pause();
        btnRiprendi.Click += (_, _) => _contactFinder.Resume();
        btnFerma.Click += (_, _) => _cts?.Cancel();
        btnExportCsv.Click += async (_, _) => await ExportCsvAsync();
        btnExportExcel.Click += async (_, _) => await ExportExcelAsync();
        btnBrowseCsvComuni.Click += BtnBrowseCsvComuni_Click;
        cmbModalita.SelectedIndexChanged += (_, _) =>
        {
            RefreshRegionOptions();
            UpdateComuneModeUi();
        };
        SetProcessingControls(isProcessing: false);

        Load += async (_, _) => await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        cmbModalita.Items.Clear();
        chkShowChrome.Visible = true;
        cmbModalita.Items.Add("RUNTS - Enti Terzo Settore Registrati");
        cmbModalita.Items.Add("Pro Loco - Albi Regionali Ufficiali (PDF)");
        cmbModalita.Items.Add("Pro Loco - Ricerca per Comune (ISTAT)");
        cmbModalita.SelectedIndex = cmbModalita.SelectedIndex < 0 ? 0 : cmbModalita.SelectedIndex;

        var all = await _csvManager.LoadAsync();
        _regioniDisponibili = all.Select(x => x.Regione)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Concat(RegioniPredefinite)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        RefreshRegionOptions();
        UpdateComuneModeUi();

        await RefreshGridAsync();
    }

    private async Task ImportRegioneAsync()
    {
        var controlsDisabled = false;
        try
        {
            var importMode = GetImportMode();
            var regione = GetRegione();
            var csvPath = importMode == ImportMode.ProLocoPerComune ? GetCsvComuniPath() : string.Empty;

            btnImporta.Enabled = false;
            cmbRegione.Enabled = false;
            cmbModalita.Enabled = false;
            txtCsvComuni.Enabled = false;
            btnBrowseCsvComuni.Enabled = false;
            controlsDisabled = true;

            lblFonte.Text = "Fonte dati: avvio importazione...";

            var imported = await Task.Run(async () =>
            {
                if (importMode == ImportMode.ProLocoPerComune)
                {
                    SafeUiInvoke(() => lblFonte.Text = "Fonte dati: 📂 caricamento CSV ISTAT in corso...");

                    var comuni = await _istatComuniImporter.LoadComuniAsync(csvPath, default);
                    SafeUiInvoke(() => lblFonte.Text = $"Fonte dati: ✓ caricati {comuni.Count} comuni da CSV");
                    await Task.Delay(800);

                    if (!IsTutteLeRegioni(regione))
                    {
                        SafeUiInvoke(() => lblFonte.Text = $"Fonte dati: 🔍 filtro comuni per regione {regione}...");
                        comuni = _istatComuniImporter.FilterByRegione(comuni, regione);
                        SafeUiInvoke(() => lblFonte.Text = $"Fonte dati: ✓ filtrati {comuni.Count} comuni per {regione}");
                        await Task.Delay(800);
                    }
                    else
                    {
                        SafeUiInvoke(() => lblFonte.Text = $"Fonte dati: ⚠ elaborazione di TUTTI i {comuni.Count} comuni (nessun filtro)");
                        await Task.Delay(800);
                    }

                    SafeUiInvoke(() => lblFonte.Text = "Fonte dati: 💾 salvataggio comuni in database locale...");
                    var enti = comuni.Select(CreateEnteFromComune).ToList();
                    await _csvManager.CreateBackupAsync("import_istatcomuni", default);
                    await _csvManager.UpsertManyAsync(enti, default);

                    SafeUiInvoke(() => lblFonte.Text = $"Fonte dati: ✓ salvati {enti.Count} comuni in database");
                    await Task.Delay(500);
                    return enti.Count;
                }

                var progress = new Progress<string>(message => SafeUiInvoke(() => lblFonte.Text = $"Fonte dati: {message}"));
                return await _importer.ImportRegioneAsync(regione, importMode, progress);
            });

            await RefreshGridAsync();
            var modeLabel = GetModeLabel(importMode);

            var message = importMode == ImportMode.ProLocoPerComune
                ? $"✓ Importati {imported} comuni ({modeLabel}) per {regione}\n\n" +
                  "I comuni sono ora visibili nella tabella sottostante.\n\n" +
                  "📌 PROSSIMO STEP:\n" +
                  "Clicca [Avvia Ricerca Comuni] per cercare le Pro Loco su web\n" +
                  "(Google, DuckDuckGo, Bing) ed estrarre email/contatti."
                : $"Importati {imported} record ({modeLabel}) per {regione}.";

            MessageBox.Show(message, "Import Completato", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Attenzione", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (FileNotFoundException ex)
        {
            MessageBox.Show(
                $"File CSV ISTAT non trovato:\n\n{ex.Message}\n\n" +
                "Scarica il file da:\nhttps://www.istat.it/storage/codici-unita-amministrative/Elenco-comuni-italiani.csv",
                "File Mancante",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Errore durante l'importazione:\n\n{ex.Message}",
                "Errore",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (controlsDisabled)
            {
                btnImporta.Enabled = true;
                cmbRegione.Enabled = true;
                cmbModalita.Enabled = true;
                txtCsvComuni.Enabled = true;
                btnBrowseCsvComuni.Enabled = true;
            }
        }
    }

    private async Task AvviaRicercaAsync()
    {
        if (GetImportMode() == ImportMode.ProLocoPerComune)
        {
            await AvviaRicercaComuniAsync();
            return;
        }

        var regione = GetRegione();
        var outputFile = GetOutputCsvPath();
        if (string.IsNullOrWhiteSpace(outputFile))
        {
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            var headless = !chkShowChrome.Checked;
            SetProcessingControls(isProcessing: true);
            var progress = new Progress<(Ente ente, EnteStatistiche stats)>(x =>
            {
                if (InvokeRequired)
                {
                    BeginInvoke(() => ApplyProgress(x.ente, x.stats));
                }
                else
                {
                    ApplyProgress(x.ente, x.stats);
                }
            });

            await Task.Run(async () =>
            {
                await _contactFinder.ProcessRegionAsync(regione, (int)numThread.Value, (int)numDelay.Value, headless, outputFile, progress, _cts.Token);
            }, _cts.Token);

            await RefreshGridAsync();
            MessageBox.Show(
                $"Ricerca completata.\n\nRisultati salvati in:\n{outputFile}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show("Elaborazione fermata.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                    $"Errore durante l'elaborazione:\n\n{ex.Message}",
                    "Errore",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
        }
        finally
        {
            SetProcessingControls(isProcessing: false);
        }
    }

    private async Task AvviaRicercaComuniAsync()
    {
        var csvPath = GetCsvComuniPath();
        var regione = GetRegione();
        var outputFile = GetOutputCsvPath();
        if (string.IsNullOrWhiteSpace(outputFile))
        {
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            var headless = !chkShowChrome.Checked;
            SetProcessingControls(isProcessing: true);
            ResetComuniProgress();
            await Task.Run(
                () => ProcessComuniAsync(csvPath, regione, outputFile, (int)numDelay.Value, headless, _cts.Token),
                _cts.Token);
            await RefreshGridAsync();

            MessageBox.Show(
                $"Ricerca completata.\n\nRisultati salvati in:\n{outputFile}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show("Elaborazione fermata.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Errore durante l'elaborazione:\n\n{ex.Message}",
                "Errore",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetProcessingControls(isProcessing: false);
        }
    }

    private void InitializeSettingsMenu()
    {
        var menuStrip = new MenuStrip
        {
            Dock = DockStyle.None,
            Location = new Point(20, 0),
            AutoSize = true
        };

        var menuSettings = new ToolStripMenuItem("Impostazioni");
        var menuBrightData = new ToolStripMenuItem("Configura Bright Data API");
        menuBrightData.Click += MenuBrightData_Click;

        menuSettings.DropDownItems.Add(menuBrightData);
        menuStrip.Items.Add(menuSettings);

        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;
    }

    private void MenuBrightData_Click(object? sender, EventArgs e)
    {
        using var settingsForm = new SettingsForm();
        settingsForm.ShowDialog(this);
    }

    private async Task ExportCsvAsync()
    {
        var path = await _exportService.ExportRegionCsvAsync(GetRegione());
        MessageBox.Show($"CSV esportato: {path}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ExportExcelAsync()
    {
        var path = await _exportService.ExportRegionExcelAsync(GetRegione());
        MessageBox.Show($"Excel esportato: {path}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task RefreshGridAsync()
    {
        var regione = GetRegione();
        var all = await _csvManager.LoadAsync();
        var filtered = IsTutteLeRegioni(regione)
            ? all
            : all.Where(x => x.Regione.Equals(regione, StringComparison.OrdinalIgnoreCase)).ToList();

        _rows.Clear();
        foreach (var row in filtered)
        {
            _rows.Add(row);
        }

        ApplyProgress(null, new EnteStatistiche
        {
            TotaleEnti = filtered.Count,
            Elaborati = filtered.Count(x => x.Stato is StatoEnte.COMPLETATO or StatoEnte.ERRORE),
            SitiTrovati = filtered.Count(x => !string.IsNullOrWhiteSpace(x.SitoWeb)),
            EmailTrovate = filtered.Count(x => !string.IsNullOrWhiteSpace(x.Email)),
            PecTrovate = filtered.Count(x => !string.IsNullOrWhiteSpace(x.PEC)),
            Errori = filtered.Count(x => x.Stato == StatoEnte.ERRORE)
        });
    }

    private void ApplyProgress(Ente? ente, EnteStatistiche stats)
    {
        ApplyProgress(ente, stats, updateMainProgressBar: true);
    }

    private void ApplyProgress(Ente? ente, EnteStatistiche stats, bool updateMainProgressBar)
    {
        if (ente is not null)
        {
            var key = BuildEntityKey(ente);
            var index = _rows.ToList().FindIndex(x => BuildEntityKey(x).Equals(key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _rows[index] = ente;
            }
        }

        if (updateMainProgressBar)
        {
            progressBar.Maximum = Math.Max(stats.TotaleEnti, 1);
            progressBar.Value = Math.Min(stats.Elaborati, progressBar.Maximum);
        }

        lblTotale.Text = $"Totale Enti: {stats.TotaleEnti}";
        lblElaborati.Text = $"Elaborati: {stats.Elaborati} ({stats.PercentualeCompletamento}%)";
        lblSiti.Text = $"Siti Trovati: {stats.SitiTrovati}";
        lblEmail.Text = $"Email Trovate: {stats.EmailTrovate}";
        lblPec.Text = $"PEC Trovate: {stats.PecTrovate}";
        lblErrori.Text = $"Errori: {stats.Errori}";
    }

    private string GetRegione()
    {
        if (cmbRegione.SelectedItem is not string regione || string.IsNullOrWhiteSpace(regione))
        {
            throw new InvalidOperationException("Selezionare una regione prima di continuare.");
        }

        return regione;
    }

    private ImportMode GetImportMode()
    {
        return cmbModalita.SelectedIndex switch
        {
            1 => ImportMode.ProLocoAlbiPdf,
            2 => ImportMode.ProLocoPerComune,
            _ => ImportMode.Runts
        };
    }

    private void RefreshRegionOptions()
    {
        var regioneSelezionata = cmbRegione.SelectedItem as string;
        var regioni = GetImportMode() == ImportMode.ProLocoAlbiPdf
            ? _importer.GetSupportedPdfRegions().OrderBy(x => x).ToList()
            : _regioniDisponibili;

        if (GetImportMode() == ImportMode.ProLocoPerComune)
        {
            regioni = [TutteLeRegioniLabel, .. RegioniPredefinite];
        }

        cmbRegione.Items.Clear();
        cmbRegione.Items.AddRange(regioni.Cast<object>().ToArray());

        if (!string.IsNullOrWhiteSpace(regioneSelezionata))
        {
            var index = regioni.FindIndex(x => x.Equals(regioneSelezionata, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                cmbRegione.SelectedIndex = index;
                return;
            }
        }

        if (cmbRegione.Items.Count > 0)
        {
            cmbRegione.SelectedIndex = 0;
        }
    }

    private static string GetModeLabel(ImportMode mode) => mode switch
    {
        ImportMode.Runts => "RUNTS",
        ImportMode.ProLocoAlbiPdf => "Pro Loco da PDF",
        ImportMode.ProLocoPerComune => "Pro Loco per Comune",
        _ => "Import"
    };

    private static string BuildEntityKey(Ente ente)
    {
        if (!string.IsNullOrWhiteSpace(ente.CodiceFiscale))
        {
            return $"CF:{ente.CodiceFiscale.Trim().ToUpperInvariant()}";
        }

        return $"ALT:{ente.Regione.Trim().ToUpperInvariant()}|{ente.Comune.Trim().ToUpperInvariant()}|{ente.Categoria.Trim().ToUpperInvariant()}";
    }

    private string GetOutputCsvPath()
    {
        var regione = cmbRegione.SelectedItem as string ?? "Italia";
        var isComuniMode = GetImportMode() == ImportMode.ProLocoPerComune;
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = isComuniMode
                ? $"proloco_comuni_{SanitizeFilePart(regione)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                : $"runts_contatti_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            Title = isComuniMode ? "Salva risultati ricerca comuni" : "Salva risultati ricerca contatti",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : string.Empty;
    }

    private void SetProcessingControls(bool isProcessing)
    {
        var isComuniMode = GetImportMode() == ImportMode.ProLocoPerComune;

        btnAvvia.Enabled = !isProcessing;
        btnImporta.Enabled = !isProcessing;
        btnExportCsv.Enabled = !isProcessing;
        btnExportExcel.Enabled = !isProcessing;
        cmbRegione.Enabled = !isProcessing;
        cmbModalita.Enabled = !isProcessing;
        chkShowChrome.Enabled = !isProcessing;
        numThread.Enabled = !isProcessing;
        numDelay.Enabled = !isProcessing;
        txtCsvComuni.Enabled = !isProcessing;
        btnBrowseCsvComuni.Enabled = !isProcessing;
        btnFerma.Enabled = isProcessing;
        progressBar.Visible = isComuniMode && isProcessing;
        lblStatusComuni.Visible = isComuniMode && isProcessing;

        if (!isProcessing && isComuniMode)
        {
            lblStatusComuni.Text = "Pronto per importazione comuni ISTAT";
            _comuniStatusLines.Clear();
        }
    }

    private async Task ProcessComuniAsync(string csvPath, string regione, string outputFile, int delayMs, bool headless, CancellationToken cancellationToken)
    {
        UpdateComuniProgress("📂 Caricamento CSV comuni ISTAT in corso...", 10, append: false);
        var comuni = await _istatComuniImporter.LoadComuniAsync(csvPath, cancellationToken);
        UpdateComuniProgress($"✓ Caricati {comuni.Count} comuni da CSV", 30);
        await Task.Delay(500, cancellationToken);

        if (!IsTutteLeRegioni(regione))
        {
            UpdateComuniProgress($"🔍 Filtro comuni per regione: {regione}...", 40);
            comuni = _istatComuniImporter.FilterByRegione(comuni, regione);
            UpdateComuniProgress($"✓ Filtrati {comuni.Count} comuni per {regione}", 50);
            await Task.Delay(500, cancellationToken);
        }
        else
        {
            UpdateComuniProgress($"⚠ Elaborazione di TUTTI i {comuni.Count} comuni (nessun filtro)", 50);
        }

        var enti = comuni.Select(CreateEnteFromComune).ToList();
        await _csvManager.CreateBackupAsync("ricercacomuni", cancellationToken);
        await _csvManager.UpsertManyAsync(enti, cancellationToken);

        SafeUiInvoke(() =>
        {
            _rows.Clear();
            foreach (var ente in enti)
            {
                _rows.Add(ente);
            }

            lblFonte.Text = $"Fonte dati: CSV ISTAT ({Path.GetFileName(csvPath)})";
            ApplyProgress(null, new EnteStatistiche { TotaleEnti = enti.Count });
        });

        UpdateComuniProgress("🌐 Avvio browser Puppeteer...", 60);
        using var puppeteer = new PuppeteerHelper(_logger, headless);
        await puppeteer.InitializeAsync(cancellationToken);
        UpdateComuniProgress("✓ Browser Puppeteer pronto", 70);
        await Task.Delay(500, cancellationToken);

        UpdateComuniProgress($"🔍 Ricerca Pro Loco per {comuni.Count} comuni in corso...", 75);
        var comuniSearchEngine = new ComuniSearchEngine(_logger, puppeteer);
        await using var csvWriter = new CsvWriterService(outputFile);

        var stats = new EnteStatistiche { TotaleEnti = enti.Count };
        for (var index = 0; index < comuni.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var comune = comuni[index];
            var ente = enti[index];

            try
            {
                SafeUiInvoke(() => lblFonte.Text = $"Fonte dati: [{index + 1}/{comuni.Count}] {comune.Nome} ({comune.SiglaProvincia})");
                ente.SitoWeb = await comuniSearchEngine.FindProLocoForComuneAsync(comune, cancellationToken);
                ente.Stato = string.IsNullOrWhiteSpace(ente.SitoWeb) ? StatoEnte.DA_ELABORARE : StatoEnte.SITO_TROVATO;

                if (!string.IsNullOrWhiteSpace(ente.SitoWeb))
                {
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
                }

                ente.DataUltimoControllo = DateTime.Now;
                ente.Stato = ente.Stato == StatoEnte.ERRORE ? StatoEnte.ERRORE : StatoEnte.COMPLETATO;
                await _csvManager.UpdateAsync(ente, cancellationToken);
                await csvWriter.WriteRowAsync(ente, cancellationToken);

                stats.Elaborati++;
                if (!string.IsNullOrWhiteSpace(ente.SitoWeb)) stats.SitiTrovati++;
                if (!string.IsNullOrWhiteSpace(ente.Email)) stats.EmailTrovate++;
                if (!string.IsNullOrWhiteSpace(ente.PEC)) stats.PecTrovate++;

                var progressPercentage = comuni.Count == 0
                    ? 100
                    : 75 + (int)(((index + 1) / (double)comuni.Count) * 25);
                UpdateComuniProgress(
                    $"[{index + 1}/{comuni.Count}] {comune.Nome} ({comune.SiglaProvincia}) | Trovate: {stats.SitiTrovati} | Email: {stats.EmailTrovate}",
                    progressPercentage);
            }
            catch (OperationCanceledException)
            {
                var cancelledPercentage = comuni.Count == 0
                    ? 0
                    : 75 + (int)(((stats.Elaborati) / (double)comuni.Count) * 25);
                UpdateComuniProgress("⚠ Elaborazione annullata dall'utente", cancelledPercentage);
                throw;
            }
            catch (Exception ex)
            {
                ente.Stato = StatoEnte.ERRORE;
                ente.DataUltimoControllo = DateTime.Now;
                await _csvManager.UpdateAsync(ente, cancellationToken);
                await csvWriter.WriteRowAsync(ente, cancellationToken);
                await _logger.LogAsync($"Errore comune {comune.Nome}: {ex.Message}", cancellationToken);

                stats.Elaborati++;
                stats.Errori++;
            }
            finally
            {
                SafeUiInvoke(() => ApplyProgress(ente, new EnteStatistiche
                {
                    TotaleEnti = stats.TotaleEnti,
                    Elaborati = stats.Elaborati,
                    SitiTrovati = stats.SitiTrovati,
                    EmailTrovate = stats.EmailTrovate,
                    PecTrovate = stats.PecTrovate,
                    Errori = stats.Errori
                }, updateMainProgressBar: false));
            }

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        UpdateComuniProgress($"✓ COMPLETATO: {stats.SitiTrovati}/{stats.Elaborati} Pro Loco trovate | {stats.EmailTrovate} email", 100);
    }

    private Ente CreateEnteFromComune(ComuneIstat comune) => new()
    {
        Regione = comune.Regione,
        Provincia = string.IsNullOrWhiteSpace(comune.SiglaProvincia) ? comune.Provincia : comune.SiglaProvincia,
        Comune = comune.Nome,
        Denominazione = $"Pro Loco {comune.Nome}",
        CodiceFiscale = string.Empty,
        Categoria = "Pro Loco",
        Stato = StatoEnte.DA_ELABORARE
    };

    private void BtnBrowseCsvComuni_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Seleziona CSV Comuni ISTAT",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            txtCsvComuni.Text = dialog.FileName;
        }
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

    private void UpdateComuneModeUi()
    {
        var isComuniMode = GetImportMode() == ImportMode.ProLocoPerComune;
        lblCsvComuni.Visible = isComuniMode;
        txtCsvComuni.Visible = isComuniMode;
        btnBrowseCsvComuni.Visible = isComuniMode;
        lblStatusComuni.Visible = isComuniMode && btnFerma.Enabled;
        btnAvvia.Text = isComuniMode ? "Avvia Ricerca Comuni" : "Avvia Ricerca";
        btnImporta.Text = isComuniMode ? "Importa Comuni CSV" : "Importa Regione";
    }

    private void ResetComuniProgress()
    {
        SafeUiInvoke(() =>
        {
            _comuniStatusLines.Clear();
            lblStatusComuni.Text = "Avvio elaborazione...";
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Value = 0;
            progressBar.Visible = true;
            lblStatusComuni.Visible = true;
        });
    }

    private void UpdateComuniProgress(string status, int percentage, bool append = true)
    {
        SafeUiInvoke(() =>
        {
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Value = Math.Clamp(percentage, 0, 100);

            if (!append)
            {
                _comuniStatusLines.Clear();
            }

            _comuniStatusLines.Enqueue(status);
            while (_comuniStatusLines.Count > 3)
            {
                _comuniStatusLines.Dequeue();
            }

            lblStatusComuni.Text = string.Join(Environment.NewLine, _comuniStatusLines);
            lblFonte.Text = $"Fonte dati: {status}";
        });
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

    private static bool IsTutteLeRegioni(string value) =>
        value.Equals(TutteLeRegioniLabel, StringComparison.OrdinalIgnoreCase);

    private static string SanitizeFilePart(string value) =>
        string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')).Trim('_').ToLowerInvariant();
}
