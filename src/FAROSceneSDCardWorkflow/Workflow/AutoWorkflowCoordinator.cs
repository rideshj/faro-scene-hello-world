using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FARO.LS.SDK.AppLogic;
using FAROSceneSDCardWorkflow.Hardware;
using FAROSceneSDCardWorkflow.Registration;
using FAROSceneSDCardWorkflow.UI;

namespace FAROSceneSDCardWorkflow.Workflow
{
    /// <summary>
    /// Master coordinator for the automated SD Card Scan Ingestion & Target Registration pipeline.
    /// Connects: Hardware SD Detection -> Prompt UI -> File Ingestion -> Processing -> Target Registration -> Top View -> SCENE Project Tree.
    /// </summary>
    public class AutoWorkflowCoordinator : IDisposable
    {
        private static AutoWorkflowCoordinator _instance;
        public static AutoWorkflowCoordinator Instance => _instance ?? (_instance = new AutoWorkflowCoordinator());

        private readonly SDCardWatcher _watcher;
        private IAppHost _host;
        private bool _isWorkflowRunning = false;

        public event Action<string> LogMessage;
        public event Action<WorkflowStage, string, int> StatusChanged;

        public SDCardWatcher Watcher => _watcher;
        public RegistrationResult LastRegistrationResult { get; private set; }
        public string LastProjectPath { get; private set; }

        public AutoWorkflowCoordinator()
        {
            _watcher = new SDCardWatcher(pollIntervalMs: 1500);
            _watcher.CardInserted += OnCardInserted;
            _watcher.WatcherError += (err) => Log($"[SDCardWatcher Error] {err}");
        }

        public void Initialize(IAppHost host)
        {
            _host = host;
            _watcher.Start();
            Log("[AutoWorkflowCoordinator] Hardware SD Card Watcher started. Monitoring removable storage...");
        }

        public void Stop()
        {
            _watcher.Stop();
            Log("[AutoWorkflowCoordinator] Hardware SD Card Watcher stopped.");
        }

        public void Log(string message)
        {
            System.Diagnostics.Trace.WriteLine(message);
            LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
            _host?.Logger?.LogInfo(message);
        }

