using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FAROSceneSDCardWorkflow.Registration;

namespace FAROSceneSDCardWorkflow.UI
{
    /// <summary>
    /// Interactive dialog displaying the Bird's Eye (Top View) registration verification map,
    /// target tension residuals table, station alignment metrics, and final approval button.
    /// </summary>
    public class TopViewVerificationDialog : Form
    {
        private readonly RegistrationResult _result;
        private readonly string _projectPath;

        private TopViewCanvas _canvas;
        private DataGridView _gridTargets;
        private DataGridView _gridStations;
        private Label _lblSummary;
        private Label _lblBadge;
        private Button _btnApprove;
        private Button _btnExport;
        private Button _btnCancel;

        public bool Approved { get; private set; } = false;

        public TopViewVerificationDialog(RegistrationResult result, string projectPath)
        {
            _result = result ?? throw new ArgumentNullException(nameof(result));
            _projectPath = projectPath;

            InitializeComponent();
            PopulateData();
        }

        private void InitializeComponent()
        {
            Text = "FARO SCENE - Registration Top View Verification";
            Width = 1180;
            Height = 780;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(245, 246, 248);
            Icon = SystemIcons.Application;

            // Header Banner
            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.FromArgb(32, 36, 44),
                Padding = new Padding(16, 10, 16, 10)
            };

            var lblTitle = new Label
            {
                Text = "REGISTRATION VERIFICATION (TOP VIEW)",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            };

            _lblSummary = new Label
            {
                Text = "Review alignment of scanner stations, detected targets, and inter-station tension residuals.",
                ForeColor = Color.FromArgb(180, 190, 205),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(17, 38),
                AutoSize = true
            };

            _lblBadge = new Label
            {
                Text = "QUALITY: EXCELLENT",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(16, 124, 65), // Green
                Padding = new Padding(8, 5, 8, 5),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(headerPanel.Width - 220, 18)
            };

            headerPanel.Controls.Add(lblTitle);
            headerPanel.Controls.Add(_lblSummary);
            headerPanel.Controls.Add(_lblBadge);

            // Bottom Action Footer
            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.White,
                Padding = new Padding(16, 12, 16, 12)
            };

            _btnApprove = new Button
            {
                Text = "✓ Approve & Complete Process",
                DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Width = 220,
                Height = 36,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(footerPanel.Width - 236, 12),
                Cursor = Cursors.Hand
            };
            _btnApprove.FlatAppearance.BorderSize = 0;
            _btnApprove.Click += (s, e) =>
            {
                Approved = true;
                Close();
            };

            _btnExport = new Button
            {
                Text = "Export Report...",
                BackColor = Color.FromArgb(240, 242, 245),
                ForeColor = Color.FromArgb(30, 30, 30),
                FlatStyle = FlatStyle.Flat,
                Width = 130,
                Height = 36,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(footerPanel.Width - 376, 12),
                Cursor = Cursors.Hand
            };
            _btnExport.FlatAppearance.BorderColor = Color.FromArgb(200, 205, 215);
            _btnExport.Click += OnExportReportClick;

            _btnCancel = new Button
            {
                Text = "Reject & Cancel",
                DialogResult = DialogResult.Cancel,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(100, 100, 100),
                FlatStyle = FlatStyle.Flat,
                Width = 110,
                Height = 36,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Location = new Point(16, 12),
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(210, 215, 225);
            _btnCancel.Click += (s, e) =>
            {
                Approved = false;
                Close();
            };

            footerPanel.Controls.Add(_btnApprove);
            footerPanel.Controls.Add(_btnExport);
            footerPanel.Controls.Add(_btnCancel);

            // Split Container: Left is Canvas, Right is Metrics & Residuals Table
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 750,
                SplitterWidth = 5,
                BackColor = Color.FromArgb(220, 224, 230)
            };

