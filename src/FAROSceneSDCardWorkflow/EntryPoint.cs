using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using FARO.LS.SDK.AppLogic;
using FAROSceneSDCardWorkflow.Hardware;
using FAROSceneSDCardWorkflow.UI;
using FAROSceneSDCardWorkflow.Workflow;

namespace FAROSceneSDCardWorkflow
{
    /// <summary>
    /// Dual-model Entry Point for FARO SCENE:
    /// 1. Implements standard managed SCENE Open API lifecycle interface (IApp).
    /// 2. Provides unmanaged native C export bridges (lsAppStartup / lsAppShutdown)
    ///    required by FARO SCENE's native loader (Scene_Interfaces.dll).
    /// </summary>
    public class EntryPoint : IApp
    {
        // UI Ribbon Identifiers matching App.xml
        private const string TabId = "Apps";
        private const string TabTitle = "Apps";
        private const string GroupId = "SDCardWorkflowGroup";
        private const string GroupTitle = "SD Card Automation";
        private const string ButtonAutoWorkflowId = "BtnAutoWorkflow";
        private const string ButtonAutoWorkflowTitle = "SD Auto-Workflow";
        private const string ButtonDashboardId = "BtnDashboard";
        private const string ButtonDashboardTitle = "Workflow Center";

        // Singleton instance
        public static EntryPoint Instance { get; } = new EntryPoint();

        private IAppHost _host;
        private IRibbonButton _btnAutoWorkflow;
        private IRibbonButton _btnDashboard;
        private Image _smallIcon;
        private Image _largeIcon;

        private static Thread _dashboardThread;
        private static WorkflowDashboardWindow _dashboardWindow;
        private static readonly object _windowLock = new object();

        #region Native C++ Loader Export Bridge (lsAppStartup / lsAppShutdown)

