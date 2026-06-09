using EasySearch.Helpers;
using EasySearch.Services;

namespace EasySearch.Forms;

/// <summary>
/// Form per configurazione impostazioni applicazione.
/// </summary>
public sealed class SettingsForm : Form
{
    private TextBox _txtBrightDataApiKey = null!;
    private TextBox _txtBrightDataHost = null!;
    private NumericUpDown _numBrightDataPort = null!;
    private Button _btnTest = null!;
    private Label _lblStatus = null!;

    public SettingsForm()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = "Impostazioni EasySearch";
        Size = new Size(680, 380);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var grpBrightData = new GroupBox
        {
            Text = "Bright Data Datacenter Proxy",
            Location = new Point(20, 20),
            Size = new Size(620, 260)
        };

        var lblInfo = new Label
        {
            Text = "Inserisci credenziali Bright Data in formato username:password e configura host/porta del proxy.",
            Location = new Point(10, 25),
            Size = new Size(590, 40),
            ForeColor = Color.Gray
        };

        var lblApiKey = new Label
        {
            Text = "API Key (username:password):",
            Location = new Point(10, 75),
            AutoSize = true
        };

        _txtBrightDataApiKey = new TextBox
        {
            Location = new Point(220, 72),
            Size = new Size(380, 25),
            PasswordChar = '*',
            PlaceholderText = "brd-customer-xxx-zone-datacenter_proxy1:password"
        };

        var lblHost = new Label
        {
            Text = "Host:",
            Location = new Point(10, 112),
            AutoSize = true
        };

        _txtBrightDataHost = new TextBox
        {
            Location = new Point(220, 109),
            Size = new Size(380, 25),
            Text = "brd.superproxy.io"
        };

        var lblPort = new Label
        {
            Text = "Port:",
            Location = new Point(10, 149),
            AutoSize = true
        };

        _numBrightDataPort = new NumericUpDown
        {
            Location = new Point(220, 146),
            Size = new Size(110, 25),
            Minimum = 1,
            Maximum = 65535,
            Value = 22225
        };

        var lblPortInfo = new Label
        {
            Text = "22225 = Datacenter Proxy, 33335 = Web Unlocker",
            Location = new Point(340, 149),
            Size = new Size(260, 20),
            ForeColor = Color.Gray
        };

        _btnTest = new Button
        {
            Text = "Testa Connessione",
            Location = new Point(220, 188),
            Size = new Size(160, 35)
        };
        _btnTest.Click += BtnTest_Click;

        _lblStatus = new Label
        {
            Location = new Point(220, 232),
            Size = new Size(380, 20),
            ForeColor = Color.Blue,
            Text = string.Empty
        };

        grpBrightData.Controls.AddRange([
            lblInfo, lblApiKey, _txtBrightDataApiKey,
            lblHost, _txtBrightDataHost,
            lblPort, _numBrightDataPort, lblPortInfo,
            _btnTest, _lblStatus
        ]);

        var btnSave = new Button
        {
            Text = "Salva Impostazioni",
            Location = new Point(500, 300),
            Size = new Size(140, 35)
        };
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button
        {
            Text = "Annulla",
            Location = new Point(380, 300),
            Size = new Size(100, 35)
        };
        btnCancel.Click += (_, _) => Close();

        Controls.AddRange([grpBrightData, btnSave, btnCancel]);
    }

    private void LoadSettings()
    {
        _txtBrightDataApiKey.Text = RegistrySettingsManager.GetBrightDataApiKey() ?? string.Empty;
        _txtBrightDataHost.Text = RegistrySettingsManager.GetBrightDataHost();
        _numBrightDataPort.Value = RegistrySettingsManager.GetBrightDataPort();
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (!TryGetValidatedInputs(out var apiKey, out var host, out var port))
        {
            return;
        }

        RegistrySettingsManager.SaveBrightDataApiKey(apiKey);
        RegistrySettingsManager.SaveBrightDataHost(host);
        RegistrySettingsManager.SaveBrightDataPort(port);

        MessageBox.Show(
            $"Impostazioni salvate correttamente!\n\nHost: {host}\nPort: {port}",
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
        var previousHost = RegistrySettingsManager.GetBrightDataHost();
        var previousPort = RegistrySettingsManager.GetBrightDataPort();
        try
        {
            if (!TryGetValidatedInputs(out var apiKey, out var host, out var port))
            {
                _lblStatus.Text = "✗ Configurazione non valida";
                _lblStatus.ForeColor = Color.Red;
                return;
            }

            RegistrySettingsManager.SaveBrightDataApiKey(apiKey);
            RegistrySettingsManager.SaveBrightDataHost(host);
            RegistrySettingsManager.SaveBrightDataPort(port);

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
                _lblStatus.Text = "⚠ Nessun risultato (verifica credenziali o proxy)";
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
            RegistrySettingsManager.SaveBrightDataHost(previousHost);
            RegistrySettingsManager.SaveBrightDataPort(previousPort);
            _btnTest.Enabled = true;
        }
    }

    private bool TryGetValidatedInputs(out string apiKey, out string host, out int port)
    {
        apiKey = _txtBrightDataApiKey.Text.Trim();
        host = _txtBrightDataHost.Text.Trim();
        port = (int)_numBrightDataPort.Value;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show("API Key non può essere vuota.", "Validazione", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        if (!apiKey.Contains(':'))
        {
            MessageBox.Show("API Key deve essere nel formato username:password.", "Validazione", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            MessageBox.Show("Host non può essere vuoto.", "Validazione", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        return true;
    }
}
