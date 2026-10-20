using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace FAROSceneHelloWorld.UI
{
    /// <summary>
    /// Information model for individual scan statistics.
    /// </summary>
    public class ScanItemInfo
    {
        public string Name { get; set; }
        public bool IsLoaded { get; set; }
        public long PointCount { get; set; }
    }

    /// <summary>
    /// Clean, production-ready modal dialog displaying workspace scan point statistics.
    /// Follows FARO SCENE desktop UI aesthetics with high-DPI Segoe UI font styling.
    /// </summary>
    public class StatsDialog : Form
    {
        private readonly string _workspaceName;
        private readonly string _workspacePath;
        private readonly List<ScanItemInfo> _scans;
        private readonly long _totalActivePoints;
        private readonly int _loadedScanCount;

        public StatsDialog(string workspaceName, string workspacePath, List<ScanItemInfo> scans, long totalActivePoints, int loadedScanCount)
        {
            _workspaceName = workspaceName ?? "Unknown Workspace";
            _workspacePath = workspacePath ?? "Unsaved";
            _scans = scans ?? new List<ScanItemInfo>();
            _totalActivePoints = totalActivePoints;
            _loadedScanCount = loadedScanCount;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "FARO SCENE - Workspace Scan Statistics";
            this.Size = new Size(580, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.BackColor = Color.FromArgb(245, 247, 250);

            // Header Banner
            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(24, 76, 120), // FARO Corporate Blue
                Padding = new Padding(15, 10, 15, 10)
            };

            var lblHeaderTitle = new Label
            {
                Text = "FARO SCENE Workspace Statistics",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(15, 10)
            };

            var lblHeaderSubtitle = new Label
            {
                Text = $"Workspace: {_workspaceName}",
                ForeColor = Color.FromArgb(210, 230, 250),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(16, 35)
            };

            headerPanel.Controls.Add(lblHeaderTitle);
            headerPanel.Controls.Add(lblHeaderSubtitle);
            this.Controls.Add(headerPanel);

            // Summary Card Panel
            var summaryPanel = new Panel
            {
                Location = new Point(15, 75),
                Size = new Size(535, 95),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblTotalScans = new Label
            {
                Text = $"Total Scans: {_scans.Count}",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(15, 12),
                AutoSize = true
            };

            var lblLoadedScans = new Label
            {
                Text = $"Open / Loaded Scans: {_loadedScanCount}  (Unloaded: {_scans.Count - _loadedScanCount})",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                Location = new Point(15, 36),
                AutoSize = true
            };

            var lblActivePoints = new Label
            {
                Text = $"Active Scan Points: {_totalActivePoints:N0} pts",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 110, 180),
                Location = new Point(15, 60),
                AutoSize = true
            };

            summaryPanel.Controls.Add(lblTotalScans);
            summaryPanel.Controls.Add(lblLoadedScans);
            summaryPanel.Controls.Add(lblActivePoints);
            this.Controls.Add(summaryPanel);

            // Details List View
            var lblDetails = new Label
            {
                Text = "Scan Breakdown:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(15, 178),
                AutoSize = true
            };
            this.Controls.Add(lblDetails);

            var listView = new ListView
            {
                Location = new Point(15, 200),
                Size = new Size(535, 205),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            listView.Columns.Add("Scan Name", 240);
            listView.Columns.Add("Status", 100);
            listView.Columns.Add("Points", 170, HorizontalAlignment.Right);

            foreach (var scan in _scans)
            {
                var item = new ListViewItem(scan.Name);
                item.SubItems.Add(scan.IsLoaded ? "Loaded" : "Unloaded");
                item.SubItems.Add(scan.IsLoaded ? $"{scan.PointCount:N0}" : "N/A (Unloaded)");
                if (!scan.IsLoaded)
                {
                    item.ForeColor = Color.Gray;
                }
                listView.Items.Add(item);
            }

            this.Controls.Add(listView);

            // Bottom Buttons Panel
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = Color.FromArgb(240, 242, 245)
            };

            var btnCopy = new Button
            {
                Text = "Copy to Clipboard",
                Size = new Size(130, 30),
                Location = new Point(15, 10),
                UseVisualStyleBackColor = true
            };
            btnCopy.Click += (s, e) =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== FARO SCENE Workspace Statistics ===");
                sb.AppendLine($"Workspace: {_workspaceName}");
                sb.AppendLine($"File Path: {_workspacePath}");
                sb.AppendLine($"Total Scans: {_scans.Count}");
                sb.AppendLine($"Open / Loaded Scans: {_loadedScanCount}");
                sb.AppendLine($"Total Active Points: {_totalActivePoints:N0}");
                sb.AppendLine();
                sb.AppendLine("--- Scans ---");
                foreach (var scan in _scans)
                {
                    sb.AppendLine($"  - {scan.Name}: {(scan.IsLoaded ? $"{scan.PointCount:N0} pts" : "Unloaded")}");
                }
                Clipboard.SetText(sb.ToString());
                MessageBox.Show(this, "Statistics copied to clipboard successfully.", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            var btnClose = new Button
            {
                Text = "Close",
                Size = new Size(90, 30),
                Location = new Point(460, 10),
                DialogResult = DialogResult.OK,
                UseVisualStyleBackColor = true
            };
            this.AcceptButton = btnClose;

            bottomPanel.Controls.Add(btnCopy);
            bottomPanel.Controls.Add(btnClose);
            this.Controls.Add(bottomPanel);
        }
    }
}