        private void OnCardInserted(SDCardInfo card)
        {
            Log($"[Hardware] SD Card detected on drive {card.DriveLetter} ({card.VolumeLabel}). Scans found: {card.ScanCount}");

            if (_isWorkflowRunning)
            {
                Log("[AutoWorkflowCoordinator] Workflow already running. Queued event skipped.");
                return;
            }

            // Launch prompt on STA UI thread
            var thread = new Thread(() =>
            {
                PromptAndRunWorkflow(card);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        /// <summary>
        /// Prompts the user with SDCardPromptDialog and initiates the 5-stage pipeline upon confirmation.
        /// </summary>
        public void PromptAndRunWorkflow(SDCardInfo card)
        {
            if (_isWorkflowRunning)
            {
                MessageBox.Show("A workflow task is currently running. Please wait for it to complete.",
                    "Workflow Busy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                _isWorkflowRunning = true;

                // 1. Show interactive popup dialog
                SDCardPromptDialog promptDlg = null;
                DialogResult dr = DialogResult.Cancel;

                using (promptDlg = new SDCardPromptDialog(card))
                {
                    dr = promptDlg.ShowDialog();
                }

                if (dr != DialogResult.OK)
                {
                    Log("[AutoWorkflowCoordinator] User cancelled SD card workflow prompt.");
                    return;
                }

                string projectName = promptDlg.ProjectName;
                string baseDir = promptDlg.BaseDirectory;
                TargetType targetType = promptDlg.SelectedTargetType;
                bool strayFilter = promptDlg.ApplyStrayFilter;
                bool runReg = promptDlg.PerformRegistration;
                bool showTopView = promptDlg.ShowTopViewVerification;

                // 2. Execute pipeline with WorkflowProgressDialog
                ExecutePipeline(card, projectName, baseDir, targetType, strayFilter, runReg, showTopView);
            }
            catch (Exception ex)
            {
                Log($"[AutoWorkflowCoordinator] Pipeline exception: {ex.Message}");
                MessageBox.Show($"Pipeline execution failed:\n\n{ex.Message}", "Workflow Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isWorkflowRunning = false;
            }
        }

        private void ExecutePipeline(
            SDCardInfo card,
            string projectName,
            string baseDir,
            TargetType targetType,
            bool strayFilter,
            bool runReg,
            bool showTopView)
        {
            var cts = new CancellationTokenSource();
            WorkflowProgressDialog progressDlg = new WorkflowProgressDialog(cts);

            // Run background task
            var task = Task.Run(() =>
            {
                try
                {
                    // ----------------------------------------------------
                    // STAGE 1: Create Project Structure
                    // ----------------------------------------------------
                    ReportWorkflowProgress(progressDlg, WorkflowStage.ProjectCreated, "Creating local project directory structure...", 20, 5);
                    Log($"[Stage 1/5] Creating project directory '{projectName}' in '{baseDir}'...");
                    string projectPath = ScanImportManager.CreateProjectDirectory(baseDir, projectName);
                    LastProjectPath = projectPath;
                    ReportWorkflowProgress(progressDlg, WorkflowStage.ProjectCreated, "Project folder created.", 100, 10);
                    Thread.Sleep(300);

                    // ----------------------------------------------------
                    // STAGE 2: Import Scans from Removable Media
                    // ----------------------------------------------------
                    ReportWorkflowProgress(progressDlg, WorkflowStage.ImportingScans, "Importing scans from SD card to local SSD...", 0, 15);
                    Log($"[Stage 2/5] Importing {card.ScanFiles.Count} scan(s) from {card.DriveLetter} to {projectPath}...");

                    var importedFiles = ScanImportManager.ImportScans(
                        card.ScanFiles,
                        projectPath,
                        (msg, pct) => ReportWorkflowProgress(progressDlg, WorkflowStage.ImportingScans, msg, pct, 15 + (int)(pct * 0.25f)),
                        cts.Token);

                    Log($"[Stage 2/5] Imported {importedFiles.Count} files successfully.");

                    // ----------------------------------------------------
                    // STAGE 3: Process Scans (Filter stray points & Grid)
                    // ----------------------------------------------------
                    ReportWorkflowProgress(progressDlg, WorkflowStage.ProcessingScans, "Filtering stray points and loading scan geometry...", 0, 40);
                    Log("[Stage 3/5] Processing scan point clouds...");

                    var stations = ScanProcessor.ProcessScans(
                        importedFiles,
                        applyStrayFilter: strayFilter,
                        applyColorization: true,
                        progressCallback: (msg, pct) => ReportWorkflowProgress(progressDlg, WorkflowStage.ProcessingScans, msg, pct, 40 + (int)(pct * 0.25f)),
                        cancellationToken: cts.Token);

                    // ----------------------------------------------------
                    // STAGE 4: Target Detection & 6-DOF Registration
                    // ----------------------------------------------------
                    RegistrationResult regResult = null;
                    if (runReg)
                    {
                        ReportWorkflowProgress(progressDlg, WorkflowStage.RegisteringTargets, "Detecting reference targets and solving 6-DOF registration...", 0, 65);
                        Log($"[Stage 4/5] Executing target-based registration (Target: {targetType})...");

                        var regEngine = new TargetRegistrationEngine();
                        regEngine.ProgressChanged += (msg, pct) =>
                        {
                            ReportWorkflowProgress(progressDlg, WorkflowStage.RegisteringTargets, msg, pct, 65 + (int)(pct * 0.25f));
                        };

                        var settings = new TargetRegistrationSettings
                        {
                            TargetType = targetType,
                            MaxDistanceTolerance = 0.008f, // 8mm correspondence distance tolerance
                            MaxAllowedMeanErrorMm = 4.0f
                        };

                        regResult = regEngine.Execute(stations, settings);
                        LastRegistrationResult = regResult;

                        Log($"[Stage 4/5] Target registration complete: {regResult}");
                    }

                    ReportWorkflowProgress(progressDlg, WorkflowStage.VerifyingTopView, "Preparing Top View verification...", 100, 95);
                    Thread.Sleep(400);

                    // Close progress dialog on UI thread
                    progressDlg.Invoke(new Action(() => progressDlg.Close()));

                    // ----------------------------------------------------
                    // STAGE 5: Top View Verification Dialog
                    // ----------------------------------------------------
                    if (showTopView && regResult != null)
                    {
                        progressDlg.Invoke(new Action(() =>
                        {
                            using (var verifyDlg = new TopViewVerificationDialog(regResult, projectPath))
                            {
                                if (verifyDlg.ShowDialog() == DialogResult.OK)
                                {
                                    CompleteWorkflow(regResult, projectPath, card);
                                }
                                else
                                {
                                    Log("[AutoWorkflowCoordinator] User rejected top view registration verification.");
                                }
                            }
                        }));
                    }
                    else if (regResult != null)
                    {
                        CompleteWorkflow(regResult, projectPath, card);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log("[AutoWorkflowCoordinator] Workflow was cancelled by user.");
                    progressDlg.Invoke(new Action(() => progressDlg.Close()));
                    MessageBox.Show("Workflow operation was cancelled.", "Workflow Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Log($"[AutoWorkflowCoordinator] Workflow background error: {ex}");
                    progressDlg.Invoke(new Action(() => progressDlg.Close()));
                    MessageBox.Show($"Workflow error:\n\n{ex.Message}", "Workflow Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }, cts.Token);

            // Display modal progress dialog on UI thread
            progressDlg.ShowDialog();
        }

        private void CompleteWorkflow(RegistrationResult result, string projectPath, SDCardInfo card)
        {
            Log($"[Stage 6/6] Finalizing registration and writing summary report for project {projectPath}...");

            try
            {
                // Write final project report
                string reportDir = Path.Combine(projectPath, "Reports");
                Directory.CreateDirectory(reportDir);
                string reportPath = Path.Combine(reportDir, "RegistrationReport.txt");

                using (var sw = new StreamWriter(reportPath))
                {
                    sw.WriteLine("==========================================================================");
                    sw.WriteLine("           FARO SCENE AUTOMATED FIELD-TO-FINISH REGISTRATION REPORT       ");
                    sw.WriteLine("==========================================================================");
                    sw.WriteLine($"Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sw.WriteLine($"Source    : SD Card {card.DriveLetter} ({card.VolumeLabel})");
                    sw.WriteLine($"Project   : {projectPath}");
                    sw.WriteLine($"Status    : {result.QualityRating}");
                    sw.WriteLine($"Scans     : {result.RegisteredScans}/{result.TotalScans} Registered");
                    sw.WriteLine($"Mean Err  : {result.MeanTargetErrorMm:F2} mm");
                    sw.WriteLine($"Max Err   : {result.MaxTargetErrorMm:F2} mm");
                    sw.WriteLine($"Elapsed   : {result.Elapsed.TotalSeconds:F2} s");
                    sw.WriteLine("--------------------------------------------------------------------------");
                    sw.WriteLine("REGISTERED SCAN STATIONS:");
                    foreach (var s in result.Stations)
                    {
                        var p = s.ScannerPosition;
                        sw.WriteLine($"  [{s.ScanId}] {s.Name} => Position: ({p.X:F4}, {p.Y:F4}, {p.Z:F4}) | Targets: {s.Targets.Count}");
                    }
                    sw.WriteLine("==========================================================================");
                }

                // If host is connected, log to SCENE Open API host
                _host?.Logger?.LogInfo($"[FAROSceneSDCardWorkflow] Registration completed for {result.RegisteredScans} scans with mean error {result.MeanTargetErrorMm:F2}mm.");

                string summary = $"Pipeline Completed Successfully!\n\n" +
                                 $"• Project: {Path.GetFileName(projectPath)}\n" +
                                 $"• Scans Registered: {result.RegisteredScans} of {result.TotalScans}\n" +
                                 $"• Target Mean Residual: {result.MeanTargetErrorMm:F2} mm\n" +
                                 $"• Quality Rating: {result.QualityRating}\n" +
                                 $"• Report Saved: {reportPath}\n\n" +
                                 $"Would you like to safely eject the SD card ({card.DriveLetter})?";

                var res = MessageBox.Show(summary, "FARO SCENE - Workflow Complete", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (res == DialogResult.Yes)
                {
                    Log($"[AutoWorkflowCoordinator] SD Card {card.DriveLetter} safe for removal.");
                    MessageBox.Show($"SD Card {card.DriveLetter} is now ready to be safely removed from your computer.",
                        "Safe to Remove Hardware", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Log($"[AutoWorkflowCoordinator] CompleteWorkflow warning: {ex.Message}");
            }
        }

        /// <summary>
        /// Simulates inserting an SD card with 3 overlapping scans and 4 shared reference spheres for testing.
        /// </summary>
        public void SimulateSDCardInsertion()
        {
            Log("[AutoWorkflowCoordinator] Generating synthetic SD Card data with 3 overlapping scan stations...");

            string simFolder = Path.Combine(Path.GetTempPath(), "FARO_Simulated_SDCard_" + Guid.NewGuid().ToString().Substring(0, 8));
            var card = SyntheticSDCardGenerator.GenerateSyntheticSDCard(simFolder, scanCount: 3);

            Log($"[AutoWorkflowCoordinator] Simulated SD card ready at {card.DriveLetter} ({card.ScanFiles.Count} scans). Triggering workflow prompt...");
            _watcher.SimulateCardInserted(card.DriveLetter);
        }

        private void ReportWorkflowProgress(WorkflowProgressDialog dlg, WorkflowStage stage, string message, int stagePct, int overallPct)
        {
            dlg.UpdateStage(stage, message, stagePct, overallPct);
            StatusChanged?.Invoke(stage, message, overallPct);
        }

        public void Dispose()
        {
            _watcher.Dispose();
        }
    }
}
