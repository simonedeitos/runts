#nullable disable
namespace EasySearch.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;
    private MenuStrip menuStripMain;
    private ToolStripMenuItem menuImpostazioni;
    private ToolStripMenuItem menuConfiguraBrightData;
    private GroupBox groupConfigurazione;
    private TextBox txtCsvComuni;
    private Button btnBrowseCsvComuni;
    private TextBox txtParolaCerca;
    private RadioButton rbRisultatoUnivoco;
    private RadioButton rbMultiRisultato;
    private CheckBox chkEmail;
    private CheckBox chkPec;
    private CheckBox chkTelefono;
    private CheckBox chkSitoWeb;
    private CheckBox chkIndirizzo;
    private NumericUpDown numThread;
    private NumericUpDown numDelay;
    private CheckBox chkShowChrome;
    private Button btnAvvia;
    private Button btnPausa;
    private Button btnRiprendi;
    private Button btnFerma;
    private Button btnExportCsv;
    private Button btnExportExcel;
    private Label lblTotale;
    private Label lblElaborati;
    private Label lblSiti;
    private Label lblEmail;
    private Label lblPec;
    private Label lblErrori;
    private Label lblFonte;
    private Label lblStatusComuni;
    private ProgressBar progressBar;
    private DataGridView gridEnti;
    private GroupBox groupRisultati;
    private TableLayoutPanel tableRoot;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        #nullable restore
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        menuStripMain = new MenuStrip();
        menuImpostazioni = new ToolStripMenuItem();
        menuConfiguraBrightData = new ToolStripMenuItem();
        tableRoot = new TableLayoutPanel();
        groupConfigurazione = new GroupBox();
        txtCsvComuni = new TextBox();
        btnBrowseCsvComuni = new Button();
        txtParolaCerca = new TextBox();
        rbRisultatoUnivoco = new RadioButton();
        rbMultiRisultato = new RadioButton();
        chkEmail = new CheckBox();
        chkPec = new CheckBox();
        chkTelefono = new CheckBox();
        chkSitoWeb = new CheckBox();
        chkIndirizzo = new CheckBox();
        numThread = new NumericUpDown();
        numDelay = new NumericUpDown();
        chkShowChrome = new CheckBox();
        btnAvvia = new Button();
        btnPausa = new Button();
        btnRiprendi = new Button();
        btnFerma = new Button();
        btnExportCsv = new Button();
        btnExportExcel = new Button();
        lblTotale = new Label();
        lblElaborati = new Label();
        lblSiti = new Label();
        lblEmail = new Label();
        lblPec = new Label();
        lblErrori = new Label();
        lblFonte = new Label();
        lblStatusComuni = new Label();
        progressBar = new ProgressBar();
        groupRisultati = new GroupBox();
        gridEnti = new DataGridView();
        ((System.ComponentModel.ISupportInitialize)numThread).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numDelay).BeginInit();
        ((System.ComponentModel.ISupportInitialize)gridEnti).BeginInit();
        SuspendLayout();

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.White;
        ClientSize = new Size(1500, 900);
        MinimumSize = new Size(1400, 800);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "EASYSEARCH";

        menuStripMain.BackColor = Color.White;
        menuStripMain.Dock = DockStyle.Top;
        menuStripMain.Items.AddRange(new ToolStripItem[] { menuImpostazioni });
        menuStripMain.Padding = new Padding(12, 4, 12, 4);
        menuStripMain.TabIndex = 0;
        menuStripMain.Text = "menuStripMain";

        menuImpostazioni.Alignment = ToolStripItemAlignment.Right;
        menuImpostazioni.DropDownItems.AddRange(new ToolStripItem[] { menuConfiguraBrightData });
        menuImpostazioni.Text = "Impostazioni";

        menuConfiguraBrightData.Text = "Configura Bright Data API";

        tableRoot.ColumnCount = 1;
        tableRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableRoot.Dock = DockStyle.Fill;
        tableRoot.Location = new Point(0, 32);
        tableRoot.Padding = new Padding(12);
        tableRoot.RowCount = 6;
        tableRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 250F));
        tableRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        tableRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        tableRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        tableRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        tableRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        groupConfigurazione.Dock = DockStyle.Fill;
        groupConfigurazione.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        groupConfigurazione.Text = "Configurazione ricerca";
        groupConfigurazione.Padding = new Padding(12);

        var configLayout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            RowCount = 4
        };
        configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86F));
        configLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var lblCsv = new Label
        {
            Text = "CSV comuni ISTAT:",
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F)
        };
        txtCsvComuni.Dock = DockStyle.Fill;
        txtCsvComuni.Font = new Font("Segoe UI", 9F);
        btnBrowseCsvComuni.Text = "Sfoglia...";
        btnBrowseCsvComuni.Dock = DockStyle.Fill;

        var lblParola = new Label
        {
            Text = "Parola da cercare:",
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F)
        };
        txtParolaCerca.Dock = DockStyle.Left;
        txtParolaCerca.Font = new Font("Segoe UI", 9F);
        txtParolaCerca.Width = 280;
        txtParolaCerca.Text = "Pro Loco";

        var panelsLayout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4)
        };
        panelsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280F));
        panelsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var groupModalita = new GroupBox
        {
            Text = "Modalità risultato:",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F)
        };
        var modalitaPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(10, 10, 0, 0),
            WrapContents = false
        };
        rbRisultatoUnivoco.Text = "Risultato univoco";
        rbRisultatoUnivoco.Checked = true;
        rbMultiRisultato.Text = "Multi risultato";
        modalitaPanel.Controls.AddRange(new Control[] { rbRisultatoUnivoco, rbMultiRisultato });
        groupModalita.Controls.Add(modalitaPanel);

        var groupDati = new GroupBox
        {
            Text = "Dati da cercare:",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F)
        };
        var datiPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 12, 10, 10)
        };
        chkEmail.Text = "Email";
        chkEmail.Checked = true;
        chkPec.Text = "PEC";
        chkPec.Checked = true;
        chkTelefono.Text = "Telefono";
        chkTelefono.Checked = true;
        chkSitoWeb.Text = "Sito Web";
        chkSitoWeb.Checked = true;
        chkIndirizzo.Text = "Indirizzo";
        datiPanel.Controls.AddRange(new Control[] { chkEmail, chkPec, chkTelefono, chkSitoWeb, chkIndirizzo });
        groupDati.Controls.Add(datiPanel);

        panelsLayout.Controls.Add(groupModalita, 0, 0);
        panelsLayout.Controls.Add(groupDati, 1, 0);

        var settingsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 6, 0, 0)
        };
        var lblThread = new Label { Text = "Thread:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) };
        numThread.Minimum = 1;
        numThread.Maximum = 10;
        numThread.Value = 3;
        numThread.Width = 70;
        var lblDelay = new Label { Text = "Delay (ms):", AutoSize = true, Margin = new Padding(20, 8, 6, 0) };
        numDelay.Minimum = 100;
        numDelay.Maximum = 5000;
        numDelay.Value = 500;
        numDelay.Increment = 100;
        numDelay.Width = 90;
        chkShowChrome.Text = "Mostra Chrome (debug)";
        chkShowChrome.AutoSize = true;
        chkShowChrome.Margin = new Padding(20, 6, 12, 0);
        btnAvvia.Text = "Avvia Ricerca";
        btnPausa.Text = "Pausa";
        btnRiprendi.Text = "Riprendi";
        btnFerma.Text = "Ferma";
        btnExportCsv.Text = "Esporta CSV";
        btnExportExcel.Text = "Esporta Excel";
        btnAvvia.Size = new Size(120, 34);
        btnPausa.Size = new Size(90, 34);
        btnRiprendi.Size = new Size(90, 34);
        btnFerma.Size = new Size(90, 34);
        btnExportCsv.Size = new Size(110, 34);
        btnExportExcel.Size = new Size(110, 34);
        settingsPanel.Controls.AddRange(new Control[]
        {
            lblThread, numThread, lblDelay, numDelay, chkShowChrome,
            btnAvvia, btnPausa, btnRiprendi, btnFerma, btnExportCsv, btnExportExcel
        });

        configLayout.Controls.Add(lblCsv, 0, 0);
        configLayout.Controls.Add(txtCsvComuni, 1, 0);
        configLayout.Controls.Add(btnBrowseCsvComuni, 2, 0);
        configLayout.Controls.Add(lblParola, 0, 1);
        configLayout.Controls.Add(txtParolaCerca, 1, 1);
        configLayout.SetColumnSpan(txtParolaCerca, 2);
        configLayout.Controls.Add(panelsLayout, 0, 2);
        configLayout.SetColumnSpan(panelsLayout, 3);
        configLayout.Controls.Add(settingsPanel, 0, 3);
        configLayout.SetColumnSpan(settingsPanel, 3);
        groupConfigurazione.Controls.Add(configLayout);

        var statsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0),
            WrapContents = false
        };
        lblTotale.AutoSize = true;
        lblTotale.Margin = new Padding(0, 8, 24, 0);
        lblElaborati.AutoSize = true;
        lblElaborati.Margin = new Padding(0, 8, 24, 0);
        lblSiti.AutoSize = true;
        lblSiti.Margin = new Padding(0, 8, 24, 0);
        lblEmail.AutoSize = true;
        lblEmail.Margin = new Padding(0, 8, 24, 0);
        lblPec.AutoSize = true;
        lblPec.Margin = new Padding(0, 8, 24, 0);
        lblErrori.AutoSize = true;
        lblErrori.Margin = new Padding(0, 8, 24, 0);
        statsPanel.Controls.AddRange(new Control[] { lblTotale, lblElaborati, lblSiti, lblEmail, lblPec, lblErrori });

        progressBar.Dock = DockStyle.Fill;
        progressBar.Style = ProgressBarStyle.Continuous;

        lblFonte.Dock = DockStyle.Fill;
        lblFonte.TextAlign = ContentAlignment.MiddleLeft;
        lblFonte.Font = new Font("Segoe UI", 9F);
        lblFonte.Text = "Fonte dati: -";

        lblStatusComuni.Dock = DockStyle.Fill;
        lblStatusComuni.TextAlign = ContentAlignment.MiddleLeft;
        lblStatusComuni.Font = new Font("Segoe UI", 9F);
        lblStatusComuni.ForeColor = Color.DarkSlateBlue;
        lblStatusComuni.Text = "Pronto";

        groupRisultati.Dock = DockStyle.Fill;
        groupRisultati.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        groupRisultati.Text = "Risultati";
        groupRisultati.Padding = new Padding(12);

        gridEnti.AllowUserToAddRows = false;
        gridEnti.AllowUserToDeleteRows = false;
        gridEnti.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        gridEnti.BackgroundColor = Color.White;
        gridEnti.BorderStyle = BorderStyle.None;
        gridEnti.Dock = DockStyle.Fill;
        gridEnti.MultiSelect = false;
        gridEnti.ReadOnly = true;
        gridEnti.RowHeadersVisible = false;
        gridEnti.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        groupRisultati.Controls.Add(gridEnti);

        tableRoot.Controls.Add(groupConfigurazione, 0, 0);
        tableRoot.Controls.Add(statsPanel, 0, 1);
        tableRoot.Controls.Add(progressBar, 0, 2);
        tableRoot.Controls.Add(lblFonte, 0, 3);
        tableRoot.Controls.Add(lblStatusComuni, 0, 4);
        tableRoot.Controls.Add(groupRisultati, 0, 5);

        Controls.Add(tableRoot);
        Controls.Add(menuStripMain);
        MainMenuStrip = menuStripMain;

        ((System.ComponentModel.ISupportInitialize)numThread).EndInit();
        ((System.ComponentModel.ISupportInitialize)numDelay).EndInit();
        ((System.ComponentModel.ISupportInitialize)gridEnti).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }
}
