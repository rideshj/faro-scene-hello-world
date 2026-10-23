using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FAROSceneSDCardWorkflow.Hardware;
using FAROSceneSDCardWorkflow.Registration;

namespace FAROSceneSDCardWorkflow.UI
{
    /// <summary>
    /// Interactive dialog popped up automatically upon SD card insertion.
    /// Lets the user configure the project name, output location, target types, and begin the workflow.
    /// </summary>
    public class SDCardPromptDialog : Form
    {
        private readonly SDCardInfo _cardInfo;

        public string ProjectName => _txtProjectName.Text.Trim();
        public string BaseDirectory => _txtBaseDirectory.Text.Trim();
        public TargetType SelectedTargetType { get; private set; } = TargetType.Sphere140mm;
        public bool ApplyStrayFilter => _chkStrayFilter.Checked;
        public bool PerformRegistration => _chkRegistration.Checked;
        public bool ShowTopViewVerification => _chkTopView.Checked;

        private TextBox _txtProjectName;
        private TextBox _txtBaseDirectory;
        private ComboBox _cmbTargetType;
        private CheckBox _chkStrayFilter;
        private CheckBox _chkRegistration;
        private CheckBox _chkTopView;
        private Button _btnStart;
        private Button _btnCancel;

        public SDCardPromptDialog(SDCardInfo cardInfo)
        {
            _cardInfo = cardInfo ?? throw new ArgumentNullException(nameof(cardInfo));

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "FARO SCENE - SD Card Scan Ingestion Detected";
            Width = 620;
            Height = 540;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(248, 249, 251);

            // Banner Header
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = Color.FromArgb(24, 76, 120),
                Padding = new Padding(20, 14, 20, 14)
            };

            var lblTitle = new Label
            {
                Text = "FARO Focus SD Card Detected",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 14),
                AutoSize = true
            };

            var lblSubtitle = new Label
            {
                Text = $"Volume {_cardInfo.DriveLetter} ({_cardInfo.VolumeLabel}) - {_cardInfo.ScanCount} Scan(s) Ready for Ingestion ({_cardInfo.TotalScanSizeMB:F1} MB)",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(200, 225, 255),
                Location = new Point(21, 44),
                AutoSize = true
            };

            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSubtitle);

            // Content Container
            var content = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 16, 24, 16)
            };

            int y = 14;

            // Group 1: Project Information
            var grpProject = new GroupBox
            {
                Text = "New Project Information",
                Location = new Point(24, y),
                Size = new Size(556, 130),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 50, 65)
            };

            var lblProjName = new Label
            {
                Text = "Project Name:",
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(16, 30),
                AutoSize = true
            };
            string defaultName = $"Project_{DateTime.Now:yyyyMMdd_HHmm}";
            _txtProjectName = new TextBox
            {
                Text = defaultName,
                Font = new Font("Segoe UI", 9f),
                Location = new Point(130, 27),
                Size = new Size(405, 23)
            };

            var lblProjDir = new Label
            {
                Text = "Project Folder:",
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(16, 68),
                AutoSize = true
            };
            string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FARO Projects");
            _txtBaseDirectory = new TextBox
            {
                Text = defaultDir,
                Font = new Font("Segoe UI", 9f),
                Location = new Point(130, 65),
                Size = new Size(315, 23)
            };

            var btnBrowse = new Button
            {
                Text = "Browse...",
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(452, 64),
                Size = new Size(83, 25),
                UseVisualStyleBackColor = true,
                Cursor = Cursors.Hand
            };
            btnBrowse.Click += (s, e) =>
            {
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = _txtBaseDirectory.Text;
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        _txtBaseDirectory.Text = fbd.SelectedPath;
                    }
                }
            };

            var lblHint = new Label
            {
                Text = "Sub-folders (Scans, Exports, Reports) will be created automatically.",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Italic),
                ForeColor = Color.FromArgb(120, 130, 145),
                Location = new Point(130, 95),
                AutoSize = true
            };

            grpProject.Controls.Add(lblProjName);
            grpProject.Controls.Add(_txtProjectName);
            grpProject.Controls.Add(lblProjDir);
            grpProject.Controls.Add(_txtBaseDirectory);
            grpProject.Controls.Add(btnBrowse);
            grpProject.Controls.Add(lblHint);

            y += 140;

            // Group 2: Registration & Processing Configuration
            var grpSettings = new GroupBox
            {
                Text = "Automated Pipeline Settings",
                Location = new Point(24, y),
                Size = new Size(556, 160),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 50, 65)
            };

            var lblTargetType = new Label
            {
                Text = "Reference Target:",
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(16, 32),
                AutoSize = true
            };

            _cmbTargetType = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f),
                Location = new Point(140, 29),
                Size = new Size(395, 23)
            };
            _cmbTargetType.Items.Add("140mm Reference Spheres (Radius 70mm - Standard FARO)");
            _cmbTargetType.Items.Add("200mm Reference Spheres (Radius 100mm)");
            _cmbTargetType.Items.Add("Checkerboard Target A4 (Black & White)");
            _cmbTargetType.SelectedIndex = 0;
            _cmbTargetType.SelectedIndexChanged += (s, e) =>
            {
                if (_cmbTargetType.SelectedIndex == 0) SelectedTargetType = TargetType.Sphere140mm;
                else if (_cmbTargetType.SelectedIndex == 1) SelectedTargetType = TargetType.Sphere200mm;
                else SelectedTargetType = TargetType.CheckerboardA4;
            };

            _chkStrayFilter = new CheckBox
            {
                Text = "Apply Stray Point & Range Filtering (0.5m - 65m)",
                Checked = true,
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(140, 65),
                AutoSize = true
            };

            _chkRegistration = new CheckBox
            {
                Text = "Run Target-Based Registration (Kabsch SVD Rigid 6-DOF)",
                Checked = true,
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(140, 92),
                AutoSize = true
            };

            _chkTopView = new CheckBox
            {
                Text = "Show Bird's Eye (Top View) Verification Map upon Completion",
                Checked = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(16, 124, 65),
                Location = new Point(140, 120),
                AutoSize = true
            };

            grpSettings.Controls.Add(lblTargetType);
            grpSettings.Controls.Add(_cmbTargetType);
            grpSettings.Controls.Add(_chkStrayFilter);
            grpSettings.Controls.Add(_chkRegistration);
            grpSettings.Controls.Add(_chkTopView);

            // Bottom Buttons
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.White,
                Padding = new Padding(20, 12, 20, 12)
            };

            _btnStart = new Button
            {
                Text = "Create Project & Run Workflow",
                DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Size = new Size(240, 36),
                Location = new Point(footer.Width - 260, 12),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            _btnStart.FlatAppearance.BorderSize = 0;

            _btnCancel = new Button
            {
                Text = "Ignore / Cancel",
                DialogResult = DialogResult.Cancel,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(100, 100, 100),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(120, 36),
                Location = new Point(20, 12),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(210, 215, 225);

            footer.Controls.Add(_btnStart);
            footer.Controls.Add(_btnCancel);

            content.Controls.Add(grpProject);
            content.Controls.Add(grpSettings);

            Controls.Add(content);
            Controls.Add(footer);
            Controls.Add(header);
        }
    }
}
