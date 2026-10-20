using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using FARO.LS.SDK.AppLogic;
using FARO.LS.SDK.DataLogic;

namespace FAROSceneHelloWorld.UI
{
    /// <summary>
    /// Interactive diagnostic and control window running inside FARO SCENE.
    /// Demonstrates real-time button clicks, threading, workspace inspection,
    /// and host process diagnostics.
    /// </summary>
    public class InteractiveToolboxWindow : Form
    {
        private readonly IAppHost _host;
        private int _clickCount = 0;
        private BackgroundWorker _simWorker;

        // UI Controls
        private Label _lblStatusBadge;
        private Label _lblClickCounter;
        private Label _lblWorkspaceStatus;
        private Label _lblPointsStatus;
        private ProgressBar _progressBar;
        private ListBox _logListBox;
        private Button _btnPing;
        private Button _btnWorkspace;
        private Button _btnSysInfo;
        private Button _btnSimulate;
        private Button _btnModalStats;
        private Button _btnClearLog;
        private Button _btnCopyLog;

        public InteractiveToolboxWindow(IAppHost host)
        {
            _host = host;
            InitializeComponent();
            LogEvent("INFO", $"Interactive window opened inside {Process.GetCurrentProcess().ProcessName} (PID: {Process.GetCurrentProcess().Id}).");
            LogEvent("INFO", "Add-in lifecycle is active. Click any button below to test responsiveness.");
        }

        private void InitializeComponent()
        {
            this.Text = "FARO SCENE - Add-in Interactive Controller";
            this.Size = new Size(680, 600);
            this.MinimumSize = new Size(600, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.BackColor = Color.FromArgb(245, 247, 250);

            // 1. Top Header Banner (FARO Blue)
            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.FromArgb(24, 76, 120),
                Padding = new Padding(18, 12, 18, 12)
            };

            var lblTitle = new Label
            {
                Text = "FARO SCENE • Add-in Interactive Controller",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 10)
            };

            var proc = Process.GetCurrentProcess();
            string subText = $"Host Process: {proc.ProcessName}.exe (x64, PID: {proc.Id}) • .NET {Environment.Version}";
            var lblSubtitle = new Label
            {
                Text = subText,
                ForeColor = Color.FromArgb(200, 225, 250),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(18, 38)
            };

            headerPanel.Controls.Add(lblTitle);
            headerPanel.Controls.Add(lblSubtitle);
            this.Controls.Add(headerPanel);

            // 2. Metrics & Status Cards Panel
            var cardPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 90,
                Padding = new Padding(15, 10, 15, 5),
                BackColor = Color.FromArgb(245, 247, 250)
            };

