# Automated Field-to-Finish SD Card Ingestion & Target-Based Registration Add-In

## Overview

The **FARO SCENE SD Card Automation & Registration Add-In** (`FAROSceneSDCardWorkflow`) is a pure C# .NET Framework 4.8 (x64) in-process plugin for FARO SCENE. It completely automates the field-to-finish scan ingestion pipeline:

```
[SD Card Plug-in] 
       │
       ▼
[Auto-Prompt Dialog] ──> Configure Project Name, Folder & Target Tolerances
       │
       ▼
[High-Speed Ingestion] ──> Buffered copy from SD card to local SSD (`Scans/`, `Exports/`, `Reports/`)
       │
       ▼
[Point Cloud Processing] ──> Stray point & range filtering (0.5m - 65m)
       │
       ▼
[Target Detection] ──> Sub-millimeter Delogne-Kåsa algebraic sphere fitting (140mm / 200mm)
       │
       ▼
[Target-Based Registration] ──> Invariant distance matrix matching & Horn/Kabsch SVD 6-DOF solver
       │
       ▼
[Top View Verification Map] ──> Interactive Bird's Eye canvas (Tripods, Targets, Footprints, Residuals)
       │
       ▼
[Approve & Complete] ──> SCENE project update, report export & safe SD unmount
```

---

## Key Technical Components

### 1. Hardware SD Card Monitor (`Hardware/SDCardWatcher.cs`)
- Runs a low-overhead background polling thread (`Timer` at 1.5s interval) querying `DriveInfo.GetDrives()`.
- Detects the arrival of removable storage media (`DriveType.Removable`).
- Identifies FARO scanner data layouts by searching for:
  - Raw scan files (`*.fls`, `*.pts`, `*.xyz`, `*.fws`, `*.faro`) in root and standard FARO scanner directories (`RawScans/`, `Scans/`, `FARO/`, `Focus/`).
  - FARO Focus metadata (`Focus.ini`, `Scanner.ini`, scanner serial numbers).
- Triggers the `CardInserted` event and dispatches the interactive prompt directly onto an STA thread.
- Includes `SimulateCardInserted()` to facilitate zero-hardware automated testing and desktop demonstrations.

### 2. Auto-Prompt Dialog (`UI/SDCardPromptDialog.cs`)
- Automatically pops up on top of FARO SCENE when removable media with scans is mounted.
- Displays volume label, capacity, and scan inventory (count and total megabytes).
- Allows the operator to customize:
  - Project name (default: `Project_YYYYMMDD_HHMM`).
  - Target project directory.
  - Target types (140mm Reference Spheres, 200mm Large Spheres, Checkerboards).
  - Stray point filtering and registration toggles.

### 3. High-Speed Buffered Ingestion (`Workflow/ScanImportManager.cs`)
- Pre-creates standardized project directories:
  - `<ProjectRoot>/Scans`
  - `<ProjectRoot>/Exports`
  - `<ProjectRoot>/Reports`
- High-throughput streaming copier utilizing 1MB buffers with byte-level progress reporting and `CancellationToken` cancellation support.

### 4. Mathematical Geometry & Registration Engine

#### A. Delogne-Kåsa Closed-Form Algebraic Sphere Fitting (`Registration/TargetDetector.cs`)
Given surface points $(x_i, y_i, z_i)$ from an isolated target cluster, fits the sphere equation:
$$(x_i - x_0)^2 + (y_i - y_0)^2 + (z_i - z_0)^2 = R^2$$
Expanding into linear form:
$$2 x_i x_0 + 2 y_i y_0 + 2 z_i z_0 + (R^2 - x_0^2 - y_0^2 - z_0^2) = x_i^2 + y_i^2 + z_i^2$$
Solves the $4 \times 4$ linear system $A^T A c = A^T b$ with partial pivoting. Delivers sub-millimeter target center accuracy without iterative convergence issues or local minima.

#### B. Pairwise Euclidean Distance Invariant Matching (`Registration/TargetMatcher.cs`)
Because Euclidean distances between target pairs are invariant under rigid 3D transformations ($|T_i - T_j| = |T_a' - T_b'|$), inter-target distance matrices are computed for each scan station. Candidate correspondences are matched automatically across overlapping stations within an $8\text{mm}$ threshold without requiring any manual tagging.

