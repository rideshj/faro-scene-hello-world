using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FAROSceneSDCardWorkflow.Hardware;
using FAROSceneSDCardWorkflow.Workflow;

namespace FAROSceneSDCardWorkflow.UI
{
    /// <summary>
    /// Modeless Workflow Control Center allowing users to monitor SD card insertion status,
    /// simulate hardware events, inspect diagnostic logs, and view last registration results.
    /// </summary>
    public class WorkflowDashboardWindow : Form
    {
        private readonly AutoWorkflowCoordinator _coordinator;

        private Label _lblWatcherStatus;
        private Label _lblDriveCount;
        private RichTextBox _rtbLog;
        private Button _btnSimulate;
        private Button _btnManualFolder;
        private Button _btnViewLastTopView;

        public WorkflowDashboardWindow(AutoWorkflowCoordinator coordinator)
        {
            _coordinator = coordinator ?? AutoWorkflowCoordinator.Instance;

            InitializeComponent();

            _coordinator.LogMessage += OnLogMessageReceived;
            FormClosed += (s, e) => _coordinator.LogMessage -= OnLogMessageReceived;

            RefreshDriveStatus();
        }

        private void InitializeComponent()
        {
            Text = "FARO SCENE - SD Card Automation Center";
            Width = 720;
            Height = 560;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(245, 246, 248);

            // Banner
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 75,
                BackColor = Color.FromArgb(24, 76, 120),
                Padding = new Padding(20, 12, 20, 12)
            };

            var lblTitle = new Label
            {
                Text = "SD CARD AUTOMATION & REGISTRATION CENTER",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 14),
                AutoSize = true
            };

            var lblSub = new Label
            {
                Text = "Automated Hardware Ingestion, Stray Point Filtering, Target Registration & Top-View Verification.",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(200, 225, 255),
                Location = new Point(21, 42),
                AutoSize = true
            };

            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSub);

            // Center Panel
            var content = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 14, 20, 14)
            };

            // Group: Watcher Status & Hardware
            var grpStatus = new GroupBox
            {
                Text = "Hardware SD Card Monitor",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 50, 65),
                Location = new Point(20, 14),
                Size = new Size(660, 95)
            };

            _lblWatcherStatus = new Label
            {
                Text = "Status: ACTIVE (Polling removable storage every 1.5s)",
                ForeColor = Color.FromArgb(16, 124, 65), // Green
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(20, 28),
                AutoSize = true
            };

            _lblDriveCount = new Label
            {
                Text = "Removable Drives Detected: Scanning...",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(70, 80, 95),
                Location = new Point(20, 54),
                AutoSize = true
            };

            var btnRefreshDrives = new Button
            {
                Text = "Refresh Drives",
                Font = new Font("Segoe UI", 8.5f),
                Size = new Size(110, 26),
                Location = new Point(530, 26),
                Cursor = Cursors.Hand
            };
            btnRefreshDrives.Click += (s, e) => RefreshDriveStatus();

            grpStatus.Controls.Add(_lblWatcherStatus);
            grpStatus.Controls.Add(_lblDriveCount);
            grpStatus.Controls.Add(btnRefreshDrives);

            // Group: Quick Actions & Simulation
            var grpActions = new GroupBox
            {
                Text = "Actions & Hardware Simulation",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 50, 65),
                Location = new Point(20, 120),
                Size = new Size(660, 80)
            };

            _btnSimulate = new Button
            {
                Text = "▶ Simulate SD Card (3 Scans)",
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Size = new Size(210, 34),
                Location = new Point(20, 28),
                Cursor = Cursors.Hand
            };
            _btnSimulate.FlatAppearance.BorderSize = 0;
            _btnSimulate.Click += (s, e) =>
            {
                _coordinator.SimulateSDCardInsertion();
            };

            _btnManualFolder = new Button
            {
                Text = "📁 Ingest Local Folder...",
                Font = new Font("Segoe UI", 8.5f),
                Size = new Size(170, 34),
                Location = new Point(245, 28),
                Cursor = Cursors.Hand
            };
            _btnManualFolder.Click += OnIngestLocalFolder;

            _btnViewLastTopView = new Button
            {
                Text = "🗺 View Last Top View Map",
                Font = new Font("Segoe UI", 8.5f),
                Size = new Size(190, 34),
                Location = new Point(430, 28),
                Cursor = Cursors.Hand
            };
            _btnViewLastTopView.Click += OnViewLastTopView;

            grpActions.Controls.Add(_btnSimulate);
            grpActions.Controls.Add(_btnManualFolder);
            grpActions.Controls.Add(_btnViewLastTopView);

            // Group: Real-time Activity Log
            var grpLog = new GroupBox
            {
                Text = "Event & Activity Log",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 50, 65),
                Location = new Point(20, 212),
                Size = new Size(660, 280),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            _rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 30, 36),
                ForeColor = Color.FromArgb(220, 225, 235),
                Font = new Font("Consolas", 8.5f),
                ReadOnly = true,
                BorderStyle = BorderStyle.None
            };

            var logToolPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                Padding = new Padding(4)
            };

            var btnClearLog = new Button
            {
                Text = "Clear Log",
                Size = new Size(80, 24),
                Location = new Point(570, 3),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Font = new Font("Segoe UI", 8f),
                Cursor = Cursors.Hand
            };
            btnClearLog.Click += (s, e) => _rtbLog.Clear();

            logToolPanel.Controls.Add(btnClearLog);
            grpLog.Controls.Add(_rtbLog);
            grpLog.Controls.Add(logToolPanel);

            content.Controls.Add(grpStatus);
            content.Controls.Add(grpActions);
            content.Controls.Add(grpLog);

            Controls.Add(content);
            Controls.Add(header);
        }

        private void RefreshDriveStatus()
        {
            try
            {
                var drives = DriveInfo.GetDrives();
                int removable = 0;
                string details = "";

                foreach (var d in drives)
                {
                    if (d.DriveType == DriveType.Removable && d.IsReady)
                    {
                        removable++;
                        details += $"{d.Name} ({d.VolumeLabel}, {d.TotalSize / (1024 * 1024 * 1024)}GB)  ";
                    }
                }

                _lblDriveCount.Text = removable > 0
                    ? $"Removable Drives: {removable} attached => {details}"
                    : "Removable Drives: None currently inserted. Ready for SD Card plug-in.";
            }
            catch (Exception ex)
            {
                _lblDriveCount.Text = $"Drive scan error: {ex.Message}";
            }
        }

        private void OnLogMessageReceived(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnLogMessageReceived(message)));
                return;
            }

            _rtbLog.AppendText(message + "\n");
            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.ScrollToCaret();
        }

        private void OnIngestLocalFolder(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select Folder Containing FARO Scan Files (*.fls, *.pts, *.xyz)";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    var card = SDCardWatcher.InspectFolder(fbd.SelectedPath);
                    if (card == null || card.ScanCount == 0)
                    {
                        MessageBox.Show("No supported scan files (.fls, .pts, .xyz) found in selected directory.",
                            "No Scans Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    _coordinator.PromptAndRunWorkflow(card);
                }
            }
        }

        private void OnViewLastTopView(object sender, EventArgs e)
        {
            if (_coordinator.LastRegistrationResult == null)
            {
                MessageBox.Show("No registration has been executed yet in this session.\n\nPlease insert an SD card or click 'Simulate SD Card' to run a registration.",
                    "No Data", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new TopViewVerificationDialog(_coordinator.LastRegistrationResult, _coordinator.LastProjectPath))
            {
                dlg.ShowDialog(this);
            }
        }
    }
}
