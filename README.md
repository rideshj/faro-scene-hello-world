# FARO SCENE Open API - "Hello World" Add-In

A minimal, production-ready FARO SCENE extension built in C# (.NET Framework 4.8 x64). This add-in demonstrates how to integrate into the FARO SCENE ribbon interface, hook into the application lifecycle (`IApp` / `IAppHost`), query the active workspace for scan metrics, and display active scan points and open scans in a modal dialog.

For complete reverse-engineering insights, calling convention specifications, and runtime verification records, see [docs/TRACEABILITY.md](docs/TRACEABILITY.md).

---

## Architecture Overview

```
FARO SCENE Host (x64)
  │
  ├── App.xml Manifest (Metadata, Compatibility, Entry Point, Ribbon UI)
  │
  ├── IApp Lifecycle Controller (EntryPoint.cs)
  │     ├── Initialize(IAppHost host) ──> Registers Ribbon Tab, Group & Button
  │     └── Shutdown()                ──> Detaches events & frees UI handles
  │
  ├── Command Handler (CountPointsCommand.cs)
  │     ├── Inspects IAppHost.WorkspaceHost.ActiveWorkspace
  │     └── Iterates IWorkspace.Scans to aggregate loaded scans & points
  │
  └── UI Presentation (StatsDialog.cs)
        └── High-DPI Windows Forms modal dialog with copy-to-clipboard support
```

---

## Project Structure

```
SCENE/
├── build.ps1                                   # PowerShell build, verification, and packaging script
├── FAROSceneHelloWorld.sln                     # Visual Studio 2022 / MSBuild solution
├── README.md                                   # Documentation and deployment guide
├── .gitignore                                  # Git exclusion rules
│
├── lib/                                        # Reference assemblies for standalone / CI builds
│   ├── FARO.LS.SDK.AppLogic.dll                # SCENE App & UI Logic interface contracts
│   └── FARO.LS.SDK.DataLogic.dll               # SCENE Workspace & Scan Data model contracts
│
└── src/
    └── FAROSceneHelloWorld/
        ├── FAROSceneHelloWorld.csproj          # Project config (net48 x64, Private=False SDK references)
        ├── AppManifest.xml                     # FARO SCENE native manifest (required by CBVAppManifest)
        ├── App.xml                             # Managed Open API manifest
        ├── AppIcon.ico                         # Root application icon
        ├── EntryPoint.cs                       # IApp / IAppHost lifecycle entry point
        │
        ├── Commands/
        │   └── CountPointsCommand.cs           # Ribbon button event handler & scan point aggregator
        │
        ├── UI/
        │   ├── InteractiveToolboxWindow.cs     # Live diagnostic and interactive controller window
        │   └── StatsDialog.cs                  # Modal statistics dialog
        │
        ├── Properties/
        │   └── AssemblyInfo.cs                 # Assembly metadata, GUID, and versioning
        │
        └── Resources/
            ├── icon16.png                      # 16x16 Ribbon button icon
            ├── icon32.png                      # 32x32 Ribbon button icon
            └── app_icon.png                    # 64x64 App Manager icon
```

---

## Key Constraints & SDK Conventions

### 1. SDK Isolation (`Private=False` / `Copy Local=False`)
FARO SCENE loads add-ins into its running process space where `FARO.LS.SDK.AppLogic.dll` and `FARO.LS.SDK.DataLogic.dll` already reside. 
- In [FAROSceneHelloWorld.csproj](src/FAROSceneHelloWorld/FAROSceneHelloWorld.csproj), both SDK references are explicitly configured with:
  ```xml
  <Private>False</Private>
  <CopyLocal>False</CopyLocal>
  ```
- The build script (`build.ps1`) actively verifies that no SDK assemblies are present in the build output before generating the package.

### 2. Standalone & CI Build Portability
- The project looks for SCENE at `C:\Program Files\FARO\SCENE` (or custom path via `FARO_SCENE_DIR`).
- If SCENE is not installed on the build agent, it automatically falls back to the provided `lib/` reference assemblies.

### 3. Native App Loader & Export Weaving
FARO SCENE's native loader (`Scene_Interfaces.dll`) loads app DLLs via Win32 `LoadLibraryW` and checks for unmanaged C++ exports:
- `lsAppStartup(IntPtr context, IntPtr reserved)` -> returns integer status (non-zero for success).
- `lsAppShutdown(IntPtr context, IntPtr reserved)` -> handles teardown.

Pure managed C# DLLs lack these exports by default. `build.ps1` automatically performs IL post-processing (via `ildasm` and `ilasm`) to inject `.vtfixup`, `.vtentry`, and unmanaged `.export` declarations, producing a dual managed/native assembly compatible with SCENE's native loader.

---

## Building and Packaging

Run the build script from PowerShell:

```powershell
.\build.ps1 -Clean
```

Parameters:
- `-Configuration` : `Release` (default) or `Debug`
- `-Platform`      : `x64` (default)
- `-OutputDir`     : Target directory for packaged files (default `dist`)
- `-Clean`         : Cleans prior compilation and staging outputs

### Output Artifacts
The build produces two deployment packages in the `dist/` directory:
- `FAROSceneHelloWorld.faroapp` : Modern FARO App package (ZIP archive).
- `FAROSceneHelloWorld.fpp`     : FARO Plugin package (accepted by SCENE AppInstaller).

Inside the package:
```
FAROSceneHelloWorld.faroapp
├── AppManifest.xml
├── App.xml
├── AppIcon.ico
├── FAROSceneHelloWorld.dll
├── FARO.LS.SDK.AppLogic.dll
├── FARO.LS.SDK.DataLogic.dll
└── Resources/
    ├── icon16.png
    ├── icon32.png
    └── app_icon.png
```

---

## Installation in FARO SCENE

> [!IMPORTANT]
> **Administrator Privileges**: FARO SCENE installs apps into `C:\Program Files\FARO\SCENE\Apps\` (or `SCENE LT\Apps\`). If you encounter permission errors during installation, start FARO SCENE by right-clicking and choosing **Run as administrator**.

1. **Drag and Drop**: Launch FARO SCENE, then drag and drop `FAROSceneHelloWorld.fpp` (or `FAROSceneHelloWorld.faroapp`) directly onto the open FARO SCENE window.
2. **App Manager**:
   - In SCENE, navigate to **Apps** on the ribbon or open **Options -> Apps**.
   - Click **Install App** / **Import**, browse to `dist\FAROSceneHelloWorld.fpp` (or `.faroapp`), and select it.
3. Once enabled, navigate to the **Apps** ribbon tab. You will see the **Workspace Tools** group and the **Scan Stats** button.
4. Open a project (`.lsproj` or `.fls`), click **Scan Stats**, and view the modal dialog displaying active scan points and loaded scans.