            // Left: Canvas & Toolbar
            var leftPanel = new Panel { Dock = DockStyle.Fill };
            var toolBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 34,
                BackColor = Color.FromArgb(40, 44, 52),
                Padding = new Padding(8, 4, 8, 4)
            };

            var chkPoints = new CheckBox
            {
                Text = "Points",
                ForeColor = Color.White,
                Checked = true,
                AutoSize = true,
                Location = new Point(10, 6)
            };
            chkPoints.CheckedChanged += (s, e) =>
            {
                _canvas.ShowPoints = chkPoints.Checked;
                _canvas.Invalidate();
            };

            var chkLinks = new CheckBox
            {
                Text = "Tension Links",
                ForeColor = Color.White,
                Checked = true,
                AutoSize = true,
                Location = new Point(80, 6)
            };
            chkLinks.CheckedChanged += (s, e) =>
            {
                _canvas.ShowTargetLinks = chkLinks.Checked;
                _canvas.Invalidate();
            };

            var chkLabels = new CheckBox
            {
                Text = "Target Labels",
                ForeColor = Color.White,
                Checked = true,
                AutoSize = true,
                Location = new Point(190, 6)
            };
            chkLabels.CheckedChanged += (s, e) =>
            {
                _canvas.ShowTargetLabels = chkLabels.Checked;
                _canvas.Invalidate();
            };

            var btnResetView = new Button
            {
                Text = "Fit View",
                BackColor = Color.FromArgb(60, 66, 78),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(70, 24),
                Location = new Point(310, 4),
                Cursor = Cursors.Hand
            };
            btnResetView.FlatAppearance.BorderSize = 0;
            btnResetView.Click += (s, e) => _canvas.ResetView();

            var lblHint = new Label
            {
                Text = "Pan: Left/Middle Drag | Zoom: Mouse Wheel",
                ForeColor = Color.FromArgb(160, 170, 185),
                Font = new Font("Segoe UI", 8f),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                AutoSize = true,
                Location = new Point(toolBar.Width - 250, 8)
            };

            toolBar.Controls.Add(chkPoints);
            toolBar.Controls.Add(chkLinks);
            toolBar.Controls.Add(chkLabels);
            toolBar.Controls.Add(btnResetView);
            toolBar.Controls.Add(lblHint);

            _canvas = new TopViewCanvas { Dock = DockStyle.Fill };
            leftPanel.Controls.Add(_canvas);
            leftPanel.Controls.Add(toolBar);

            // Right: TabControl (Target Tension Residuals & Station Coordinates)
            var rightTabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f)
            };

            var tabResiduals = new TabPage("Target Tension Residuals");
            _gridTargets = CreateStyledGrid();
            _gridTargets.Columns.Add("TargetId", "Target");
            _gridTargets.Columns.Add("Pair", "Station Pair");
            _gridTargets.Columns.Add("Residual", "Residual (mm)");
            _gridTargets.Columns.Add("Status", "Quality");
            _gridTargets.Columns[0].Width = 75;
            _gridTargets.Columns[1].Width = 130;
            _gridTargets.Columns[2].Width = 95;
            _gridTargets.Columns[3].Width = 80;
            tabResiduals.Controls.Add(_gridTargets);

            var tabStations = new TabPage("Station Transforms");
            _gridStations = CreateStyledGrid();
            _gridStations.Columns.Add("Name", "Scan Name");
            _gridStations.Columns.Add("X", "X (m)");
            _gridStations.Columns.Add("Y", "Y (m)");
            _gridStations.Columns.Add("Z", "Z (m)");
            _gridStations.Columns.Add("Targets", "Targets");
            _gridStations.Columns[0].Width = 95;
            _gridStations.Columns[1].Width = 70;
            _gridStations.Columns[2].Width = 70;
            _gridStations.Columns[3].Width = 70;
            _gridStations.Columns[4].Width = 65;
            tabStations.Controls.Add(_gridStations);

            rightTabs.TabPages.Add(tabResiduals);
            rightTabs.TabPages.Add(tabStations);

            split.Panel1.Controls.Add(leftPanel);
            split.Panel2.Controls.Add(rightTabs);

            Controls.Add(split);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);
        }

        private DataGridView CreateStyledGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                RowTemplate = { Height = 24 }
            };
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 243, 246);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(40, 45, 55);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            grid.ColumnHeadersHeight = 28;
            grid.GridColor = Color.FromArgb(230, 235, 240);
            return grid;
        }

        private void PopulateData()
        {
            // Update Top View Canvas
            _canvas.SetData(_result.Stations, _result.Correspondences);

            // Update Summary Text
            _lblSummary.Text = $"{_result.RegisteredScans}/{_result.TotalScans} Scans Registered | " +
                               $"Mean Error: {_result.MeanTargetErrorMm:F2} mm | " +
                               $"Max Error: {_result.MaxTargetErrorMm:F2} mm | " +
                               $"{_result.Correspondences.Count} Target Correspondences";

            // Update Quality Badge
            if (_result.MeanTargetErrorMm <= 2.0f && _result.RegisteredScans == _result.TotalScans)
            {
                _lblBadge.Text = $"QUALITY: EXCELLENT ({_result.MeanTargetErrorMm:F1}mm)";
                _lblBadge.BackColor = Color.FromArgb(16, 124, 65); // Green
            }
            else if (_result.MeanTargetErrorMm <= 4.0f)
            {
                _lblBadge.Text = $"QUALITY: GOOD ({_result.MeanTargetErrorMm:F1}mm)";
                _lblBadge.BackColor = Color.FromArgb(216, 140, 0); // Orange
            }
            else
            {
                _lblBadge.Text = $"QUALITY: REVIEW NEEDED ({_result.MeanTargetErrorMm:F1}mm)";
                _lblBadge.BackColor = Color.FromArgb(232, 17, 35); // Red
            }

            // Populate Correspondences Grid
            _gridTargets.Rows.Clear();
            foreach (var c in _result.Correspondences)
            {
                string status = c.DistanceErrorMm <= 2.0f ? "Pass (<2mm)" : (c.DistanceErrorMm <= 4.0f ? "Acceptable" : "Warning");
                int rowIdx = _gridTargets.Rows.Add(
                    c.TargetInScanA.TargetId,
                    $"{c.TargetInScanA.ScanId} ↔ {c.TargetInScanB.ScanId}",
                    $"{c.DistanceErrorMm:F2}",
                    status
                );

                if (c.DistanceErrorMm > 4.0f)
                {
                    _gridTargets.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.Red;
                }
            }

            // Populate Stations Grid
            _gridStations.Rows.Clear();
            foreach (var st in _result.Stations)
            {
                var pos = st.ScannerPosition;
                _gridStations.Rows.Add(
                    st.Name,
                    $"{pos.X:F3}",
                    $"{pos.Y:F3}",
                    $"{pos.Z:F3}",
                    st.Targets.Count.ToString()
                );
            }
        }

        private void OnExportReportClick(object sender, EventArgs e)
        {
            try
            {
                string reportPath = Path.Combine(_projectPath ?? Path.GetTempPath(), "Reports", "RegistrationReport.txt");
                string reportDir = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(reportDir) && !Directory.Exists(reportDir))
                {
                    Directory.CreateDirectory(reportDir);
                }

                using (var sw = new StreamWriter(reportPath))
                {
                    sw.WriteLine("==========================================================================");
                    sw.WriteLine("               FARO SCENE TARGET-BASED REGISTRATION REPORT                 ");
                    sw.WriteLine("==========================================================================");
                    sw.WriteLine($"Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sw.WriteLine($"Project   : {_projectPath}");
                    sw.WriteLine($"Status    : {_result.QualityRating}");
                    sw.WriteLine($"Scans     : {_result.RegisteredScans}/{_result.TotalScans} Registered");
                    sw.WriteLine($"Mean Err  : {_result.MeanTargetErrorMm:F2} mm");
                    sw.WriteLine($"Max Err   : {_result.MaxTargetErrorMm:F2} mm");
                    sw.WriteLine($"Elapsed   : {_result.Elapsed.TotalSeconds:F2} s");
                    sw.WriteLine("--------------------------------------------------------------------------");
                    sw.WriteLine("SCAN STATIONS:");
                    foreach (var s in _result.Stations)
                    {
                        var p = s.ScannerPosition;
                        sw.WriteLine($"  [{s.ScanId}] {s.Name} => Position: ({p.X:F4}, {p.Y:F4}, {p.Z:F4}) | Targets: {s.Targets.Count}");
                    }
                    sw.WriteLine("--------------------------------------------------------------------------");
                    sw.WriteLine("TARGET CORRESPONDENCE RESIDUALS:");
                    foreach (var c in _result.Correspondences)
                    {
                        sw.WriteLine($"  {c.TargetInScanA.TargetId,-12} | {c.TargetInScanA.ScanId} <-> {c.TargetInScanB.ScanId,-12} | Error: {c.DistanceErrorMm:F2} mm");
                    }
                    sw.WriteLine("==========================================================================");
                }

                MessageBox.Show($"Registration report exported successfully to:\n\n{reportPath}",
                    "Report Exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export report: {ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
