namespace Launcher;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private Button btnGenerate;
    private Button btnAbout;
    private TextBox txtAppName;
    private TextBox txtUrl;
    private PictureBox picIconPreview;
     private Label lblStatus;
    private ProgressBar progressBar1;

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();

        this.picIconPreview = new PictureBox();
        
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(900, 550);
        this.Text = "WebView2 App Generator - Web2EXE Wizard";

        // Label for App Name
        Label lblAppName = new Label();
        lblAppName.Text = "App Name";
        lblAppName.Location = new System.Drawing.Point(50, 20);
        lblAppName.Size = new System.Drawing.Size(100, 20);
        lblAppName.AutoSize = true;
        this.Controls.Add(lblAppName);

        // TextBox for App Name
        txtAppName = new TextBox();
        txtAppName.Text = "My Application";
        txtAppName.Location = new System.Drawing.Point(50, 45);
        txtAppName.Size = new System.Drawing.Size(700, 30);
        txtAppName.Font = new System.Drawing.Font(this.Font.FontFamily, 10);
        this.Controls.Add(txtAppName);

        // Label for Website URL
        Label lblUrl = new Label();
        lblUrl.Text = "Website URL";
        lblUrl.Location = new System.Drawing.Point(50, 85);
        lblUrl.Size = new System.Drawing.Size(120, 20);
        lblUrl.AutoSize = true;
        this.Controls.Add(lblUrl);

        // TextBox for URL input
        txtUrl = new TextBox();
        txtUrl.Text = "https://www.youtube.com";
        txtUrl.Location = new System.Drawing.Point(50, 110);
        txtUrl.Size = new System.Drawing.Size(700, 30);
        txtUrl.Font = new System.Drawing.Font(this.Font.FontFamily, 10);
        txtUrl.TextChanged += txtUrl_TextChanged;
        this.Controls.Add(txtUrl);

        // PictureBox for favicon preview
        picIconPreview = new PictureBox();
        picIconPreview.Name = "picIconPreview";
        picIconPreview.Size = new System.Drawing.Size(64, 64);
        picIconPreview.Location = new System.Drawing.Point(770, 110);
        picIconPreview.SizeMode = PictureBoxSizeMode.Zoom;
        picIconPreview.BorderStyle = BorderStyle.FixedSingle;
        this.Controls.Add(picIconPreview);

        // Generate Button
        btnGenerate = new Button();
        btnGenerate.Text = "Generate";
        btnGenerate.Location = new System.Drawing.Point(50, 160);
        btnGenerate.Size = new System.Drawing.Size(120, 40);
        btnGenerate.Click += btnGenerate_Click;
        this.Controls.Add(btnGenerate);

        // About Button
        btnAbout = new Button();
        btnAbout.Text = "About";
        btnAbout.Location = new System.Drawing.Point(180, 160);
        btnAbout.Size = new System.Drawing.Size(100, 40);
        btnAbout.Click += btnAbout_Click;
        this.Controls.Add(btnAbout);

        // Progress Bar
        progressBar1 = new ProgressBar();
        progressBar1.Location = new System.Drawing.Point(50, 210);
        progressBar1.Size = new System.Drawing.Size(700, 25);
        progressBar1.Minimum = 0;
        progressBar1.Maximum = 100;
        progressBar1.Value = 0;
        this.Controls.Add(progressBar1);

        // Status Label
        lblStatus = new Label();
        lblStatus.Text = "Ready";
        lblStatus.Location = new System.Drawing.Point(50, 250);
        lblStatus.Size = new System.Drawing.Size(700, 250);
        lblStatus.AutoSize = false;
        lblStatus.Font = new System.Drawing.Font(this.Font.FontFamily, 9);
        lblStatus.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
        lblStatus.BorderStyle = BorderStyle.Fixed3D;
        lblStatus.Padding = new Padding(10);
        lblStatus.TextAlign = ContentAlignment.TopLeft;
        this.Controls.Add(lblStatus);
    }

    #endregion
}
