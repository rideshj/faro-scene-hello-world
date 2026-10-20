using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using FARO.LS.SDK.AppLogic;
using FAROSceneHelloWorld.Classification;
using FAROSceneHelloWorld.Geometry;

namespace FAROSceneHelloWorld.UI
{
    /// <summary>
    /// Interactive dialog displaying real-time extraction progress and hierarchical
    /// cluster results for structural and MEP elements.
    /// Supports direct real scan file loading (.pts, .xyz, .txt), active SCENE workspace querying,
    /// and explicit demo benchmark mode.
    /// </summary>
    public class ClassificationProgressDialog : Form
    {
        private readonly IAppHost _host;
        private PointCloud _currentCloud;
        private string _currentSourceDesc;

        // UI Controls
        private Label _lblStatus;
        private Label _lblSubStatus;
        private Label _lblSourceBadge;
        private Label _lblSourceDetails;
        private ProgressBar _progressBar;
        private TreeView _treeResults;
        private Button _btnLoadFile;
        private Button _btnQueryScene;
        private Button _btnDemoBenchmark;
        private Button _btnRun;
        private Button _btnExportDxf;
        private Button _btnExportPts;
        private Button _btnClose;

        private ClassificationResult _lastResult;

        public ClassificationProgressDialog(IAppHost host)
        {
            _host = host;
            InitializeComponent();
            CheckInitialSource();
        }

        private void InitializeComponent()
        {
            this.Text = "FARO SCENE - Structural & MEP Element Auto-Classifier";
            this.Size = new Size(720, 650);
            this.MinimumSize = new Size(640, 520);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.BackColor = Color.FromArgb(245, 247, 250);

            // 1. Header Banner
            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(24, 76, 120),
                Padding = new Padding(15, 10, 15, 10)
            };

            var lblTitle = new Label
            {
                Text = "Automated Structural & MEP Classification",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(15, 10)
            };

            var lblSubtitle = new Label
            {
                Text = "Spatial Voxel Grid • PCA Normal Estimation • RANSAC Plane & Cylinder Extraction",
                ForeColor = Color.FromArgb(200, 225, 250),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(16, 34)
            };

            headerPanel.Controls.Add(lblTitle);
            headerPanel.Controls.Add(lblSubtitle);
            this.Controls.Add(headerPanel);

            // 2. Data Source Selector & Information Card
            var sourcePanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 90,
                Padding = new Padding(15, 8, 15, 5),
                BackColor = Color.FromArgb(245, 247, 250)
            };