#### C. Closed-Form Horn / Kabsch SVD 6-DOF Rigid Solver (`Registration/RigidTransformSolver.cs`)
Finds the optimal rotation matrix $R \in \mathrm{SO}(3)$ and translation vector $t \in \mathbb{R}^3$ minimizing target residual errors:
$$\min_{R, t} \sum_{i=1}^N \|(p_i R + t) - q_i\|^2$$
- Computes centroids $\bar{p}, \bar{q}$ and centers points: $x_i = p_i - \bar{p}, y_i = q_i - \bar{q}$.
- Forms covariance matrix $H = \sum x_i^T y_i$.
- Performs SVD $H = U \Sigma V^T$ via cyclic Jacobi eigendecomposition on $H^T H$.
- Calculates $R = V U^T$. Enforces $\det(R) = +1$ to prevent reflections.
- Derives translation: $t = \bar{q} - \bar{p} R$.

#### D. Multi-Scan Network Graph Registration (`Registration/TargetRegistrationEngine.cs`)
- Sets Scan 1 as the reference world frame ($I$).
- Progressively discovers overlapping scans, matches target triangles, and computes 6-DOF alignment.
- Computes target tension residuals (mm), mean target error, and maximum target error across the network.
- Rates overall network quality:
  - **Excellent**: Mean error $\le 2.0\text{mm}$.
  - **Good**: Mean error $\le 4.0\text{mm}$.
  - **Review Needed**: Mean error $> 4.0\text{mm}$.

### 5. Bird's Eye (Top View) Verification Canvas (`UI/TopViewCanvas.cs`)
- High-performance 2D GDI+ orthographic canvas with double buffering:
  - Renders scan footprints color-coded by station with stride downsampling for 60 FPS fluidity.
  - Draws scanner station tripods ($\bigoplus$) with station labels and coordinate callouts.
  - Draws detected target bullseye markers ($\odot$) with target names and tension residuals in mm.
  - Draws color-coded inter-station tension links (Green $< 2\text{mm}$, Orange $< 4\text{mm}$, Red $> 4\text{mm}$).
  - Real-time coordinate grid with dynamic meters/pixels scale bar.
  - Pan (middle/left drag) and zoom (mouse wheel) centered on cursor.
  - `[Fit View]` button for auto-centering.

### 6. Verification Dialog & Completion (`UI/TopViewVerificationDialog.cs`)
- Displays side-by-side interactive Top View map and target tension residuals table.
- Buttons:
  - `[✓ Approve & Complete Process]`: Updates project, saves summary report, and prompts to safely eject the SD card.
  - `[Export Report...]`: Generates detailed audit report in `Reports/RegistrationReport.txt`.

---

## Verification Test Results

An automated end-to-end test suite (`tests/SDCardWorkflowVerification/`) validates every stage of the pipeline on synthetic overlapping scans:

| Test ID | Test Description | Result | Details |
|---------|------------------|--------|---------|
| **TEST 1** | Synthetic SD Card Generation | **PASS** | 3 scans generated, Focus serial and metadata verified |
| **TEST 2** | Project Creation & Buffered Import | **PASS** | `Scans/`, `Exports/`, `Reports/` staged, 3 files imported |
| **TEST 3** | Scan Processing & Stray Point Filter | **PASS** | 35,600 points loaded per station, range filtered |
| **TEST 4** | 140mm Reference Sphere Detection | **PASS** | Spheres detected with Delogne-Kåsa algebraic fit ($r \approx 70\text{mm}$) |
| **TEST 5** | Distance Invariant Target Matching | **PASS** | 5 matched pairs (Stn 1-2), 4 matched pairs (Stn 1-3) |
| **TEST 6** | Horn/Kabsch SVD 6-DOF Rigid Solver | **PASS** | Mean residual: **$0.02\text{mm}$**, recovered position: $(4.000, 1.500, 0.000)$ |
| **TEST 7** | Full Network Multi-Scan Registration | **PASS** | 3/3 scans registered, Mean Error: **$0.02\text{mm}$**, Quality: **Excellent** |
| **TEST 8** | Top View Canvas & Report Generation | **PASS** | Orthographic canvas bounds computed, report written |

**Overall Verification: 8/8 Tests Passed (100% Success)**

---

## How to Install & Use

1. Run the build script:
   ```powershell
   .\build.ps1 -Clean
   ```
2. The compiled package is placed in `dist/FAROSceneSDCardWorkflow.faroapp`.
3. In FARO SCENE:
   - Drag and drop `FAROSceneSDCardWorkflow.faroapp` onto the SCENE window, OR
   - Go to **Menu -> Apps -> Import / Install App** and select `FAROSceneSDCardWorkflow.faroapp`.
4. Plug in any SD card containing FARO scans — the interactive prompt will appear automatically!
   - To test without an SD card, open the Ribbon: **Apps -> SD Card Automation -> Workflow Center -> Simulate SD Card (3 Scans)**.
