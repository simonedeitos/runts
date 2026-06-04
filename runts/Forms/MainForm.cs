using runts.Models;
using runts.Services;
using System.ComponentModel;

namespace runts.Forms;

/// <summary>
/// Form principale con gestione import, ricerca contatti, export e statistiche in tempo reale.
/// </summary>
public partial class MainForm : Form
{
    private readonly RuntsImporter _importer;
    private readonly CsvManager _csvManager;
    private readonly ContactFinderService _contactFinder;
    private readonly ExportService _exportService;
    private readonly BindingList<Ente> _rows = [];
    private CancellationTokenSource? _cts;

    public MainForm(RuntsImporter importer, CsvManager csvManager, ContactFinderService contactFinder, ExportService exportService)
    {
        _importer = importer;
        _csvManager = csvManager;
        _contactFinder = contactFinder;
        _exportService = exportService;

        InitializeComponent();

        gridEnti.DataSource = _rows;
        btnImporta.Click += async (_, _) => await ImportRegioneAsync();
        btnAvvia.Click += async (_, _) => await AvviaRicercaAsync();
        btnPausa.Click += (_, _) => _contactFinder.Pause();
        btnRiprendi.Click += (_, _) => _contactFinder.Resume();
        btnFerma.Click += (_, _) => _cts?.Cancel();
        btnExportCsv.Click += async (_, _) => await ExportCsvAsync();
        btnExportExcel.Click += async (_, _) => await ExportExcelAsync();

        Load += async (_, _) => await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var all = await _csvManager.LoadAsync();
        var regioni = all.Select(x => x.Regione).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        if (regioni.Count == 0)
        {
            regioni =
            [
                "Abruzzo", "Basilicata", "Calabria", "Campania", "Emilia-Romagna", "Friuli-Venezia Giulia", "Lazio", "Liguria", "Lombardia", "Marche", "Molise", "Piemonte", "Puglia", "Sardegna", "Sicilia", "Toscana", "Trentino-Alto Adige", "Umbria", "Valle d'Aosta", "Veneto"
            ];
        }

        cmbRegione.Items.Clear();
        cmbRegione.Items.AddRange(regioni.Cast<object>().ToArray());
        if (cmbRegione.Items.Count > 0 && cmbRegione.SelectedIndex == -1)
        {
            cmbRegione.SelectedIndex = 0;
        }

        await RefreshGridAsync();
    }

    private async Task ImportRegioneAsync()
    {
        var regione = GetRegione();
        var imported = await _importer.ImportRegioneAsync(regione);
        await RefreshGridAsync();
        MessageBox.Show($"Importati {imported} enti per {regione}.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task AvviaRicercaAsync()
    {
        var regione = GetRegione();
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
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

            await _contactFinder.ProcessRegionAsync(regione, (int)numThread.Value, (int)numDelay.Value, progress, _cts.Token);
            await RefreshGridAsync();
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show("Elaborazione fermata.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
            var index = _rows.ToList().FindIndex(x => x.CodiceFiscale.Equals(ente.CodiceFiscale, StringComparison.OrdinalIgnoreCase));
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
}