            var innerSourceCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(10, 8, 10, 8)
            };

            var btnFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            _btnLoadFile = new Button
            {
                Text = "📂 Open Scan File (.pts, .xyz)...",
                Size = new Size(205, 28),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0)
            };
            _btnLoadFile.FlatAppearance.BorderSize = 0;
            _btnLoadFile.Click += (s, e) => OnOpenFileClicked();

            _btnQueryScene = new Button
            {
                Text = "🔄 SCENE Active Scan",
                Size = new Size(160, 28),
                BackColor = Color.FromArgb(43, 87, 154),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0)
            };
            _btnQueryScene.FlatAppearance.BorderSize = 0;
            _btnQueryScene.Click += (s, e) => OnQuerySceneClicked();

            _btnDemoBenchmark = new Button
            {
                Text = "🧪 Load Demo Benchmark",
                Size = new Size(175, 28),
                BackColor = Color.FromArgb(100, 100, 100),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 0, 0)
            };
            _btnDemoBenchmark.FlatAppearance.BorderSize = 0;
            _btnDemoBenchmark.Click += (s, e) => OnLoadBenchmarkClicked();

            btnFlow.Controls.Add(_btnLoadFile);
            btnFlow.Controls.Add(_btnQueryScene);
            btnFlow.Controls.Add(_btnDemoBenchmark);

            _lblSourceBadge = new Label
            {
                Text = "Data Source: Checking SCENE workspace...",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 40, 40),
                Location = new Point(10, 40),
                AutoSize = true
            };

            _lblSourceDetails = new Label
            {
                Text = "Choose a point cloud file (.pts/.xyz) or query the active SCENE scan to begin.",
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(110, 110, 110),
                Location = new Point(11, 60),
                AutoSize = true
            };

            innerSourceCard.Controls.Add(_lblSourceDetails);
            innerSourceCard.Controls.Add(_lblSourceBadge);
            innerSourceCard.Controls.Add(btnFlow);
            sourcePanel.Controls.Add(innerSourceCard);
            this.Controls.Add(sourcePanel);

            // 3. Progress Section
            var progressPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                Padding = new Padding(15, 6, 15, 4),
                BackColor = Color.FromArgb(245, 247, 250)
            };

            _lblStatus = new Label
            {
                Text = "Ready to analyze point cloud geometry.",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(15, 5),
                AutoSize = true
            };

            _lblSubStatus = new Label
            {
                Text = "Select your scan data source above and click 'Run Classification'.",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 100, 100),
                Location = new Point(15, 23),
                AutoSize = true
            };

            _progressBar = new ProgressBar
            {
                Location = new Point(15, 42),
                Size = new Size(675, 14),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Style = ProgressBarStyle.Continuous
            };

            progressPanel.Controls.Add(_lblStatus);
            progressPanel.Controls.Add(_lblSubStatus);
            progressPanel.Controls.Add(_progressBar);
            this.Controls.Add(progressPanel);

            // 4. TreeView for Hierarchical Cluster Results
            var resultsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(15, 4, 15, 8)
            };

            var lblTreeHeader = new Label
            {
                Text = "Detected Elements & SCENE Project Clusters:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 22
            };

            _treeResults = new TreeView
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                BorderStyle = BorderStyle.FixedSingle,
                ShowNodeToolTips = true
            };

            resultsPanel.Controls.Add(_treeResults);
            resultsPanel.Controls.Add(lblTreeHeader);
            this.Controls.Add(resultsPanel);

            // 5. Bottom Action Bar
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                BackColor = Color.FromArgb(235, 238, 242),
                Padding = new Padding(15, 9, 15, 9)
            };

            _btnRun = new Button
            {
                Text = "▶ Run Classification",
                Size = new Size(160, 30),
                Location = new Point(15, 9),
                BackColor = Color.FromArgb(16, 124, 65), // Green
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnRun.FlatAppearance.BorderSize = 0;
            _btnRun.Click += async (s, e) => await StartClassificationAsync();

            _btnExportDxf = new Button
            {
                Text = "Export 3D DXF",
                Size = new Size(115, 30),
                Location = new Point(185, 9),
                Enabled = false,
                UseVisualStyleBackColor = true
            };
            _btnExportDxf.Click += (s, e) => ExportDxf();

            _btnExportPts = new Button
            {
                Text = "Export PTS Points",
                Location = new Point(308, 9),
                Size = new Size(125, 30),
                Enabled = false,
                UseVisualStyleBackColor = true
            };
            _btnExportPts.Click += (s, e) => ExportPts();

            _btnClose = new Button
            {
                Text = "Close",
                Size = new Size(85, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(bottomPanel.Width - 100, 9),
                UseVisualStyleBackColor = true
            };
            _btnClose.Click += (s, e) => this.Close();

            bottomPanel.Controls.Add(_btnRun);
            bottomPanel.Controls.Add(_btnExportDxf);
            bottomPanel.Controls.Add(_btnExportPts);
            bottomPanel.Controls.Add(_btnClose);
            this.Controls.Add(bottomPanel);
        }

        private void CheckInitialSource()
        {
            var cloud = ScanDataLoader.TryLoadActiveScanPoints(_host, out string sourceDesc, out string diagnostic);
            if (cloud != null && cloud.Count > 0)
            {
                _currentCloud = cloud;
                _currentSourceDesc = sourceDesc;
                _lblSourceBadge.Text = $"Data Source: {sourceDesc}";
                _lblSourceBadge.ForeColor = Color.FromArgb(16, 124, 65);
                _lblSourceDetails.Text = $"Points: {cloud.Count:N0} | Bounds: Min={cloud.BoundingMin:F2}, Max={cloud.BoundingMax:F2}";
                _lblStatus.Text = $"Active scan loaded ({cloud.Count:N0} points). Ready to classify.";
            }
            else
            {
                _lblSourceBadge.Text = "Data Source: No active scan loaded in SCENE";
                _lblSourceBadge.ForeColor = Color.FromArgb(180, 80, 0);
                _lblSourceDetails.Text = diagnostic + " -> Click 'Open Scan File' or 'Load Demo Benchmark'.";
            }
        }

        private void OnOpenFileClicked()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Open Point Cloud Scan File";
                ofd.Filter = "Point Cloud Files (*.pts;*.xyz;*.txt;*.csv;*.asc)|*.pts;*.xyz;*.txt;*.csv;*.asc|All Files (*.*)|*.*";

                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        Cursor.Current = Cursors.WaitCursor;
                        _lblStatus.Text = $"Loading {Path.GetFileName(ofd.FileName)}...";
                        _progressBar.Value = 10;

                        var cloud = ScanDataLoader.LoadFromFile(ofd.FileName, (msg, pct) =>
                        {
                            if (this.InvokeRequired) this.BeginInvoke(new Action(() => _lblSubStatus.Text = msg));
                            else _lblSubStatus.Text = msg;
                        });

                        _currentCloud = cloud;
                        _currentSourceDesc = $"File: '{Path.GetFileName(ofd.FileName)}' ({cloud.Count:N0} points)";

                        _lblSourceBadge.Text = $"Data Source: Real Scan File: {Path.GetFileName(ofd.FileName)} ({cloud.Count:N0} points)";
                        _lblSourceBadge.ForeColor = Color.FromArgb(0, 102, 204);
                        _lblSourceDetails.Text = $"Path: {ofd.FileName} | Bounds: Min={cloud.BoundingMin:F2}, Max={cloud.BoundingMax:F2}";

                        _lblStatus.Text = $"Real scan loaded successfully ({cloud.Count:N0} points). Ready to classify.";
                        _lblSubStatus.Text = "Click 'Run Classification' to analyze real point cloud geometry.";
                        _progressBar.Value = 0;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Failed to load scan file:\n\n" + ex.Message, "Error Loading Scan", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        Cursor.Current = Cursors.Default;
                    }
                }
            }
        }

        private void OnQuerySceneClicked()
        {
            var cloud = ScanDataLoader.TryLoadActiveScanPoints(_host, out string sourceDesc, out string diagnostic);
            if (cloud != null && cloud.Count > 0)
            {
                _currentCloud = cloud;
                _currentSourceDesc = sourceDesc;
                _lblSourceBadge.Text = $"Data Source: {sourceDesc}";
                _lblSourceBadge.ForeColor = Color.FromArgb(16, 124, 65);
                _lblSourceDetails.Text = $"Points: {cloud.Count:N0} | Bounds: Min={cloud.BoundingMin:F2}, Max={cloud.BoundingMax:F2}";
                _lblStatus.Text = $"Active scan loaded ({cloud.Count:N0} points). Ready to classify.";
                _lblSubStatus.Text = "Click 'Run Classification' to extract elements.";
            }
            else
            {
                MessageBox.Show(this,
                    "Unable to read active scan points from SCENE workspace:\n\n" + diagnostic +
                    "\n\nTips:\n1. Make sure a project is open in SCENE.\n2. In SCENE's Structure Tree, right-click your scan and choose 'Load Points' into memory.\n3. Alternatively, click 'Open Scan File' to load your scan directly.",
                    "SCENE Workspace Query",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void OnLoadBenchmarkClicked()
        {
            _currentCloud = ScanDataLoader.GenerateBenchmarkScanCloud();
            _currentSourceDesc = "Synthetic TLS Indoor Scan Benchmark (Demo Mode - 149,000 pts)";

            _lblSourceBadge.Text = "Data Source: 🧪 Synthetic TLS Benchmark Room (Demo Mode - 149,000 pts)";
            _lblSourceBadge.ForeColor = Color.FromArgb(100, 100, 100);
            _lblSourceDetails.Text = "Demo scan: 12m x 10m x 3.2m synthetic indoor room (Ground Floor, Ceiling, 4 Walls, 2 Columns, 2 Pipes).";

            _lblStatus.Text = "Demo benchmark loaded (149,000 points).";
            _lblSubStatus.Text = "Click 'Run Classification' to test algorithms against synthetic benchmark geometry.";
        }

        private async Task StartClassificationAsync()
        {
            if (_currentCloud == null || _currentCloud.Count == 0)
            {
                var choice = MessageBox.Show(this,
                    "No point cloud is currently selected.\n\n" +
                    "Would you like to:\n" +
                    "- Click 'Yes' to browse and open a real scan file (.pts, .xyz, .txt)\n" +
                    "- Click 'No' to run the Demo Benchmark Scan for testing",
                    "Select Point Cloud Source",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (choice == DialogResult.Yes)
                {
                    OnOpenFileClicked();
                    if (_currentCloud == null) return;
                }
                else if (choice == DialogResult.No)
                {
                    OnLoadBenchmarkClicked();
                }
                else
                {
                    return;
                }
            }

            _btnRun.Enabled = false;
            _btnLoadFile.Enabled = false;
            _btnQueryScene.Enabled = false;
            _btnDemoBenchmark.Enabled = false;
            _btnExportDxf.Enabled = false;
            _btnExportPts.Enabled = false;
            _treeResults.Nodes.Clear();

            _lblStatus.Text = $"Analyzing {_currentSourceDesc}...";
            _lblSubStatus.Text = "Downsampling with spatial voxel grid...";
            _progressBar.Value = 5;

            var classifier = new ElementClassifier();
            classifier.ProgressChanged += (msg, pct) =>
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        _lblSubStatus.Text = msg;
                        _progressBar.Value = Math.Max(0, Math.Min(100, pct));
                    }));
                }
                else
                {
                    _lblSubStatus.Text = msg;
                    _progressBar.Value = Math.Max(0, Math.Min(100, pct));
                }
            };

            await Task.Run(() =>
            {
                _lastResult = classifier.Execute(_currentCloud, _host, voxelSize: 0.035f);
            });

            // Populate TreeView
            PopulateResultsTree(_lastResult);

            _lblStatus.Text = $"Classification Complete: {_lastResult.TotalDetectedCount} elements extracted.";
            _lblSubStatus.Text = $"Processed in {_lastResult.ExecutionTime.TotalSeconds:F2} seconds. Organized into SCENE project clusters.";
            _progressBar.Value = 100;

            _btnRun.Enabled = true;
            _btnLoadFile.Enabled = true;
            _btnQueryScene.Enabled = true;
            _btnDemoBenchmark.Enabled = true;
            _btnExportDxf.Enabled = true;
            _btnExportPts.Enabled = true;
        }

        private void PopulateResultsTree(ClassificationResult result)
        {
            _treeResults.BeginUpdate();
            _treeResults.Nodes.Clear();

            var root = new TreeNode($"Extracted Results: {result.TotalDetectedCount} elements from {_currentSourceDesc}");

            // Cluster 1: Floors
            var nodeFloors = new TreeNode($"📁 Floors ({result.Floors.Count})");
            foreach (var f in result.Floors)
            {
                var item = new TreeNode($"■ {f.Name} - {f.DimensionSummary} ({f.InlierCount:N0} inlier pts)");
                nodeFloors.Nodes.Add(item);
            }
            root.Nodes.Add(nodeFloors);

            // Cluster 2: Ceilings
            var nodeCeilings = new TreeNode($"📁 Ceilings ({result.Ceilings.Count})");
            foreach (var c in result.Ceilings)
            {
                var item = new TreeNode($"■ {c.Name} - {c.DimensionSummary} ({c.InlierCount:N0} inlier pts)");
                nodeCeilings.Nodes.Add(item);
            }
            root.Nodes.Add(nodeCeilings);

            // Cluster 3: Walls
            var nodeWalls = new TreeNode($"📁 Walls ({result.Walls.Count})");
            foreach (var w in result.Walls)
            {
                var item = new TreeNode($"■ {w.Name} - {w.DimensionSummary} ({w.InlierCount:N0} inlier pts)");
                nodeWalls.Nodes.Add(item);
            }
            root.Nodes.Add(nodeWalls);

            // Cluster 4: Columns
            var nodeColumns = new TreeNode($"📁 Columns ({result.Columns.Count})");
            foreach (var col in result.Columns)
            {
                var item = new TreeNode($"● {col.Name} - {col.DimensionSummary} ({col.InlierCount:N0} inlier pts)");
                nodeColumns.Nodes.Add(item);
            }
            root.Nodes.Add(nodeColumns);

            // Cluster 5: MEP Pipes
            var nodePipes = new TreeNode($"📁 MEP Pipes ({result.Pipes.Count})");
            foreach (var pipe in result.Pipes)
            {
                var item = new TreeNode($"● {pipe.Name} - {pipe.DimensionSummary} ({pipe.InlierCount:N0} inlier pts)");
                nodePipes.Nodes.Add(item);
            }
            root.Nodes.Add(nodePipes);

            _treeResults.Nodes.Add(root);
            _treeResults.ExpandAll();
            _treeResults.EndUpdate();
        }

        private void ExportDxf()
        {
            if (_lastResult == null) return;

            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Export Classified Elements to 3D DXF";
                sfd.Filter = "AutoCAD DXF (*.dxf)|*.dxf";
                sfd.FileName = "Classified_Elements.dxf";

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        ElementClassifier.ExportToDxf(_lastResult, sfd.FileName);
                        MessageBox.Show(this,
                            $"3D DXF exported successfully to:\n{sfd.FileName}\n\nLayers created: Floors, Ceilings, Walls, Columns, MEP_Pipes.",
                            "DXF Export Complete",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Failed to export DXF: " + ex.Message, "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ExportPts()
        {
            if (_lastResult == null) return;

            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Export Segmented Point Cloud";
                sfd.Filter = "Point Cloud PTS (*.pts)|*.pts|ASCII XYZ (*.xyz)|*.xyz";
                sfd.FileName = "Classified_Points.pts";

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var sb = new StringBuilder();
                        int totalPts = _lastResult.AllElements.Sum(e => e.InlierCount);
                        sb.AppendLine(totalPts.ToString());

                        foreach (var elem in _lastResult.AllElements)
                        {
                            int intensity = (int)elem.Type * 50 + 50;
                            foreach (var p in elem.InlierPoints)
                            {
                                sb.AppendLine($"{p.X:F4} {p.Y:F4} {p.Z:F4} {intensity}");
                            }
                        }

                        File.WriteAllText(sfd.FileName, sb.ToString());
                        MessageBox.Show(this,
                            $"Point cloud exported successfully with {totalPts:N0} points:\n{sfd.FileName}",
                            "Export Complete",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Failed to export points: " + ex.Message, "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
    }
}
