using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using FARO.LS.SDK.AppLogic;
using FAROSceneHelloWorld.Commands;
using FAROSceneHelloWorld.UI;

namespace FAROSceneHelloWorld
{
    /// <summary>
    /// Dual-model Entry Point for FARO SCENE:
    /// 1. Implements standard managed SCENE Open API lifecycle interface (IApp).
    /// 2. Provides unmanaged native C export bridges (lsAppStartup / lsAppShutdown)
    ///    required by FARO SCENE's native loader (Scene_Interfaces.dll).
    /// </summary>
    public class EntryPoint : IApp
    {
        // UI Identifiers
        private const string TabId = "Apps";
        private const string TabTitle = "Apps";
        private const string GroupId = "WorkspaceToolsGroup";
        private const string GroupTitle = "Workspace Tools";
        private const string ButtonId = "BtnCountScanPoints";
        private const string ButtonTitle = "Scan Stats";

        // Singleton instance for bridge dispatch
        public static EntryPoint Instance { get; } = new EntryPoint();

        // Cached host and UI references for lifecycle management
        private IAppHost _host;
        private IRibbonButton _ribbonButton;
        private CountPointsCommand _command;
        private Image _smallIcon;
        private Image _largeIcon;

        private static Thread _uiThread;
        private static InteractiveToolboxWindow _toolboxWindow;
        private static readonly object _windowLock = new object();

        #region Native C++ Loader Export Bridge (lsAppStartup / lsAppShutdown)

        /// <summary>
        /// Called when FARO SCENE native loader invokes lsAppStartup.
        /// SCENE convention: returns 0 (LS_OK) on success, non-zero on error.
        /// </summary>
        public static int OnNativeStartup(IntPtr sceneContext, IntPtr reserved)
        {
            try
            {
                // If a host was not already supplied via managed activation, synthesize fallback host
                if (Instance._host == null)
                {
                    var fallbackHost = new LocalAppHost();
                    Instance.Initialize(fallbackHost);
                }

                // Show the interactive diagnostic window on an STA thread
                ShowInteractiveWindow(Instance._host);

                return 0; // LS_OK / Success
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "FARO SCENE Add-in Initialization Exception:\n\n" + ex.Message,
                    "FARO SCENE Add-in Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1; // Error code
            }
        }

        /// <summary>
        /// Called when FARO SCENE native loader invokes lsAppShutdown.
        /// </summary>
        public static void OnNativeShutdown(IntPtr sceneContext, IntPtr reserved)
        {
            try
            {
                Instance.Shutdown();
            }
            catch
            {
                // Suppress shutdown exceptions
            }
        }

        #endregion

        #region Lifecycle Startup & Registration

