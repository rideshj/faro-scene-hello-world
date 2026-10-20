using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using FARO.LS.SDK.AppLogic;
using FAROSceneHelloWorld.Classification;

namespace FAROSceneHelloWorld.UI
{
    /// <summary>
    /// Interactive dialog displaying real-time extraction progress and hierarchical
    /// cluster results for structural and MEP elements.
    /// </summary>
    public class ClassificationProgressDialog : Form
    {
        private readonly IAppHost _host;
        private ProgressBar _progressBar;
        private Label _lblStatus;
        private Label _lblSubStatus;
        private TreeView _treeResults;
        private Button _btnRun;
        private Button _btnExportDxf;
        private Button _btnExportPts;
        private Button _btnClose;

        private ClassificationResult _lastResult;

        public ClassificationProgressDialog(IAppHost host)
        {
            _host = host;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "FARO SCENE - Structural & MEP Element Auto-Classifier";
            this.Size = new Size(680, 580);
            this.MinimumSize = new Size(600, 480);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.BackColor = Color.FromArgb(245, 247, 250);

            // 1. Header Banner
            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.FromArgb(24, 76, 120),
                Padding = new Padding(15, 12, 15, 12)
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
                Text = "Spatial Voxel Grid • PCA Normal Estimation • RANSAC Plane & Cylinder Segmentation",
                ForeColor = Color.FromArgb(200, 225, 250),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(16, 36)
            };

            headerPanel.Controls.Add(lblTitle);
            headerPanel.Controls.Add(lblSubtitle);
            this.Controls.Add(headerPanel);

            // 2. Progress Section
            var progressPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 75,
                Padding = new Padding(15, 10, 15, 5),
                BackColor = Color.FromArgb(245, 247, 250)
            };

            _lblStatus = new Label
            {
                Text = "Ready to classify active scan elements.",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(15, 8),
                AutoSize = true
            };

            _lblSubStatus = new Label
            {
                Text = "Click 'Run Classification' to extract Floors, Ceilings, Walls, Columns, and Pipes.",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 100, 100),
                Location = new Point(15, 28),
                AutoSize = true
            };

            _progressBar = new ProgressBar
            {
                Location = new Point(15, 48),
                Size = new Size(635, 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Style = ProgressBarStyle.Continuous
            };

            progressPanel.Controls.Add(_lblStatus);
            progressPanel.Controls.Add(_lblSubStatus);
            progressPanel.Controls.Add(_progressBar);
            this.Controls.Add(progressPanel);

            // 3. TreeView for Hierarchical Cluster Results
            var resultsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(15, 5, 15, 10)
            };

            var lblTreeHeader = new Label
            {
                Text = "Detected Clusters & Geometric Primitives (SCENE Project Tree):",
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

            // 4. Bottom Action Bar
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = Color.FromArgb(235, 238, 242),
                Padding = new Padding(15, 10, 15, 10)
            };

            _btnRun = new Button
            {
                Text = "▶ Run Classification",
                Size = new Size(150, 30),
                Location = new Point(15, 10),
                BackColor = Color.FromArgb(0, 120, 215),
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
                Location = new Point(175, 10),
                Enabled = false,
                UseVisualStyleBackColor = true
            };
            _btnExportDxf.Click += (s, e) => ExportDxf();

            _btnExportPts = new Button
            {
                Text = "Export PTS Points",
                Size = new Size(125, 30),
                Location = new Point(298, 10),
                Enabled = false,
                UseVisualStyleBackColor = true
            };
            _btnExportPts.Click += (s, e) => ExportPts();

            _btnClose = new Button
            {
                Text = "Close",
                Size = new Size(85, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(bottomPanel.Width - 100, 10),
                UseVisualStyleBackColor = true
            };
            _btnClose.Click += (s, e) => this.Close();

            bottomPanel.Controls.Add(_btnRun);
            bottomPanel.Controls.Add(_btnExportDxf);
            bottomPanel.Controls.Add(_btnExportPts);
            bottomPanel.Controls.Add(_btnClose);
            this.Controls.Add(bottomPanel);
        }

        private async Task StartClassificationAsync()
        {
            _btnRun.Enabled = false;
            _btnExportDxf.Enabled = false;
            _btnExportPts.Enabled = false;
            _treeResults.Nodes.Clear();

            _lblStatus.Text = "Acquiring scan data...";
            _lblSubStatus.Text = "Querying active workspace scan or synthetic TLS benchmark...";
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
                // 1. Load active scan points or benchmark scan
                var rawCloud = ScanDataLoader.LoadActiveScanPoints(_host, out string sourceDesc);
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() => _lblStatus.Text = $"Processing {sourceDesc}..."));
                }

                // 2. Execute full classification pipeline
                _lastResult = classifier.Execute(rawCloud, _host, voxelSize: 0.05f);
            });

            // Populate TreeView
            PopulateResultsTree(_lastResult);

            _lblStatus.Text = $"Classification Complete: {_lastResult.TotalDetectedCount} elements extracted.";
            _lblSubStatus.Text = $"Processed in {_lastResult.ExecutionTime.TotalSeconds:F2} seconds. Inserted into SCENE project clusters.";
            _progressBar.Value = 100;

            _btnRun.Enabled = true;
            _btnExportDxf.Enabled = true;
            _btnExportPts.Enabled = true;
        }

        private void PopulateResultsTree(ClassificationResult result)
        {
            _treeResults.BeginUpdate();
            _treeResults.Nodes.Clear();

            var root = new TreeNode($"SCENE Project Workspace (Extracted: {result.TotalDetectedCount} elements)");

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