            var innerCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(15, 10, 15, 10)
            };

            _lblStatusBadge = new Label
            {
                Text = "● Add-in Status: Active & Responding",
                ForeColor = Color.FromArgb(16, 137, 62), // Green
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(15, 10),
                AutoSize = true
            };

            _lblClickCounter = new Label
            {
                Text = "Button Clicks: 0",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Location = new Point(340, 10),
                AutoSize = true
            };

            _lblWorkspaceStatus = new Label
            {
                Text = "Workspace: Query pending...",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(80, 80, 80),
                Location = new Point(15, 36),
                AutoSize = true
            };

            _lblPointsStatus = new Label
            {
                Text = "Active Points: Ready to inspect",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(80, 80, 80),
                Location = new Point(340, 36),
                AutoSize = true
            };

            innerCard.Controls.Add(_lblStatusBadge);
            innerCard.Controls.Add(_lblClickCounter);
            innerCard.Controls.Add(_lblWorkspaceStatus);
            innerCard.Controls.Add(_lblPointsStatus);
            cardPanel.Controls.Add(innerCard);
            this.Controls.Add(cardPanel);

            // 3. Action Buttons Grid
            var actionPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 90,
                Padding = new Padding(15, 5, 15, 5),
                BackColor = Color.FromArgb(245, 247, 250)
            };

            var flowButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false
            };

            _btnPing = CreateActionButton("🖱️ Click Me / Ping", Color.FromArgb(0, 120, 215), (s, e) => OnPingClicked());
            _btnWorkspace = CreateActionButton("📁 Query Workspace", Color.FromArgb(43, 87, 154), (s, e) => OnQueryWorkspaceClicked());
            _btnSysInfo = CreateActionButton("💻 System Info", Color.FromArgb(70, 70, 70), (s, e) => OnSystemInfoClicked());
            _btnSimulate = CreateActionButton("⚡ Background Task", Color.FromArgb(180, 80, 0), (s, e) => OnSimulateTaskClicked());
            _btnModalStats = CreateActionButton("📊 Scan Stats Dialog", Color.FromArgb(0, 130, 130), (s, e) => OnModalStatsClicked());

            flowButtons.Controls.Add(_btnPing);
            flowButtons.Controls.Add(_btnWorkspace);
            flowButtons.Controls.Add(_btnSysInfo);
            flowButtons.Controls.Add(_btnSimulate);
            flowButtons.Controls.Add(_btnModalStats);
            actionPanel.Controls.Add(flowButtons);
            this.Controls.Add(actionPanel);

            // 4. Progress Bar for Background Task
            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 12,
                Style = ProgressBarStyle.Blocks,
                Visible = false
            };
            this.Controls.Add(_progressBar);

            // 5. Console / Event Log Area
            var logContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(15, 5, 15, 10)
            };

            var lblLogHeader = new Label
            {
                Text = "Live Interaction & Event Log:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 22
            };

            _logListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8.5F, FontStyle.Regular),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };

            logContainer.Controls.Add(_logListBox);
            logContainer.Controls.Add(lblLogHeader);
            this.Controls.Add(logContainer);

            // 6. Bottom Bar with Clear & Copy Buttons
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                BackColor = Color.FromArgb(235, 238, 242),
                Padding = new Padding(15, 8, 15, 8)
            };

            _btnClearLog = new Button
            {
                Text = "Clear Log",
                Size = new Size(95, 28),
                Location = new Point(15, 8),
                UseVisualStyleBackColor = true
            };
            _btnClearLog.Click += (s, e) =>
            {
                _logListBox.Items.Clear();
                LogEvent("INFO", "Log cleared.");
            };

            _btnCopyLog = new Button
            {
                Text = "Copy Log",
                Size = new Size(95, 28),
                Location = new Point(118, 8),
                UseVisualStyleBackColor = true
            };
            _btnCopyLog.Click += (s, e) =>
            {
                var sb = new StringBuilder();
                foreach (var item in _logListBox.Items)
                {
                    sb.AppendLine(item.ToString());
                }
                if (sb.Length > 0)
                {
                    Clipboard.SetText(sb.ToString());
                    LogEvent("SUCCESS", "Log copied to system clipboard.");
                }
            };

            var btnClose = new Button
            {
                Text = "Close",
                Size = new Size(85, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(bottomPanel.Width - 100, 8),
                UseVisualStyleBackColor = true
            };
            btnClose.Click += (s, e) => this.Close();

            bottomPanel.Controls.Add(_btnClearLog);
            bottomPanel.Controls.Add(_btnCopyLog);
            bottomPanel.Controls.Add(btnClose);
            this.Controls.Add(bottomPanel);
        }

        private Button CreateActionButton(string text, Color bg, EventHandler onClick)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(150, 34),
                BackColor = bg,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(4)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += onClick;
            return btn;
        }

        #region Event Handlers

        private void OnPingClicked()
        {
            _clickCount++;
            _lblClickCounter.Text = $"Button Clicks: {_clickCount}";
            _lblStatusBadge.Text = $"● Add-in Status: Active (Last Click: {DateTime.Now:HH:mm:ss})";

            LogEvent("CLICK", $"Ping button clicked! Current counter: {_clickCount}");
        }

        private void OnQueryWorkspaceClicked()
        {
            LogEvent("QUERY", "Querying FARO SCENE WorkspaceHost...");

            try
            {
                var wsHost = _host?.WorkspaceHost;
                if (wsHost == null)
                {
                    _lblWorkspaceStatus.Text = "Workspace: Host interface not attached";
                    LogEvent("WARN", "WorkspaceHost is null in the current context.");
                    return;
                }

                if (!wsHost.HasActiveWorkspace || wsHost.ActiveWorkspace == null)
                {
                    _lblWorkspaceStatus.Text = "Workspace: None open in SCENE";
                    _lblPointsStatus.Text = "Active Points: 0";
                    LogEvent("INFO", "No active workspace currently open in SCENE. (Open a .lsproj to see scan stats)");
                    return;
                }

                var ws = wsHost.ActiveWorkspace;
                string name = ws.Name ?? "Untitled";
                string path = ws.FilePath ?? "Unsaved";
                int totalScans = 0;
                int loadedScans = 0;
                long totalPoints = 0;

                if (ws.Scans != null)
                {
                    foreach (var scan in ws.Scans)
                    {
                        if (scan == null) continue;
                        totalScans++;
                        if (scan.IsLoaded)
                        {
                            loadedScans++;
                            totalPoints += scan.NumberOfPoints;
                            LogEvent("SCAN", $"  Loaded scan '{scan.Name}': {scan.NumberOfPoints:N0} points");
                        }
                    }
                }

                _lblWorkspaceStatus.Text = $"Workspace: {name} ({loadedScans}/{totalScans} loaded)";
                _lblPointsStatus.Text = $"Active Points: {totalPoints:N0} pts";

                LogEvent("SUCCESS", $"Workspace '{name}' queried: {loadedScans}/{totalScans} scans loaded, {totalPoints:N0} points total.");
            }
            catch (Exception ex)
            {
                LogEvent("ERROR", $"Failed to query workspace: {ex.Message}");
            }
        }

        private void OnSystemInfoClicked()
        {
            try
            {
                var proc = Process.GetCurrentProcess();
                proc.Refresh();

                long memMb = proc.WorkingSet64 / (1024 * 1024);
                int threadCount = proc.Threads.Count;
                string os = Environment.OSVersion.ToString();
                string clr = Environment.Version.ToString();
                string appDir = Path.GetDirectoryName(typeof(InteractiveToolboxWindow).Assembly.Location);

                LogEvent("SYSTEM", $"=== Host Process Diagnostics ===");
                LogEvent("SYSTEM", $"  Process Name   : {proc.ProcessName} (x64: {Environment.Is64BitProcess})");
                LogEvent("SYSTEM", $"  Process ID     : {proc.Id}");
                LogEvent("SYSTEM", $"  Memory In-Use  : {memMb} MB Working Set");
                LogEvent("SYSTEM", $"  Thread Count   : {threadCount} threads");
                LogEvent("SYSTEM", $"  CLR Version    : {clr}");
                LogEvent("SYSTEM", $"  Add-in Path    : {appDir}");
            }
            catch (Exception ex)
            {
                LogEvent("ERROR", $"System info query failed: {ex.Message}");
            }
        }

        private void OnSimulateTaskClicked()
        {
            if (_simWorker != null && _simWorker.IsBusy)
            {
                LogEvent("WARN", "A background task is already executing.");
                return;
            }

            _progressBar.Value = 0;
            _progressBar.Visible = true;
            _btnSimulate.Enabled = false;

            LogEvent("TASK", "Starting background worker thread (simulating scan processing)...");

            _simWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true
            };

            _simWorker.DoWork += (s, e) =>
            {
                for (int i = 1; i <= 100; i += 10)
                {
                    Thread.Sleep(80); // Simulate asynchronous compute
                    _simWorker.ReportProgress(i);
                }
            };

            _simWorker.ProgressChanged += (s, e) =>
            {
                _progressBar.Value = Math.Min(100, e.ProgressPercentage);
            };

            _simWorker.RunWorkerCompleted += (s, e) =>
            {
                _progressBar.Visible = false;
                _btnSimulate.Enabled = true;
                LogEvent("SUCCESS", "Background processing task finished successfully (UI remained fully responsive).");
            };

            _simWorker.RunWorkerAsync();
        }

        private void OnModalStatsClicked()
        {
            LogEvent("UI", "Opening modal Scan Statistics dialog...");
            try
            {
                var cmd = new Commands.CountPointsCommand(_host);
                cmd.Execute(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogEvent("ERROR", $"Failed to open stats dialog: {ex.Message}");
            }
        }

        private void LogEvent(string level, string message)
        {
            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            string entry = $"[{time}] [{level}] {message}";

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() =>
                {
                    _logListBox.Items.Add(entry);
                    _logListBox.TopIndex = _logListBox.Items.Count - 1;
                }));
            }
            else
            {
                _logListBox.Items.Add(entry);
                _logListBox.TopIndex = _logListBox.Items.Count - 1;
            }
        }

        #endregion
    }
}