        /// <summary>
        /// LIFECYCLE STEP 1: INITIALIZE
        /// Called by FARO SCENE when the application loads or when the add-in is enabled
        /// by the user inside the SCENE App Manager.
        /// </summary>
        public bool Initialize(IAppHost host)
        {
            if (host == null)
            {
                return false;
            }

            _host = host;

            try
            {
                _host.Logger?.LogInfo("[FAROSceneHelloWorld] Initializing add-in...");

                // Initialize the command handler
                _command = new CountPointsCommand(_host);

                // Register UI Ribbon elements
                RegisterRibbonUI();

                _host.Logger?.LogInfo("[FAROSceneHelloWorld] Add-in successfully initialized and registered.");
                return true;
            }
            catch (Exception ex)
            {
                _host.Logger?.LogError($"[FAROSceneHelloWorld] Failed to initialize add-in: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// LIFECYCLE STEP 2: REGISTRATION
        /// Builds Ribbon UI elements (Tab -> Group -> Button) and connects click events.
        /// </summary>
        private void RegisterRibbonUI()
        {
            var ribbonManager = _host.RibbonManager;
            if (ribbonManager == null)
            {
                _host.Logger?.LogWarning("[FAROSceneHelloWorld] RibbonManager is null. UI creation skipped.");
                return;
            }

            IRibbonTab tab = ribbonManager.GetTab(TabId) ?? ribbonManager.CreateTab(TabId, TabTitle);
            if (tab == null)
            {
                _host.Logger?.LogWarning("[FAROSceneHelloWorld] Unable to create or acquire RibbonTab.");
                return;
            }

            IRibbonGroup group = tab.GetGroup(GroupId) ?? tab.CreateGroup(GroupId, GroupTitle);
            if (group == null)
            {
                _host.Logger?.LogWarning("[FAROSceneHelloWorld] Unable to create or acquire RibbonGroup.");
                return;
            }

            _ribbonButton = group.CreateButton(ButtonId, ButtonTitle);
            if (_ribbonButton != null)
            {
                _ribbonButton.ToolTip = "Count and display active scan points and open scans in a modal dialog";
                _ribbonButton.Enabled = true;

                LoadButtonIcons();

                if (_smallIcon != null) _ribbonButton.SmallImage = _smallIcon;
                if (_largeIcon != null) _ribbonButton.LargeImage = _largeIcon;

                _ribbonButton.Click += OnRibbonButtonClicked;
                _host.Logger?.LogInfo("[FAROSceneHelloWorld] Ribbon button registered and event handler attached.");
            }
        }

        private void OnRibbonButtonClicked(object sender, EventArgs e)
        {
            ShowInteractiveWindow(_host);
        }

        /// <summary>
        /// Launches or activates the interactive modeless toolbox window on an STA thread.
        /// </summary>
        public static void ShowInteractiveWindow(IAppHost host)
        {
            lock (_windowLock)
            {
                if (_toolboxWindow != null && !_toolboxWindow.IsDisposed)
                {
                    try
                    {
                        _toolboxWindow.BeginInvoke(new Action(() =>
                        {
                            if (_toolboxWindow.WindowState == FormWindowState.Minimized)
                            {
                                _toolboxWindow.WindowState = FormWindowState.Normal;
                            }
                            _toolboxWindow.BringToFront();
                            _toolboxWindow.Activate();
                        }));
                        return;
                    }
                    catch
                    {
                        _toolboxWindow = null;
                    }
                }

                _uiThread = new Thread(() =>
                {
                    try
                    {
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        _toolboxWindow = new InteractiveToolboxWindow(host);
                        Application.Run(_toolboxWindow);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine("[FAROSceneHelloWorld] UI Thread Error: " + ex);
                    }
                });
                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.IsBackground = true;
                _uiThread.Start();
            }
        }

        private void LoadButtonIcons()
        {
            try
            {
                string baseDir = _host.AppDirectory;
                if (string.IsNullOrEmpty(baseDir))
                {
                    baseDir = Path.GetDirectoryName(typeof(EntryPoint).Assembly.Location);
                }

                string icon16Path = Path.Combine(baseDir, "Resources", "icon16.png");
                string icon32Path = Path.Combine(baseDir, "Resources", "icon32.png");

                if (File.Exists(icon16Path))
                {
                    _smallIcon = Image.FromFile(icon16Path);
                }

                if (File.Exists(icon32Path))
                {
                    _largeIcon = Image.FromFile(icon32Path);
                }
            }
            catch (Exception ex)
            {
                _host.Logger?.LogWarning($"[FAROSceneHelloWorld] Non-critical warning: Unable to load icons: {ex.Message}");
            }
        }

        #endregion

        #region Lifecycle Shutdown & Teardown

        /// <summary>
        /// LIFECYCLE STEP 3: SHUTDOWN / UNLOAD
        /// Called by FARO SCENE when the host application is shutting down or when the user
        /// disables the add-in inside the SCENE App Manager.
        /// </summary>
        public void Shutdown()
        {
            try
            {
                _host?.Logger?.LogInfo("[FAROSceneHelloWorld] Shutting down add-in...");

                if (_toolboxWindow != null && !_toolboxWindow.IsDisposed)
                {
                    try
                    {
                        _toolboxWindow.BeginInvoke(new Action(() => _toolboxWindow.Close()));
                    }
                    catch { }
                    _toolboxWindow = null;
                }

                if (_ribbonButton != null)
                {
                    _ribbonButton.Click -= OnRibbonButtonClicked;
                }

                try
                {
                    var tab = _host?.RibbonManager?.GetTab(TabId);
                    var group = tab?.GetGroup(GroupId);
                    group?.RemoveItem(ButtonId);
                }
                catch
                {
                    // Swallow UI teardown exceptions during host exit
                }

                _smallIcon?.Dispose();
                _smallIcon = null;

                _largeIcon?.Dispose();
                _largeIcon = null;

                _ribbonButton = null;
                _command = null;

                _host?.Logger?.LogInfo("[FAROSceneHelloWorld] Teardown complete.");
            }
            catch (Exception ex)
            {
                _host?.Logger?.LogError($"[FAROSceneHelloWorld] Error during shutdown: {ex.Message}", ex);
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
