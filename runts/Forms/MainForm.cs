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
    private static readonly string[] RegioniPredefinite =
    [
        "Abruzzo", "Basilicata", "Calabria", "Campania", "Emilia-Romagna", "Friuli-Venezia Giulia", "Lazio", "Liguria", "Lombardia", "Marche", "Molise", "Piemonte", "Puglia", "Sardegna", "Sicilia", "Toscana", "Trentino-Alto Adige", "Umbria", "Valle d'Aosta", "Veneto"
    ];

    private readonly RuntsImporter _importer;
    private readonly CsvManager _csvManager;
    private readonly ContactFinderService _contactFinder;
    private readonly ExportService _exportService;
    private readonly BindingList<Ente> _rows = [];
    private List<string> _regioniDisponibili = [];
    private CancellationTokenSource? _cts;

    public MainForm(RuntsImporter importer, CsvManager csvManager, ContactFinderService contactFinder, ExportService exportService)
    {
        _importer = importer;
        _csvManager = csvManager;
        _contactFinder = contactFinder;
        _exportService = exportService;

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
        cmbModalita.SelectedIndexChanged += (_, _) => RefreshRegionOptions();
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

        await RefreshGridAsync();
    }

    private async Task ImportRegioneAsync()
    {
        var regione = GetRegione();
        var importMode = GetImportMode();
        var progress = new Progress<string>(message => lblFonte.Text = $"Fonte dati: {message}");

        lblFonte.Text = "Fonte dati: avvio importazione...";
        var imported = await _importer.ImportRegioneAsync(regione, importMode, progress);
        await RefreshGridAsync();
        var modeLabel = GetModeLabel(importMode);
        MessageBox.Show($"Importati {imported} record ({modeLabel}) per {regione}.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task AvviaRicercaAsync()
    {
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
        var filtered = all.Where(x => x.Regione.Equals(regione, StringComparison.OrdinalIgnoreCase)).ToList();

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
        if (ente is not null)
        {
            var key = BuildEntityKey(ente);
            var index = _rows.ToList().FindIndex(x => BuildEntityKey(x).Equals(key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _rows[index] = ente;
            }
        }

        progressBar.Maximum = Math.Max(stats.TotaleEnti, 1);
        progressBar.Value = Math.Min(stats.Elaborati, progressBar.Maximum);

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
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"runts_contatti_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            Title = "Salva risultati ricerca contatti",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : string.Empty;
    }

    private void SetProcessingControls(bool isProcessing)
    {
        btnAvvia.Enabled = !isProcessing;
        btnImporta.Enabled = !isProcessing;
        btnExportCsv.Enabled = !isProcessing;
        btnExportExcel.Enabled = !isProcessing;
        cmbRegione.Enabled = !isProcessing;
        cmbModalita.Enabled = !isProcessing;
        chkShowChrome.Enabled = !isProcessing;
        numThread.Enabled = !isProcessing;
        numDelay.Enabled = !isProcessing;
        btnFerma.Enabled = isProcessing;
    }
}
