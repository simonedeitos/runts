#nullable disable
namespace runts.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;
    private ComboBox cmbModalita = null!;
    private ComboBox cmbRegione = null!;
    private Button btnImporta = null!;
    private Button btnAvvia = null!;
    private Button btnPausa = null!;
    private Button btnRiprendi = null!;
    private Button btnFerma = null!;
    private Button btnExportCsv = null!;
    private Button btnExportExcel = null!;
    private NumericUpDown numThread = null!;
    private CheckBox chkShowChrome = null!;
    private NumericUpDown numDelay = null!;
    private Label lblTotale = null!;
    private Label lblElaborati = null!;
    private Label lblSiti = null!;
    private Label lblEmail = null!;
    private Label lblPec = null!;
    private Label lblErrori = null!;
    private Label lblFonte = null!;
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
        cmbModalita = new ComboBox();
        cmbRegione = new ComboBox();
        btnImporta = new Button();
        btnAvvia = new Button();
        btnPausa = new Button();
        btnRiprendi = new Button();
        btnFerma = new Button();
        btnExportCsv = new Button();
        btnExportExcel = new Button();
        numThread = new NumericUpDown();
        chkShowChrome = new CheckBox();
        numDelay = new NumericUpDown();
        lblTotale = new Label();
        lblElaborati = new Label();
        lblSiti = new Label();
        lblEmail = new Label();
        lblPec = new Label();
        lblErrori = new Label();
        lblFonte = new Label();
        progressBar = new ProgressBar();
        gridEnti = new DataGridView();
        var lblModalita = new Label();
        var lblThread = new Label();
        var lblDelay = new Label();
        var toolTip = new ToolTip(components);

        SuspendLayout();

        Text = "RUNTS CONTACT FINDER";
        ClientSize = new Size(1280, 760);
        MinimumSize = new Size(1024, 700);
        StartPosition = FormStartPosition.CenterScreen;

        lblModalita.Text = "Modalità importazione";
        lblModalita.Location = new Point(20, 22);
        lblModalita.Size = new Size(130, 24);

        cmbModalita.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbModalita.Location = new Point(160, 20);
        cmbModalita.Size = new Size(280, 28);

        cmbRegione.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbRegione.Location = new Point(450, 20);
        cmbRegione.Size = new Size(190, 28);

        btnImporta.Text = "Importa Regione";
        btnImporta.Location = new Point(650, 18);
        btnImporta.Size = new Size(130, 32);

        btnAvvia.Text = "Avvia Ricerca";
        btnAvvia.Location = new Point(790, 18);
        btnAvvia.Size = new Size(120, 32);

        btnPausa.Text = "Pausa";
        btnPausa.Location = new Point(920, 18);
        btnPausa.Size = new Size(80, 32);

        btnRiprendi.Text = "Riprendi";
        btnRiprendi.Location = new Point(1010, 18);
        btnRiprendi.Size = new Size(80, 32);

        btnFerma.Text = "Ferma";
        btnFerma.Location = new Point(1100, 18);
        btnFerma.Size = new Size(80, 32);

        btnExportCsv.Text = "Esporta CSV Regione";
        btnExportCsv.Location = new Point(510, 58);
        btnExportCsv.Size = new Size(160, 32);

        btnExportExcel.Text = "Esporta Excel Regione";
        btnExportExcel.Location = new Point(680, 58);
        btnExportExcel.Size = new Size(170, 32);

        lblThread.Text = "Numero Thread";
        lblThread.Location = new Point(20, 62);
        lblThread.Size = new Size(110, 24);

        numThread.Minimum = 1;
        numThread.Maximum = 10;
        numThread.Value = 3;
        numThread.Location = new Point(135, 60);
        numThread.Size = new Size(80, 27);

        chkShowChrome.Text = "Mostra finestre Chrome (debug)";
        chkShowChrome.Location = new Point(20, 92);
        chkShowChrome.Size = new Size(260, 24);
        chkShowChrome.Checked = false;

        lblDelay.Text = "Delay richieste (ms)";
        lblDelay.Location = new Point(240, 62);
        lblDelay.Size = new Size(140, 24);

        numDelay.Minimum = 100;
        numDelay.Maximum = 5000;
        numDelay.Value = 500;
        numDelay.Increment = 100;
        numDelay.Location = new Point(390, 60);
        numDelay.Size = new Size(100, 27);

        progressBar.Location = new Point(20, 122);
        progressBar.Size = new Size(1150, 22);

        lblFonte.Location = new Point(20, 149);
        lblFonte.Size = new Size(1150, 24);

        lblTotale.Location = new Point(20, 175);
        lblTotale.Size = new Size(170, 24);
        lblElaborati.Location = new Point(200, 175);
        lblElaborati.Size = new Size(170, 24);
        lblSiti.Location = new Point(380, 175);
        lblSiti.Size = new Size(170, 24);
        lblEmail.Location = new Point(560, 175);
        lblEmail.Size = new Size(170, 24);
        lblPec.Location = new Point(740, 175);
        lblPec.Size = new Size(170, 24);
        lblErrori.Location = new Point(920, 175);
        lblErrori.Size = new Size(170, 24);

        gridEnti.Location = new Point(20, 210);
        gridEnti.Size = new Size(1235, 520);
        gridEnti.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        gridEnti.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        gridEnti.AllowUserToAddRows = false;
        gridEnti.AllowUserToDeleteRows = false;
        gridEnti.ReadOnly = true;

        toolTip.SetToolTip(cmbModalita, "Seleziona la modalità: RUNTS ufficiale, Pro Loco da albo PDF oppure Pro Loco per comune");
        toolTip.SetToolTip(cmbRegione, "Regione italiana da importare");
        toolTip.SetToolTip(numThread, "Numero massimo thread di lavoro (1-10)");
        toolTip.SetToolTip(chkShowChrome, "Se selezionato apre le finestre Chrome durante l'elaborazione");
        toolTip.SetToolTip(numDelay, "Delay tra richieste HTTP in millisecondi");
        toolTip.SetToolTip(btnImporta, "Importa enti RUNTS reali, Pro Loco da albi regionali ufficiali PDF o Pro Loco generate dai comuni ISTAT");

        Controls.AddRange([
            lblModalita,
            cmbModalita,
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
            chkShowChrome,
            lblDelay,
            numDelay,
            progressBar,
            lblFonte,
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
