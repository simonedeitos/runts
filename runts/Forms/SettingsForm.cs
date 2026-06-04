using runts.Helpers;
using runts.Services;

namespace runts.Forms;

/// <summary>
/// Form per configurazione impostazioni applicazione.
/// </summary>
public sealed class SettingsForm : Form
{
    private TextBox _txtBrightDataApiKey = null!;
    private Button _btnTest = null!;
    private Label _lblStatus = null!;

    public SettingsForm()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = "Impostazioni RUNTS Contact Finder";
        Size = new Size(600, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var grpBrightData = new GroupBox
        {
            Text = "Bright Data Web Unlocker API",
            Location = new Point(20, 20),
            Size = new Size(540, 150)
        };

        var lblInfo = new Label
        {
            Text = "Inserisci la tua API Key Bright Data per abilitare ricerca web scalabile.\nRegistrati su: https://brightdata.com/products/web-unlocker",
            Location = new Point(10, 25),
            Size = new Size(520, 40),
            ForeColor = Color.Gray
        };

        var lblApiKey = new Label
        {
            Text = "API Key:",
            Location = new Point(10, 75),
            AutoSize = true
        };

        _txtBrightDataApiKey = new TextBox
        {
            Location = new Point(80, 72),
            Size = new Size(440, 25),
            PasswordChar = '*'
        };

        _btnTest = new Button
        {
            Text = "Testa Connessione",
            Location = new Point(80, 105),
            Size = new Size(150, 30)
        };
        _btnTest.Click += BtnTest_Click;

        _lblStatus = new Label
        {
            Location = new Point(240, 110),
            Size = new Size(280, 20),
            ForeColor = Color.Blue,
            Text = string.Empty
        };

        grpBrightData.Controls.AddRange([lblInfo, lblApiKey, _txtBrightDataApiKey, _btnTest, _lblStatus]);

        var btnSave = new Button
        {
            Text = "Salva Impostazioni",
            Location = new Point(400, 200),
            Size = new Size(140, 35)
        };
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button
        {
            Text = "Annulla",
            Location = new Point(280, 200),
            Size = new Size(100, 35)
        };
        btnCancel.Click += (_, _) => Close();

        Controls.AddRange([grpBrightData, btnSave, btnCancel]);
    }

    private void LoadSettings()
    {
        _txtBrightDataApiKey.Text = RegistrySettingsManager.GetBrightDataApiKey() ?? string.Empty;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        RegistrySettingsManager.SaveBrightDataApiKey(_txtBrightDataApiKey.Text.Trim());

        MessageBox.Show(
            "Impostazioni salvate correttamente!",
            "Salvataggio",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        DialogResult = DialogResult.OK;
        Close();
    }

    private async void BtnTest_Click(object? sender, EventArgs e)
    {
        _btnTest.Enabled = false;
        _lblStatus.Text = "Test in corso...";
        _lblStatus.ForeColor = Color.Blue;

        var previousApiKey = RegistrySettingsManager.GetBrightDataApiKey();
        try
        {
            var apiKey = _txtBrightDataApiKey.Text.Trim();
            RegistrySettingsManager.SaveBrightDataApiKey(apiKey);

            var logger = new LoggerService();
            using var service = new BrightDataSearchService(logger);
            var results = await service.SearchGoogleAsync("test", CancellationToken.None);

            if (results.Count > 0)
            {
                _lblStatus.Text = "✓ Connessione riuscita!";
                _lblStatus.ForeColor = Color.Green;
            }
            else
            {
                _lblStatus.Text = "⚠ Nessun risultato (verifica API Key)";
                _lblStatus.ForeColor = Color.Orange;
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"✗ Errore: {ex.Message}";
            _lblStatus.ForeColor = Color.Red;
        }
        finally
        {
            RegistrySettingsManager.SaveBrightDataApiKey(previousApiKey ?? string.Empty);
            _btnTest.Enabled = true;
        }
    }
}
