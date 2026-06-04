#nullable disable
namespace runts.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;
    private ComboBox cmbRegione = null!;
    private Button btnImporta = null!;
    private Button btnAvvia = null!;
    private Button btnPausa = null!;
    private Button btnRiprendi = null!;
    private Button btnFerma = null!;
    private Button btnExportCsv = null!;
    private Button btnExportExcel = null!;
    private NumericUpDown numThread = null!;
    private NumericUpDown numDelay = null!;
    private Label lblTotale = null!;
    private Label lblElaborati = null!;
    private Label lblSiti = null!;
    private Label lblEmail = null!;
    private Label lblPec = null!;
    private Label lblErrori = null!;
    private ProgressBar progressBar = null!;
    private DataGridView gridEnti = null!;

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
        cmbRegione = new ComboBox();
        btnImporta = new Button();
        btnAvvia = new Button();
        btnPausa = new Button();
        btnRiprendi = new Button();
        btnFerma = new Button();
        btnExportCsv = new Button();
        btnExportExcel = new Button();
        numThread = new NumericUpDown();
        numDelay = new NumericUpDown();
        lblTotale = new Label();
        lblElaborati = new Label();
        lblSiti = new Label();
        lblEmail = new Label();
        lblPec = new Label();
        lblErrori = new Label();
        progressBar = new ProgressBar();
        gridEnti = new DataGridView();
        var lblThread = new Label();
        var lblDelay = new Label();
        var toolTip = new ToolTip(components);

        SuspendLayout();

        Text = "RUNTS CONTACT FINDER";
        ClientSize = new Size(1280, 760);
        MinimumSize = new Size(1024, 700);
        StartPosition = FormStartPosition.CenterScreen;

        cmbRegione.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbRegione.Location = new Point(20, 20);
        cmbRegione.Size = new Size(220, 28);

        btnImporta.Text = "Importa Regione";
        btnImporta.Location = new Point(255, 18);
        btnImporta.Size = new Size(130, 32);

        btnAvvia.Text = "Avvia Ricerca";
        btnAvvia.Location = new Point(400, 18);
        btnAvvia.Size = new Size(120, 32);

        btnPausa.Text = "Pausa";
        btnPausa.Location = new Point(530, 18);
        btnPausa.Size = new Size(90, 32);

        btnRiprendi.Text = "Riprendi";
        btnRiprendi.Location = new Point(630, 18);
        btnRiprendi.Size = new Size(90, 32);

        btnFerma.Text = "Ferma";
        btnFerma.Location = new Point(730, 18);
        btnFerma.Size = new Size(90, 32);

        btnExportCsv.Text = "Esporta CSV Regione";
        btnExportCsv.Location = new Point(830, 18);
        btnExportCsv.Size = new Size(160, 32);

        btnExportExcel.Text = "Esporta Excel Regione";
        btnExportExcel.Location = new Point(1000, 18);
        btnExportExcel.Size = new Size(170, 32);

        lblThread.Text = "Numero Thread";
        lblThread.Location = new Point(20, 62);
        lblThread.Size = new Size(110, 24);

        numThread.Minimum = 1;
        numThread.Maximum = 10;
        numThread.Value = 3;
        numThread.Location = new Point(135, 60);
        numThread.Size = new Size(80, 27);

        lblDelay.Text = "Delay richieste (ms)";
        lblDelay.Location = new Point(240, 62);
        lblDelay.Size = new Size(140, 24);

        numDelay.Minimum = 100;
        numDelay.Maximum = 5000;
        numDelay.Value = 500;
        numDelay.Increment = 100;
        numDelay.Location = new Point(390, 60);
        numDelay.Size = new Size(100, 27);

        progressBar.Location = new Point(20, 95);
        progressBar.Size = new Size(1150, 22);

        lblTotale.Location = new Point(20, 128);
        lblTotale.Size = new Size(170, 24);
        lblElaborati.Location = new Point(200, 128);
        lblElaborati.Size = new Size(170, 24);
        lblSiti.Location = new Point(380, 128);
        lblSiti.Size = new Size(170, 24);
        lblEmail.Location = new Point(560, 128);
        lblEmail.Size = new Size(170, 24);
        lblPec.Location = new Point(740, 128);
        lblPec.Size = new Size(170, 24);
        lblErrori.Location = new Point(920, 128);
        lblErrori.Size = new Size(170, 24);

        gridEnti.Location = new Point(20, 160);
        gridEnti.Size = new Size(1235, 570);
        gridEnti.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        gridEnti.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        gridEnti.AllowUserToAddRows = false;
        gridEnti.AllowUserToDeleteRows = false;
        gridEnti.ReadOnly = true;

        toolTip.SetToolTip(numThread, "Numero massimo thread di lavoro (1-10)");
        toolTip.SetToolTip(numDelay, "Delay tra richieste HTTP in millisecondi");

        Controls.AddRange([
            cmbRegione,
            btnImporta,
            btnAvvia,
            btnPausa,
            btnRiprendi,
            btnFerma,
            btnExportCsv,
            btnExportExcel,
            lblThread,
            numThread,
            lblDelay,
            numDelay,
            progressBar,
            lblTotale,
            lblElaborati,
            lblSiti,
            lblEmail,
            lblPec,
            lblErrori,
            gridEnti
        ]);

        ResumeLayout(false);
    }
}
