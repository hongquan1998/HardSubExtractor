namespace HardSubExtractor
{
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
        private void InitializeComponent()
        {
            // Form
            this.Text = "Hard Subtitle Extractor - OCR Video to SRT";
            this.Size = new System.Drawing.Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;

            // Panel Top - Video Selection
            var panelTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 100,
                Padding = new Padding(10)
            };

            var lblVideo = new Label
            {
                Text = "Video File:",
                Location = new Point(10, 15),
                AutoSize = true
            };

            txtVideoPath = new TextBox
            {
                Location = new Point(100, 12),
                Width = 600,
                ReadOnly = true
            };

            btnLoadVideo = new Button
            {
                Text = "Browse Video",
                Location = new Point(710, 10),
                Width = 120,
                Height = 30
            };

            var lblFps = new Label
            {
                Text = "FPS:",
                Location = new Point(10, 55),
                AutoSize = true
            };

            numFps = new NumericUpDown
            {
                Location = new Point(100, 52),
                Width = 60,
                Minimum = 1,
                Maximum = 10,  // Can select up to 10 FPS
                Value = 4      // Default 4 FPS (optimal)
            };

            var lblFpsInfo = new Label
            {
                Text = "(Safe: 4+)",
                Location = new Point(245, 55),
                AutoSize = true,
                ForeColor = Color.Gray
            };

            btnOptimizeFps = new Button
            {
                Text = "⚡ Auto",
                Location = new Point(170, 52),
                Width = 70,
                Height = 25,
                BackColor = Color.LightYellow
            };

            var lblLanguage = new Label
            {
                Text = "Language:",
                Location = new Point(320, 55),
                AutoSize = true
            };

            cmbLanguage = new ComboBox
            {
                Location = new Point(400, 52),
                Width = 140,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            // Thêm các ngôn ngữ
            cmbLanguage.Items.Add("简体中文 (Chinese Simplified)");
            cmbLanguage.Items.Add("繁體中文 (Chinese Traditional)");
            cmbLanguage.Items.Add("English");
            cmbLanguage.Items.Add("日本語 (Japanese)");
            cmbLanguage.Items.Add("한국어 (Korean)");
            cmbLanguage.Items.Add("Tiếng Việt (Vietnamese)");
            cmbLanguage.SelectedIndex = 0; // Mặc định tiếng Trung giản thể

            var lblPreprocess = new Label
            {
                Text = "Preprocess:",
                Location = new Point(550, 55),
                AutoSize = true
            };

            cmbPreprocessMode = new ComboBox
            {
                Location = new Point(620, 52),
                Width = 130,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbPreprocessMode.Items.Add("Auto (Recommended)");
            cmbPreprocessMode.Items.Add("High Contrast");
            cmbPreprocessMode.Items.Add("Adaptive");
            cmbPreprocessMode.Items.Add("Color Detection");
            cmbPreprocessMode.Items.Add("Invert");
            cmbPreprocessMode.SelectedIndex = 0;

            var lblThreads = new Label
            {
                Text = "Threads:",
                Location = new Point(760, 55),
                AutoSize = true
            };

            numThreads = new NumericUpDown
            {
                Location = new Point(820, 52),
                Width = 50,
                Minimum = 1,
                Maximum = Environment.ProcessorCount,
                Value = Math.Max(1, Environment.ProcessorCount - 1)
            };

            var lblThreadsInfo = new Label
            {
                Text = $"(max: {Environment.ProcessorCount})",
                Location = new Point(875, 55),
                AutoSize = true,
                ForeColor = Color.Gray
            };

            chkDebugMode = new CheckBox
            {
                Text = "Debug OCR",
                Location = new Point(960, 54),
                AutoSize = true,
                ForeColor = Color.DarkRed
            };

            btnOpenDebugFolder = new Button
            {
                Text = "📁 Debug Folder",
                Location = new Point(1050, 52),
                Width = 110,
                Height = 25,
                Font = new Font(Font.FontFamily, 8, FontStyle.Regular)
            };
            btnOpenDebugFolder.Click += BtnOpenDebugFolder_Click;

            panelTop.Controls.AddRange(new Control[] { 
                lblVideo, txtVideoPath, btnLoadVideo, 
                lblFps, numFps, lblFpsInfo,
                lblLanguage, cmbLanguage, lblPreprocess, cmbPreprocessMode,
                lblThreads, numThreads, lblThreadsInfo,
                chkDebugMode, btnOpenDebugFolder, btnOptimizeFps
            });

            // Panel Center - Buttons
            var panelCenter = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                Padding = new Padding(10)
            };

            btnSelectRoi = new Button
            {
                Text = "1. Preview & Select ROI",
                Location = new Point(10, 10),
                Width = 180,
                Height = 50,
                Enabled = false
            };

            btnStartOcr = new Button
            {
                Text = "2. Start OCR",
                Location = new Point(200, 10),
                Width = 150,
                Height = 50,
                Enabled = false
            };

            btnCleanSubtitle = new Button
            {
                Text = "3. Clean Subtitle",
                Location = new Point(400, 10),
                Width = 150,
                Height = 50,
                Enabled = false
            };

            btnFixTime = new Button
            {
                Text = "4. Fix Time",
                Location = new Point(560, 10),
                Width = 150,
                Height = 50,
                Enabled = false
            };

            btnExportSrt = new Button
            {
                Text = "5. Export SRT",
                Location = new Point(720, 10),
                Width = 150,
                Height = 50,
                Enabled = false,
                BackColor = Color.LightGreen
            };

            btnCreatePrompt = new Button
            {
                Text = "📋 Create Translate Prompt",
                Location = new Point(880, 10),
                Width = 180,
                Height = 50,
                Enabled = false,
                BackColor = Color.LightCyan,
                Font = new Font(Font.FontFamily, 9, FontStyle.Regular)
            };

            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(1070, 10),
                Width = 100,
                Height = 50,
                Enabled = false,
                BackColor = Color.LightCoral
            };

            panelCenter.Controls.AddRange(new Control[] { 
                btnSelectRoi, btnStartOcr, btnCleanSubtitle, btnFixTime, btnExportSrt, btnCreatePrompt, btnCancel 
            });

            // Progress Bar
            var panelProgress = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                Padding = new Padding(10)
            };

            lblStatus = new Label
            {
                Text = "Ready",
                Location = new Point(10, 10),
                AutoSize = true,
                Font = new Font(Font.FontFamily, 10, FontStyle.Bold)
            };

            progressBar = new ProgressBar
            {
                Location = new Point(10, 35),
                Width = 920,
                Height = 20
            };

            panelProgress.Controls.AddRange(new Control[] { lblStatus, progressBar });

            // TabControl
            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(10, 10)
            };

            // Tab 1: Subtitle Preview
            var tabPreview = new TabPage("Subtitle Preview");
            
            dgvSubtitles = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            dgvSubtitles.Columns.Add(new DataGridViewTextBoxColumn { Name = "Index", HeaderText = "#", Width = 50 });
            dgvSubtitles.Columns.Add(new DataGridViewTextBoxColumn { Name = "StartTime", HeaderText = "Start", Width = 120 });
            dgvSubtitles.Columns.Add(new DataGridViewTextBoxColumn { Name = "EndTime", HeaderText = "End", Width = 120 });
            dgvSubtitles.Columns.Add(new DataGridViewTextBoxColumn { Name = "Duration", HeaderText = "Duration", Width = 80 });
            dgvSubtitles.Columns.Add(new DataGridViewTextBoxColumn { Name = "Text", HeaderText = "Text", Width = 400 });

            tabPreview.Controls.Add(dgvSubtitles);

            // Tab 2: Log
            var tabLog = new TabPage("Log");

            txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Font = new Font("Consolas", 9)
            };

            var panelLogButtons = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40
            };

            var btnClearLog = new Button
            {
                Text = "Clear Log",
                Location = new Point(10, 5),
                Width = 100,
                Height = 30
            };
            btnClearLog.Click += (s, e) => txtLog.Clear();

            panelLogButtons.Controls.Add(btnClearLog);

            tabLog.Controls.Add(txtLog);
            tabLog.Controls.Add(panelLogButtons);

            tabControl.TabPages.Add(tabPreview);
            tabControl.TabPages.Add(tabLog);

            // Add all to form
            this.Controls.Add(tabControl);
            this.Controls.Add(panelProgress);
            this.Controls.Add(panelCenter);
            this.Controls.Add(panelTop);
        }

        #endregion

        private TextBox txtVideoPath = null!;
        private Button btnLoadVideo = null!;
        private NumericUpDown numFps = null!;
        private NumericUpDown numThreads = null!;
        private ComboBox cmbLanguage = null!;
        private CheckBox chkDebugMode = null!;
        private Button btnOpenDebugFolder = null!;
        private ComboBox cmbPreprocessMode = null!;
        private Button btnSelectRoi = null!;
        private Button btnStartOcr = null!;
        private Button btnCleanSubtitle = null!;
        private Button btnFixTime = null!;
        private Button btnExportSrt = null!;
        private Button btnCreatePrompt = null!;
        private Button btnCancel = null!;
        private Label lblStatus = null!;
        private ProgressBar progressBar = null!;
        private DataGridView dgvSubtitles = null!;
        private TextBox txtLog = null!;
        private Button btnOptimizeFps = null!;
    }
}
