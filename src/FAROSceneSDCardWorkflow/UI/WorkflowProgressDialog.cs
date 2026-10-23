using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using FAROSceneSDCardWorkflow.Workflow;

namespace FAROSceneSDCardWorkflow.UI
{
    /// <summary>
    /// Multi-stage pipeline progress dialog with cancellation support and live status reporting.
    /// </summary>
    public class WorkflowProgressDialog : Form
    {
        private readonly CancellationTokenSource _cts;

        private Label _lblStageTitle;
        private Label _lblCurrentTask;
        private ProgressBar _pbStage;
        private ProgressBar _pbOverall;
        private Label _lblOverallPercent;
        private Button _btnCancel;
        private ListView _lvStages;

        public CancellationToken CancellationToken => _cts.Token;

        public WorkflowProgressDialog(CancellationTokenSource cts)
        {
            _cts = cts ?? new CancellationTokenSource();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "FARO SCENE - Automated SD Card Pipeline";
            Width = 580;
            Height = 440;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(248, 249, 251);

            // Header Banner
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(32, 36, 44),
                Padding = new Padding(20, 12, 20, 12)
            };

            _lblStageTitle = new Label
            {
                Text = "Automated SD Ingestion in Progress...",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 12),
                AutoSize = true
            };

            _lblCurrentTask = new Label
            {
                Text = "Initializing workflow pipeline...",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(180, 190, 205),
                Location = new Point(21, 38),
                AutoSize = true
            };

            header.Controls.Add(_lblStageTitle);
            header.Controls.Add(_lblCurrentTask);

            // Center Panel
            var center = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 14, 20, 14)
            };

            _lvStages = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Location = new Point(20, 14),
                Size = new Size(524, 150),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 8.5f)
            };
            _lvStages.Columns.Add("Step", 50);
            _lvStages.Columns.Add("Stage", 340);
            _lvStages.Columns.Add("Status", 120);

            AddStageRow(1, "Create Local Project Structure", "Pending");
            AddStageRow(2, "Import Scans from SD Storage", "Pending");
            AddStageRow(3, "Process Points & Stray Filter", "Pending");
            AddStageRow(4, "Detect Targets & 6-DOF Registration", "Pending");
            AddStageRow(5, "Top View Bird's Eye Verification", "Pending");

            // Stage Progress
            var lblStageProgress = new Label
            {
                Text = "Stage Progress:",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(20, 178),
                AutoSize = true
            };

            _pbStage = new ProgressBar
            {
                Location = new Point(20, 200),
                Size = new Size(524, 20),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            // Overall Progress
            var lblOverall = new Label
            {
                Text = "Overall Workflow Progress:",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(20, 232),
                AutoSize = true
            };

            _lblOverallPercent = new Label
            {
                Text = "0%",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(515, 232),
                AutoSize = true
            };

            _pbOverall = new ProgressBar
            {
                Location = new Point(20, 254),
                Size = new Size(524, 20),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            center.Controls.Add(_lvStages);
            center.Controls.Add(lblStageProgress);
            center.Controls.Add(_pbStage);
            center.Controls.Add(lblOverall);
            center.Controls.Add(_lblOverallPercent);
            center.Controls.Add(_pbOverall);

            // Footer
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 55,
                BackColor = Color.White,
                Padding = new Padding(20, 10, 20, 10)
            };

            _btnCancel = new Button
            {
                Text = "Cancel Workflow",
                Size = new Size(130, 32),
                Location = new Point(footer.Width - 150, 10),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(200, 30, 30),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(220, 180, 180);
            _btnCancel.Click += (s, e) =>
            {
                _cts.Cancel();
                _btnCancel.Enabled = false;
                _btnCancel.Text = "Cancelling...";
            };

            footer.Controls.Add(_btnCancel);

            Controls.Add(center);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private void AddStageRow(int step, string stage, string status)
        {
            var item = new ListViewItem(step.ToString());
            item.SubItems.Add(stage);
            item.SubItems.Add(status);
            _lvStages.Items.Add(item);
        }

        public void UpdateStage(WorkflowStage stage, string detailMessage, int stagePct, int overallPct)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => UpdateStage(stage, detailMessage, stagePct, overallPct)));
                return;
            }

            _lblStageTitle.Text = $"Stage: {stage}";
            _lblCurrentTask.Text = detailMessage ?? "";
            _pbStage.Value = Math.Max(0, Math.Min(100, stagePct));
            _pbOverall.Value = Math.Max(0, Math.Min(100, overallPct));
            _lblOverallPercent.Text = $"{_pbOverall.Value}%";

            int activeIndex = -1;
            switch (stage)
            {
                case WorkflowStage.ProjectCreated: activeIndex = 0; break;
                case WorkflowStage.ImportingScans: activeIndex = 1; break;
                case WorkflowStage.ProcessingScans: activeIndex = 2; break;
                case WorkflowStage.RegisteringTargets: activeIndex = 3; break;
                case WorkflowStage.VerifyingTopView: activeIndex = 4; break;
            }

            for (int i = 0; i < _lvStages.Items.Count; i++)
            {
                if (i < activeIndex)
                {
                    _lvStages.Items[i].SubItems[2].Text = "✓ Complete";
                    _lvStages.Items[i].ForeColor = Color.FromArgb(16, 124, 65);
                }
                else if (i == activeIndex)
                {
                    _lvStages.Items[i].SubItems[2].Text = "In Progress...";
                    _lvStages.Items[i].ForeColor = Color.FromArgb(0, 120, 215);
                }
                else
                {
                    _lvStages.Items[i].SubItems[2].Text = "Pending";
                    _lvStages.Items[i].ForeColor = Color.FromArgb(120, 130, 140);
                }
            }
        }
    }
}