        /// <summary>
        /// Native C export bridge invoked by SCENE's native loader.
        /// SCENE convention: returns 0 (LS_OK) on success, non-zero on error.
        /// </summary>
        public static int OnNativeStartup(IntPtr sceneContext, IntPtr reserved)
        {
            try
            {
                if (Instance._host == null)
                {
                    var fallbackHost = new LocalAppHost();
                    Instance.Initialize(fallbackHost);
                }

                ShowDashboardWindow();
                return 0; // LS_OK
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "FARO SCENE SD Card Workflow Add-in Initialization Exception:\n\n" + ex.Message,
                    "FARO SCENE Add-in Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }

        public static void OnNativeShutdown(IntPtr sceneContext, IntPtr reserved)
        {
            try
            {
                Instance.Shutdown();
            }
            catch
            {
            }
        }

        #endregion

        #region Lifecycle Startup & Registration

        public bool Initialize(IAppHost host)
        {
            if (host == null) return false;

            _host = host;

            try
            {
                _host.Logger?.LogInfo("[FAROSceneSDCardWorkflow] Initializing SD Card Automation Add-in...");

                // Initialize master coordinator and hardware watcher
                AutoWorkflowCoordinator.Instance.Initialize(_host);

                // Register Ribbon UI elements
                RegisterRibbonUI();

                _host.Logger?.LogInfo("[FAROSceneSDCardWorkflow] Add-in successfully initialized. SD Card Watcher active.");
                return true;
            }
            catch (Exception ex)
            {
                _host.Logger?.LogError($"[FAROSceneSDCardWorkflow] Failed to initialize: {ex.Message}", ex);
                return false;
            }
        }

        private void RegisterRibbonUI()
        {
            var ribbonManager = _host.RibbonManager;
            if (ribbonManager == null)
            {
                _host.Logger?.LogWarning("[FAROSceneSDCardWorkflow] RibbonManager is null. Ribbon UI skipped.");
                return;
            }

            IRibbonTab tab = ribbonManager.GetTab(TabId) ?? ribbonManager.CreateTab(TabId, TabTitle);
            if (tab == null) return;

            IRibbonGroup group = tab.GetGroup(GroupId) ?? tab.CreateGroup(GroupId, GroupTitle);
            if (group == null) return;

            LoadIcons();

            // Button 1: Auto Workflow
            _btnAutoWorkflow = group.CreateButton(ButtonAutoWorkflowId, ButtonAutoWorkflowTitle);
            if (_btnAutoWorkflow != null)
            {
                _btnAutoWorkflow.ToolTip = "Automated SD card scan ingestion, target registration, and top-view verification";
                _btnAutoWorkflow.Enabled = true;
                if (_smallIcon != null) _btnAutoWorkflow.SmallImage = _smallIcon;
                if (_largeIcon != null) _btnAutoWorkflow.LargeImage = _largeIcon;
                _btnAutoWorkflow.Click += OnAutoWorkflowClicked;
            }

            // Button 2: Workflow Center
            _btnDashboard = group.CreateButton(ButtonDashboardId, ButtonDashboardTitle);
            if (_btnDashboard != null)
            {
                _btnDashboard.ToolTip = "Open SD Card Automation Dashboard & Event Monitor";
                _btnDashboard.Enabled = true;
                if (_smallIcon != null) _btnDashboard.SmallImage = _smallIcon;
                if (_largeIcon != null) _btnDashboard.LargeImage = _largeIcon;
                _btnDashboard.Click += (s, e) => ShowDashboardWindow();
            }

            _host.Logger?.LogInfo("[FAROSceneSDCardWorkflow] Ribbon UI registered successfully.");
        }

        private void OnAutoWorkflowClicked(object sender, EventArgs e)
        {
            try
            {
                var drives = DriveInfo.GetDrives();
                SDCardInfo detectedCard = null;

                foreach (var d in drives)
                {
                    if (d.DriveType == DriveType.Removable && d.IsReady)
                    {
                        var card = SDCardWatcher.InspectFolder(d.RootDirectory.FullName);
                        if (card != null && card.ScanCount > 0)
                        {
                            detectedCard = card;
                            break;
                        }
                    }
                }

                if (detectedCard != null)
                {
                    AutoWorkflowCoordinator.Instance.PromptAndRunWorkflow(detectedCard);
                }
                else
                {
                    // No card with scans found, offer folder selection or simulation
                    var dialog = MessageBox.Show(
                        "No SD Card with scans detected in removable drives.\n\n" +
                        "Would you like to simulate an SD Card insertion with 3 sample scans?\n\n" +
                        "(Click 'No' to manually browse a folder on disk)",
                        "SD Card Not Detected",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question);

                    if (dialog == DialogResult.Yes)
                    {
                        AutoWorkflowCoordinator.Instance.SimulateSDCardInsertion();
                    }
                    else if (dialog == DialogResult.No)
                    {
                        using (var fbd = new FolderBrowserDialog())
                        {
                            fbd.Description = "Select Folder Containing FARO Scan Files (*.fls, *.pts, *.xyz)";
                            if (fbd.ShowDialog() == DialogResult.OK)
                            {
                                var card = SDCardWatcher.InspectFolder(fbd.SelectedPath);
                                if (card != null && card.ScanCount > 0)
                                {
                                    AutoWorkflowCoordinator.Instance.PromptAndRunWorkflow(card);
                                }
                                else
                                {
                                    MessageBox.Show("No supported scan files (.fls, .pts, .xyz) found in selected directory.",
                                        "No Scans Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error running SD Auto-Workflow: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static void ShowDashboardWindow()
        {
            lock (_windowLock)
            {
                if (_dashboardWindow != null && !_dashboardWindow.IsDisposed)
                {
                    try
                    {
                        _dashboardWindow.BeginInvoke(new Action(() =>
                        {
                            if (_dashboardWindow.WindowState == FormWindowState.Minimized)
                            {
                                _dashboardWindow.WindowState = FormWindowState.Normal;
                            }
                            _dashboardWindow.BringToFront();
                            _dashboardWindow.Activate();
                        }));
                        return;
                    }
                    catch
                    {
                        _dashboardWindow = null;
                    }
                }

                _dashboardThread = new Thread(() =>
                {
                    try
                    {
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        _dashboardWindow = new WorkflowDashboardWindow(AutoWorkflowCoordinator.Instance);
                        Application.Run(_dashboardWindow);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine("[FAROSceneSDCardWorkflow] Dashboard Thread Error: " + ex);
                    }
                });
                _dashboardThread.SetApartmentState(ApartmentState.STA);
                _dashboardThread.IsBackground = true;
                _dashboardThread.Start();
            }
        }

        private void LoadIcons()
        {
            try
            {
                string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string icon16 = Path.Combine(asmDir, "Resources", "icon16.png");
                string icon32 = Path.Combine(asmDir, "Resources", "icon32.png");

                if (File.Exists(icon16)) _smallIcon = Image.FromFile(icon16);
                if (File.Exists(icon32)) _largeIcon = Image.FromFile(icon32);
            }
            catch
            {
                // Non-critical icon loading failure
            }
        }

        #endregion

        #region Lifecycle Shutdown & Teardown

        public void Shutdown()
        {
            try
            {
                _host?.Logger?.LogInfo("[FAROSceneSDCardWorkflow] Shutting down add-in...");

                AutoWorkflowCoordinator.Instance.Stop();

                if (_dashboardWindow != null && !_dashboardWindow.IsDisposed)
                {
                    try
                    {
                        _dashboardWindow.BeginInvoke(new Action(() => _dashboardWindow.Close()));
                    }
                    catch { }
                    _dashboardWindow = null;
                }

                if (_btnAutoWorkflow != null)
                {
                    _btnAutoWorkflow.Click -= OnAutoWorkflowClicked;
                }

                try
                {
                    var tab = _host?.RibbonManager?.GetTab(TabId);
                    var group = tab?.GetGroup(GroupId);
                    group?.RemoveItem(ButtonAutoWorkflowId);
                    group?.RemoveItem(ButtonDashboardId);
                }
                catch { }

                _smallIcon?.Dispose();
                _smallIcon = null;
                _largeIcon?.Dispose();
                _largeIcon = null;

                _btnAutoWorkflow = null;
                _btnDashboard = null;

                _host?.Logger?.LogInfo("[FAROSceneSDCardWorkflow] Teardown complete.");
            }
            catch (Exception ex)
            {
                _host?.Logger?.LogError($"[FAROSceneSDCardWorkflow] Shutdown exception: {ex.Message}", ex);
            }
            finally
            {
                _host = null;
            }
        }

        #endregion
    }

    /// <summary>
    /// Fallback host implementation used when loaded via native C++ lsAppStartup thunk.
    /// </summary>
    internal class LocalAppHost : IAppHost
    {
        public FARO.LS.SDK.DataLogic.IWorkspaceHost WorkspaceHost { get; } = new LocalWorkspaceHost();
        public IRibbonManager RibbonManager { get; } = null;
        public ILogger Logger { get; } = new LocalLogger();
        public string AppDirectory => Path.GetDirectoryName(typeof(EntryPoint).Assembly.Location);
    }

    internal class LocalWorkspaceHost : FARO.LS.SDK.DataLogic.IWorkspaceHost
    {
        public FARO.LS.SDK.DataLogic.IWorkspace ActiveWorkspace => null;
        public bool HasActiveWorkspace => false;
    }

    internal class LocalLogger : ILogger
    {
        public void LogInfo(string message) => System.Diagnostics.Trace.WriteLine(message);
        public void LogWarning(string message) => System.Diagnostics.Trace.WriteLine(message);
        public void LogError(string message, Exception ex = null) => System.Diagnostics.Trace.WriteLine($"{message} {ex}");
    }

    /// <summary>
    /// Explicit class exposing the C-style unmanaged entry points for ILASM export.
    /// Manages early AssemblyResolve hook before any SDK types are accessed by the JIT.
    /// </summary>
    public static class AppNativeExports
    {
        static AppNativeExports()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveLocalDependencies;
        }

        private static Assembly ResolveLocalDependencies(object sender, ResolveEventArgs args)
        {
            try
            {
                string name = new AssemblyName(args.Name).Name + ".dll";
                string baseDir = Path.GetDirectoryName(typeof(AppNativeExports).Assembly.Location);
                if (!string.IsNullOrEmpty(baseDir))
                {
                    string candidate = Path.Combine(baseDir, name);
                    if (File.Exists(candidate))
                    {
                        return Assembly.LoadFrom(candidate);
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int lsAppStartup(IntPtr sceneContext, IntPtr reserved)
        {
            return SafeStartup(sceneContext, reserved);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int SafeStartup(IntPtr sceneContext, IntPtr reserved)
        {
            return EntryPoint.OnNativeStartup(sceneContext, reserved);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void lsAppShutdown(IntPtr sceneContext, IntPtr reserved)
        {
            SafeShutdown(sceneContext, reserved);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SafeShutdown(IntPtr sceneContext, IntPtr reserved)
        {
            EntryPoint.OnNativeShutdown(sceneContext, reserved);
        }
    }
}
